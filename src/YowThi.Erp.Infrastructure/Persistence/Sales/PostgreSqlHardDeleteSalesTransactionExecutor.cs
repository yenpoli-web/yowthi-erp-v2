using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Sales;

namespace YowThi.Erp.Infrastructure.Persistence.Sales;

internal sealed class PostgreSqlHardDeleteSalesTransactionExecutor : IHardDeleteSalesTransactionExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlHardDeleteSalesTransactionExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public ValueTask<ApplicationResult<HardDeleteSalesTransactionResult>> HardDeleteSaleAsync(
        HardDeleteSaleExecution execution,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            execution.CommandId,
            execution.RequestHash,
            execution.ActorAccountId,
            execution.Command.SalesId,
            execution.Command.ExpectedRowVersion,
            Target.Sale,
            SalesTransactionLifecycleValidation.Validate(execution.Command),
            cancellationToken);

    public ValueTask<ApplicationResult<HardDeleteSalesTransactionResult>> HardDeleteDetailAsync(
        HardDeleteSalesDetailExecution execution,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            execution.CommandId,
            execution.RequestHash,
            execution.ActorAccountId,
            execution.Command.SalesDetailId,
            execution.Command.ExpectedRowVersion,
            Target.Detail,
            SalesTransactionLifecycleValidation.Validate(execution.Command),
            cancellationToken);

    private async ValueTask<ApplicationResult<HardDeleteSalesTransactionResult>> ExecuteAsync(
        CommandId commandId,
        CommandRequestHash requestHash,
        ActorAccountId actorAccountId,
        Guid id,
        long expectedRowVersion,
        Target target,
        ApplicationError? validationError,
        CancellationToken cancellationToken)
    {
        if (validationError is not null)
        {
            return ApplicationResult<HardDeleteSalesTransactionResult>.Failure(validationError);
        }

        var commandType = target == Target.Sale ? "HardDeleteSale" : "HardDeleteSalesDetail";

        return await _transactionRunner.ExecuteAsync(
            async ct =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await AcquireAsync(
                    commandId.Value,
                    requestHash,
                    actorAccountId.Value,
                    commandType,
                    now,
                    ct);

                if (acquisition.Kind == AcquireKind.Replay)
                {
                    return CommandTransactionDecision<ApplicationResult<HardDeleteSalesTransactionResult>>.Rollback(
                        ApplicationResult<HardDeleteSalesTransactionResult>.Success(acquisition.Result!));
                }

                if (acquisition.Kind == AcquireKind.Conflict)
                {
                    return Rollback(ApplicationErrorKind.Conflict, SalesTransactionLifecycleErrorCodes.IdempotencyKeyReused);
                }

                var state = await LockTargetAsync(id, target, ct);
                if (state is null)
                {
                    return Rollback(
                        ApplicationErrorKind.NotFound,
                        target == Target.Sale
                            ? SalesTransactionLifecycleErrorCodes.SaleNotFound
                            : SalesTransactionLifecycleErrorCodes.DetailNotFound);
                }

                if (state.RowVersion != expectedRowVersion)
                {
                    return Rollback(ApplicationErrorKind.Conflict, SalesTransactionLifecycleErrorCodes.StaleRowVersion);
                }

                var closed = target == Target.Sale
                    ? await HardDeleteSaleClosureAsync(state.SalesId, expectedRowVersion, now, ct)
                    : await HardDeleteDetailClosureAsync(state.SalesId, id, expectedRowVersion, now, ct);
                if (!closed)
                {
                    return Rollback(ApplicationErrorKind.Conflict, SalesTransactionLifecycleErrorCodes.ClosureInvalid);
                }

                var result = new HardDeleteSalesTransactionResult(id);
                var resultJson = JsonSerializer.Serialize(result, JsonOptions);
                await AppendAuditAsync(
                    commandId.Value,
                    actorAccountId.Value,
                    commandType,
                    id,
                    target,
                    expectedRowVersion,
                    now,
                    ct);
                await MarkSucceededAsync(commandId.Value, resultJson, now, ct);

                return CommandTransactionDecision<ApplicationResult<HardDeleteSalesTransactionResult>>.Commit(
                    ApplicationResult<HardDeleteSalesTransactionResult>.Success(result));
            },
            cancellationToken);
    }

    private async ValueTask<TargetState?> LockTargetAsync(Guid id, Target target, CancellationToken ct)
    {
        if (target == Target.Sale)
        {
            await using var command = Sql(
                "SELECT id, row_version FROM sales.sales WHERE id = @id FOR UPDATE;");
            command.Parameters.AddWithValue("id", id);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            return new TargetState(reader.GetGuid(0), reader.GetInt64(1));
        }

        await using (var command = Sql(
            """
            SELECT detail.sales_id, detail.row_version
            FROM sales.sales_details detail
            JOIN sales.sales sale ON sale.id = detail.sales_id
            WHERE detail.id = @id
            FOR UPDATE OF detail, sale;
            """))
        {
            command.Parameters.AddWithValue("id", id);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            return new TargetState(reader.GetGuid(0), reader.GetInt64(1));
        }
    }

    private async ValueTask<bool> HardDeleteDetailClosureAsync(
        Guid salesId,
        Guid detailId,
        long expectedRowVersion,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!await ReverseDetailInventoryAsync(detailId, ct)) return false;

        await DeletePendingSalesOutboxAsync(salesId, ct);

        await using (var movements = Sql(
            """
            DELETE FROM inventory.inventory_movements movement
            USING sales.sales_allocation_revision_items item
            WHERE movement.sales_allocation_revision_item_id = item.id
              AND item.sales_detail_id = @detail_id;
            """))
        {
            movements.Parameters.AddWithValue("detail_id", detailId);
            await movements.ExecuteNonQueryAsync(ct);
        }

        await using (var allocations = Sql("DELETE FROM sales.sales_allocations WHERE sales_detail_id = @detail_id;"))
        {
            allocations.Parameters.AddWithValue("detail_id", detailId);
            await allocations.ExecuteNonQueryAsync(ct);
        }

        await using (var items = Sql("DELETE FROM sales.sales_allocation_revision_items WHERE sales_detail_id = @detail_id;"))
        {
            items.Parameters.AddWithValue("detail_id", detailId);
            await items.ExecuteNonQueryAsync(ct);
        }

        await DeleteEmptySalesInventoryOperationsAsync(salesId, ct);
        await DeleteEmptySalesRevisionsAsync(salesId, ct);
        await CloseReceivableDetailAsync(detailId, now, ct);

        await using var detail = Sql(
            "DELETE FROM sales.sales_details WHERE id = @detail_id AND row_version = @expected_row_version;");
        detail.Parameters.AddWithValue("detail_id", detailId);
        detail.Parameters.AddWithValue("expected_row_version", expectedRowVersion);
        return await detail.ExecuteNonQueryAsync(ct) == 1;
    }

    private async ValueTask<bool> HardDeleteSaleClosureAsync(
        Guid salesId,
        long expectedRowVersion,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!await ReverseSaleInventoryAsync(salesId, ct)) return false;

        var workRecordIds = await ReadSalesPackagingWorkRecordIdsAsync(salesId, ct);
        if (workRecordIds.Length > 0)
        {
            await CloseSalesPackagingWagesAsync(workRecordIds, now, ct);
            await DeletePendingSalesPackagingOutboxAsync(workRecordIds, ct);
        }

        await DeletePendingSalesOutboxAsync(salesId, ct);
        await CloseReceivableSaleAsync(salesId, ct);

        await using (var movements = Sql(
            """
            DELETE FROM inventory.inventory_movements movement
            USING inventory.inventory_operations operation
            WHERE movement.inventory_operation_id = operation.id
              AND (
                    operation.sales_id = @sales_id
                 OR operation.sales_allocation_revision_id IN
                    (SELECT id FROM sales.sales_allocation_revisions WHERE sales_id = @sales_id)
              );
            """))
        {
            movements.Parameters.AddWithValue("sales_id", salesId);
            await movements.ExecuteNonQueryAsync(ct);
        }

        await using (var operations = Sql(
            """
            DELETE FROM inventory.inventory_operations operation
            WHERE operation.sales_id = @sales_id
               OR operation.sales_allocation_revision_id IN
                  (SELECT id FROM sales.sales_allocation_revisions WHERE sales_id = @sales_id);
            """))
        {
            operations.Parameters.AddWithValue("sales_id", salesId);
            await operations.ExecuteNonQueryAsync(ct);
        }

        await using (var wageComponents = Sql(
            """
            DELETE FROM labor.sales_packaging_wage_components component
            USING sales_handling.sales_packaging_work_records work
            WHERE component.sales_packaging_work_record_id = work.id
              AND work.sales_id = @sales_id;
            """))
        {
            wageComponents.Parameters.AddWithValue("sales_id", salesId);
            await wageComponents.ExecuteNonQueryAsync(ct);
        }

        await using (var work = Sql("DELETE FROM sales_handling.sales_packaging_work_records WHERE sales_id = @sales_id;"))
        {
            work.Parameters.AddWithValue("sales_id", salesId);
            await work.ExecuteNonQueryAsync(ct);
        }

        await using (var allocations = Sql(
            """
            DELETE FROM sales.sales_allocations allocation
            USING sales.sales_details detail
            WHERE allocation.sales_detail_id = detail.id
              AND detail.sales_id = @sales_id;
            """))
        {
            allocations.Parameters.AddWithValue("sales_id", salesId);
            await allocations.ExecuteNonQueryAsync(ct);
        }

        await using (var items = Sql("DELETE FROM sales.sales_allocation_revision_items WHERE sales_id = @sales_id;"))
        {
            items.Parameters.AddWithValue("sales_id", salesId);
            await items.ExecuteNonQueryAsync(ct);
        }

        await using (var revisions = Sql("DELETE FROM sales.sales_allocation_revisions WHERE sales_id = @sales_id;"))
        {
            revisions.Parameters.AddWithValue("sales_id", salesId);
            await revisions.ExecuteNonQueryAsync(ct);
        }

        await using (var details = Sql("DELETE FROM sales.sales_details WHERE sales_id = @sales_id;"))
        {
            details.Parameters.AddWithValue("sales_id", salesId);
            await details.ExecuteNonQueryAsync(ct);
        }

        await using var sale = Sql(
            "DELETE FROM sales.sales WHERE id = @sales_id AND row_version = @expected_row_version;");
        sale.Parameters.AddWithValue("sales_id", salesId);
        sale.Parameters.AddWithValue("expected_row_version", expectedRowVersion);
        return await sale.ExecuteNonQueryAsync(ct) == 1;
    }

    private async ValueTask<bool> ReverseDetailInventoryAsync(Guid detailId, CancellationToken ct)
    {
        await using var command = Sql(
            """
            WITH target_delta AS
            (
                SELECT
                    movement.origin,
                    movement.procurement_batch_id,
                    movement.outsourced_supply_batch_id,
                    movement.inventory_object_kind,
                    movement.procurement_product_id,
                    movement.process_material_id,
                    movement.sales_product_id,
                    movement.storage_location_id,
                    movement.raw_source_kind,
                    movement.supplier_id,
                    SUM(movement.quantity_delta) AS quantity_delta
                FROM inventory.inventory_movements movement
                JOIN sales.sales_allocation_revision_items item
                  ON item.id = movement.sales_allocation_revision_item_id
                WHERE item.sales_detail_id = @detail_id
                GROUP BY
                    movement.origin,
                    movement.procurement_batch_id,
                    movement.outsourced_supply_batch_id,
                    movement.inventory_object_kind,
                    movement.procurement_product_id,
                    movement.process_material_id,
                    movement.sales_product_id,
                    movement.storage_location_id,
                    movement.raw_source_kind,
                    movement.supplier_id
            ),
            updated AS
            (
                UPDATE inventory.inventory_positions position
                SET balance_quantity = position.balance_quantity - target_delta.quantity_delta,
                    row_version = position.row_version + 1
                FROM target_delta
                WHERE position.origin = target_delta.origin
                  AND position.procurement_batch_id IS NOT DISTINCT FROM target_delta.procurement_batch_id
                  AND position.outsourced_supply_batch_id IS NOT DISTINCT FROM target_delta.outsourced_supply_batch_id
                  AND position.inventory_object_kind = target_delta.inventory_object_kind
                  AND position.procurement_product_id IS NOT DISTINCT FROM target_delta.procurement_product_id
                  AND position.process_material_id IS NOT DISTINCT FROM target_delta.process_material_id
                  AND position.sales_product_id IS NOT DISTINCT FROM target_delta.sales_product_id
                  AND position.storage_location_id = target_delta.storage_location_id
                  AND position.raw_source_kind IS NOT DISTINCT FROM target_delta.raw_source_kind
                  AND position.supplier_id IS NOT DISTINCT FROM target_delta.supplier_id
                RETURNING 1
            )
            SELECT
                (SELECT COUNT(*) FROM target_delta),
                (SELECT COUNT(*) FROM updated);
            """);
        command.Parameters.AddWithValue("detail_id", detailId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return reader.GetInt64(0) == reader.GetInt64(1);
    }

    private async ValueTask<bool> ReverseSaleInventoryAsync(Guid salesId, CancellationToken ct)
    {
        await using var command = Sql(
            """
            WITH target_operation AS
            (
                SELECT operation.id
                FROM inventory.inventory_operations operation
                WHERE operation.sales_id = @sales_id
                   OR operation.sales_allocation_revision_id IN
                      (SELECT id FROM sales.sales_allocation_revisions WHERE sales_id = @sales_id)
            ),
            target_delta AS
            (
                SELECT
                    movement.origin,
                    movement.procurement_batch_id,
                    movement.outsourced_supply_batch_id,
                    movement.inventory_object_kind,
                    movement.procurement_product_id,
                    movement.process_material_id,
                    movement.sales_product_id,
                    movement.storage_location_id,
                    movement.raw_source_kind,
                    movement.supplier_id,
                    SUM(movement.quantity_delta) AS quantity_delta
                FROM inventory.inventory_movements movement
                JOIN target_operation operation ON operation.id = movement.inventory_operation_id
                GROUP BY
                    movement.origin,
                    movement.procurement_batch_id,
                    movement.outsourced_supply_batch_id,
                    movement.inventory_object_kind,
                    movement.procurement_product_id,
                    movement.process_material_id,
                    movement.sales_product_id,
                    movement.storage_location_id,
                    movement.raw_source_kind,
                    movement.supplier_id
            ),
            updated AS
            (
                UPDATE inventory.inventory_positions position
                SET balance_quantity = position.balance_quantity - target_delta.quantity_delta,
                    row_version = position.row_version + 1
                FROM target_delta
                WHERE position.origin = target_delta.origin
                  AND position.procurement_batch_id IS NOT DISTINCT FROM target_delta.procurement_batch_id
                  AND position.outsourced_supply_batch_id IS NOT DISTINCT FROM target_delta.outsourced_supply_batch_id
                  AND position.inventory_object_kind = target_delta.inventory_object_kind
                  AND position.procurement_product_id IS NOT DISTINCT FROM target_delta.procurement_product_id
                  AND position.process_material_id IS NOT DISTINCT FROM target_delta.process_material_id
                  AND position.sales_product_id IS NOT DISTINCT FROM target_delta.sales_product_id
                  AND position.storage_location_id = target_delta.storage_location_id
                  AND position.raw_source_kind IS NOT DISTINCT FROM target_delta.raw_source_kind
                  AND position.supplier_id IS NOT DISTINCT FROM target_delta.supplier_id
                RETURNING 1
            )
            SELECT
                (SELECT COUNT(*) FROM target_delta),
                (SELECT COUNT(*) FROM updated);
            """);
        command.Parameters.AddWithValue("sales_id", salesId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return reader.GetInt64(0) == reader.GetInt64(1);
    }

    private async ValueTask CloseReceivableDetailAsync(Guid detailId, DateTimeOffset now, CancellationToken ct)
    {
        Guid? receivableId = null;
        await using (var read = Sql(
            "SELECT receivable_id FROM finance.receivable_obligation_items WHERE sales_detail_id = @detail_id FOR UPDATE;"))
        {
            read.Parameters.AddWithValue("detail_id", detailId);
            var value = await read.ExecuteScalarAsync(ct);
            if (value is Guid id) receivableId = id;
        }

        if (receivableId is null) return;

        await using (var lockOutstanding = Sql(
            "SELECT receivable_id FROM finance.receivable_outstanding_positions WHERE receivable_id = @id FOR UPDATE;"))
        {
            lockOutstanding.Parameters.AddWithValue("id", receivableId.Value);
            await lockOutstanding.ExecuteScalarAsync(ct);
        }

        await using (var obligation = Sql("DELETE FROM finance.receivable_obligation_items WHERE sales_detail_id = @detail_id;"))
        {
            obligation.Parameters.AddWithValue("detail_id", detailId);
            await obligation.ExecuteNonQueryAsync(ct);
        }

        await using var outstanding = Sql(
            """
            UPDATE finance.receivable_outstanding_positions position
            SET original_obligation_thb = COALESCE(
                    (SELECT SUM(item.amount_thb)::bigint
                     FROM finance.receivable_obligation_items item
                     WHERE item.receivable_id = position.receivable_id), 0),
                outstanding_thb = COALESCE(
                    (SELECT SUM(item.amount_thb)::bigint
                     FROM finance.receivable_obligation_items item
                     WHERE item.receivable_id = position.receivable_id), 0)
                    + position.adjustment_total_thb - position.settlement_total_thb,
                row_version = position.row_version + 1,
                updated_at = @now
            WHERE position.receivable_id = @id;
            """);
        outstanding.Parameters.AddWithValue("id", receivableId.Value);
        outstanding.Parameters.AddWithValue("now", now);
        await outstanding.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask CloseReceivableSaleAsync(Guid salesId, CancellationToken ct)
    {
        Guid? receivableId = null;
        await using (var read = Sql("SELECT id FROM finance.receivables WHERE sales_id = @sales_id FOR UPDATE;"))
        {
            read.Parameters.AddWithValue("sales_id", salesId);
            var value = await read.ExecuteScalarAsync(ct);
            if (value is Guid id) receivableId = id;
        }

        if (receivableId is null) return;

        await using (var pendingReceiptOutbox = Sql(
            """
            DELETE FROM system.outbox_messages
            WHERE message_type = 'finance.receipt-recorded'
              AND published_at IS NULL
              AND payload ->> 'receivableId' = @receivable_id_text;
            """))
        {
            pendingReceiptOutbox.Parameters.AddWithValue("receivable_id_text", receivableId.Value.ToString());
            await pendingReceiptOutbox.ExecuteNonQueryAsync(ct);
        }

        await using (var receipts = Sql("DELETE FROM finance.receipts WHERE receivable_id = @id;"))
        {
            receipts.Parameters.AddWithValue("id", receivableId.Value);
            await receipts.ExecuteNonQueryAsync(ct);
        }

        await using (var outstanding = Sql("DELETE FROM finance.receivable_outstanding_positions WHERE receivable_id = @id;"))
        {
            outstanding.Parameters.AddWithValue("id", receivableId.Value);
            await outstanding.ExecuteNonQueryAsync(ct);
        }

        await using (var items = Sql("DELETE FROM finance.receivable_obligation_items WHERE receivable_id = @id;"))
        {
            items.Parameters.AddWithValue("id", receivableId.Value);
            await items.ExecuteNonQueryAsync(ct);
        }

        await using var receivable = Sql("DELETE FROM finance.receivables WHERE id = @id;");
        receivable.Parameters.AddWithValue("id", receivableId.Value);
        await receivable.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask<Guid[]> ReadSalesPackagingWorkRecordIdsAsync(Guid salesId, CancellationToken ct)
    {
        await using var command = Sql(
            "SELECT id FROM sales_handling.sales_packaging_work_records WHERE sales_id = @sales_id ORDER BY id FOR UPDATE;");
        command.Parameters.AddWithValue("sales_id", salesId);
        var result = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(reader.GetGuid(0));
        return result.ToArray();
    }

    private async ValueTask CloseSalesPackagingWagesAsync(Guid[] workRecordIds, DateTimeOffset now, CancellationToken ct)
    {
        Guid[] dailyWageIds;
        await using (var read = Sql(
            """
            SELECT component.employee_daily_wage_id
            FROM labor.sales_packaging_wage_components component
            WHERE component.sales_packaging_work_record_id = ANY(@work_record_ids)
            ORDER BY component.employee_daily_wage_id
            FOR UPDATE OF component;
            """))
        {
            read.Parameters.AddWithValue("work_record_ids", workRecordIds);
            var ids = new HashSet<Guid>();
            await using var reader = await read.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) ids.Add(reader.GetGuid(0));
            dailyWageIds = ids.OrderBy(id => id).ToArray();
        }

        if (dailyWageIds.Length == 0) return;

        await using (var lockWages = Sql(
            "SELECT id FROM labor.employee_daily_wages WHERE id = ANY(@ids) ORDER BY id FOR UPDATE;"))
        {
            lockWages.Parameters.AddWithValue("ids", dailyWageIds);
            await using var reader = await lockWages.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) { }
        }

        await using (var deleteComponents = Sql(
            "DELETE FROM labor.sales_packaging_wage_components WHERE sales_packaging_work_record_id = ANY(@work_record_ids);"))
        {
            deleteComponents.Parameters.AddWithValue("work_record_ids", workRecordIds);
            await deleteComponents.ExecuteNonQueryAsync(ct);
        }

        await using (var recalcWages = Sql(
            """
            UPDATE labor.employee_daily_wages wage
            SET sales_packaging_wage_total_thb = COALESCE(
                    (SELECT SUM(component.wage_amount_thb)::bigint
                     FROM labor.sales_packaging_wage_components component
                     WHERE component.employee_daily_wage_id = wage.id), 0),
                total_wage_thb = wage.processing_wage_total_thb
                    + COALESCE(
                        (SELECT SUM(component.wage_amount_thb)::bigint
                         FROM labor.sales_packaging_wage_components component
                         WHERE component.employee_daily_wage_id = wage.id), 0),
                row_version = wage.row_version + 1
            WHERE wage.id = ANY(@daily_wage_ids);
            """))
        {
            recalcWages.Parameters.AddWithValue("daily_wage_ids", dailyWageIds);
            await recalcWages.ExecuteNonQueryAsync(ct);
        }

        await using (var obligation = Sql(
            """
            UPDATE finance.payable_obligation_items obligation
            SET amount_thb = wage.total_wage_thb
            FROM labor.employee_daily_wages wage
            WHERE obligation.employee_daily_wage_id = wage.id
              AND wage.id = ANY(@daily_wage_ids);
            """))
        {
            obligation.Parameters.AddWithValue("daily_wage_ids", dailyWageIds);
            await obligation.ExecuteNonQueryAsync(ct);
        }

        await using (var outstanding = Sql(
            """
            UPDATE finance.payable_outstanding_positions position
            SET original_obligation_thb = wage.total_wage_thb,
                outstanding_thb = wage.total_wage_thb + position.adjustment_total_thb - position.settlement_total_thb,
                row_version = position.row_version + 1,
                updated_at = @now
            FROM finance.payables payable
            JOIN labor.employee_daily_wages wage ON wage.id = payable.employee_daily_wage_id
            WHERE position.payable_id = payable.id
              AND wage.id = ANY(@daily_wage_ids);
            """))
        {
            outstanding.Parameters.AddWithValue("daily_wage_ids", dailyWageIds);
            outstanding.Parameters.AddWithValue("now", now);
            await outstanding.ExecuteNonQueryAsync(ct);
        }

        await using var pendingWageOutbox = Sql(
            """
            DELETE FROM system.outbox_messages
            WHERE message_type = 'labor.employee-daily-wage.confirmed'
              AND published_at IS NULL
              AND payload ->> 'employeeDailyWageId' = ANY(@daily_wage_id_texts);
            """);
        pendingWageOutbox.Parameters.AddWithValue("daily_wage_id_texts", dailyWageIds.Select(id => id.ToString()).ToArray());
        await pendingWageOutbox.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask DeletePendingSalesPackagingOutboxAsync(Guid[] workRecordIds, CancellationToken ct)
    {
        await using var command = Sql(
            """
            DELETE FROM system.outbox_messages
            WHERE message_type = 'sales-handling.work-record.recorded'
              AND published_at IS NULL
              AND payload ->> 'workRecordId' = ANY(@ids);
            """);
        command.Parameters.AddWithValue("ids", workRecordIds.Select(id => id.ToString()).ToArray());
        await command.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask DeletePendingSalesOutboxAsync(Guid salesId, CancellationToken ct)
    {
        await using var command = Sql(
            """
            DELETE FROM system.outbox_messages
            WHERE message_type IN ('sales.confirmed', 'sales.allocation-revised')
              AND published_at IS NULL
              AND payload ->> 'salesId' = @sales_id_text;
            """);
        command.Parameters.AddWithValue("sales_id_text", salesId.ToString());
        await command.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask DeleteEmptySalesInventoryOperationsAsync(Guid salesId, CancellationToken ct)
    {
        await using var command = Sql(
            """
            DELETE FROM inventory.inventory_operations operation
            WHERE (
                    operation.sales_id = @sales_id
                 OR operation.sales_allocation_revision_id IN
                    (SELECT id FROM sales.sales_allocation_revisions WHERE sales_id = @sales_id)
                  )
              AND NOT EXISTS
                  (SELECT 1 FROM inventory.inventory_movements movement
                   WHERE movement.inventory_operation_id = operation.id);
            """);
        command.Parameters.AddWithValue("sales_id", salesId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask DeleteEmptySalesRevisionsAsync(Guid salesId, CancellationToken ct)
    {
        await using var command = Sql(
            """
            DELETE FROM sales.sales_allocation_revisions revision
            WHERE revision.sales_id = @sales_id
              AND NOT EXISTS
                  (SELECT 1 FROM sales.sales_allocation_revision_items item
                   WHERE item.sales_allocation_revision_id = revision.id);
            """);
        command.Parameters.AddWithValue("sales_id", salesId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask<Acquisition> AcquireAsync(
        Guid commandId,
        CommandRequestHash requestHash,
        Guid actorAccountId,
        string commandType,
        DateTimeOffset startedAt,
        CancellationToken ct)
    {
        await using (var insert = Sql(
            """
            INSERT INTO system.command_executions
                (command_id, command_type, request_hash, status, result_payload,
                 actor_account_id, started_at, executed_at)
            VALUES
                (@command_id, @command_type, @request_hash, 'IN_PROGRESS', NULL,
                 @actor_account_id, @started_at, NULL)
            ON CONFLICT (command_id) DO NOTHING;
            """))
        {
            insert.Parameters.AddWithValue("command_id", commandId);
            insert.Parameters.AddWithValue("command_type", commandType);
            insert.Parameters.AddWithValue("request_hash", requestHash.Bytes.ToArray());
            insert.Parameters.AddWithValue("actor_account_id", actorAccountId);
            insert.Parameters.AddWithValue("started_at", startedAt);
            if (await insert.ExecuteNonQueryAsync(ct) == 1) return Acquisition.Acquired();
        }

        await using var select = Sql(
            """
            SELECT command_type, request_hash, status, actor_account_id, result_payload::text
            FROM system.command_executions
            WHERE command_id = @command_id;
            """);
        select.Parameters.AddWithValue("command_id", commandId);
        await using var reader = await select.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException("Sales hard-delete command conflict could not be loaded.");

        if (!string.Equals(reader.GetString(0), commandType, StringComparison.Ordinal)
            || reader.GetGuid(3) != actorAccountId
            || !requestHash.Bytes.Span.SequenceEqual(reader.GetFieldValue<byte[]>(1)))
        {
            return Acquisition.Conflict();
        }

        if (!string.Equals(reader.GetString(2), "SUCCEEDED", StringComparison.Ordinal) || reader.IsDBNull(4))
            throw new InvalidOperationException($"A committed {commandType} command is not replayable.");

        var replay = JsonSerializer.Deserialize<HardDeleteSalesTransactionResult>(reader.GetString(4), JsonOptions)
            ?? throw new InvalidOperationException($"Stored {commandType} result could not be deserialized.");
        return Acquisition.Replay(replay);
    }

    private async ValueTask AppendAuditAsync(
        Guid commandId,
        Guid actorAccountId,
        string commandType,
        Guid id,
        Target target,
        long beforeRowVersion,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        var auditId = Guid.CreateVersion7();
        var key = JsonSerializer.Serialize(new { id }, JsonOptions);
        await using (var audit = Sql(
            """
            INSERT INTO audit.audit_events
                (id, command_id, command_type, event_kind, actor_account_id, occurred_at, reason_text)
            VALUES
                (@id, @command_id, @command_type, 'HARD_DELETE', @actor, @occurred_at, NULL);
            """))
        {
            audit.Parameters.AddWithValue("id", auditId);
            audit.Parameters.AddWithValue("command_id", commandId);
            audit.Parameters.AddWithValue("command_type", commandType);
            audit.Parameters.AddWithValue("actor", actorAccountId);
            audit.Parameters.AddWithValue("occurred_at", occurredAt);
            await audit.ExecuteNonQueryAsync(ct);
        }

        await using var subject = Sql(
            """
            INSERT INTO audit.audit_event_subjects
                (audit_event_id, sequence, subject_kind, subject_key, change_kind,
                 before_row_version, after_row_version, change_summary)
            VALUES
                (@audit_id, 1, @kind, CAST(@key AS jsonb), 'HARD_DELETE', @before, NULL, NULL);
            """);
        subject.Parameters.AddWithValue("audit_id", auditId);
        subject.Parameters.AddWithValue("kind", target == Target.Sale ? "sales.sale" : "sales.detail");
        subject.Parameters.AddWithValue("key", key);
        subject.Parameters.AddWithValue("before", beforeRowVersion);
        await subject.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask MarkSucceededAsync(Guid commandId, string resultJson, DateTimeOffset now, CancellationToken ct)
    {
        await using var command = Sql(
            """
            UPDATE system.command_executions
            SET status = 'SUCCEEDED', result_payload = CAST(@result AS jsonb), executed_at = @now
            WHERE command_id = @command_id AND status = 'IN_PROGRESS';
            """);
        command.Parameters.AddWithValue("result", resultJson);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("command_id", commandId);
        if (await command.ExecuteNonQueryAsync(ct) != 1)
            throw new InvalidOperationException("Sales hard-delete command could not transition to SUCCEEDED.");
    }

    private NpgsqlCommand Sql(string sql)
    {
        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Sales hard-delete SQL requires an active command transaction.");
        return new NpgsqlCommand(
            sql,
            (NpgsqlConnection)_dbContext.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction());
    }

    private static CommandTransactionDecision<ApplicationResult<HardDeleteSalesTransactionResult>> Rollback(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<HardDeleteSalesTransactionResult>>.Rollback(
            ApplicationResult<HardDeleteSalesTransactionResult>.Failure(ApplicationError.Create(kind, code)));

    private sealed record TargetState(Guid SalesId, long RowVersion);
    private enum Target { Sale, Detail }
    private enum AcquireKind { Acquired, Replay, Conflict }
    private sealed record Acquisition(AcquireKind Kind, HardDeleteSalesTransactionResult? Result)
    {
        public static Acquisition Acquired() => new(AcquireKind.Acquired, null);
        public static Acquisition Replay(HardDeleteSalesTransactionResult result) => new(AcquireKind.Replay, result);
        public static Acquisition Conflict() => new(AcquireKind.Conflict, null);
    }
}
