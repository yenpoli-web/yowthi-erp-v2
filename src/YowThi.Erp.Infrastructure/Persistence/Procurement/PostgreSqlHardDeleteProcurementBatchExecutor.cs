using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Procurement;

namespace YowThi.Erp.Infrastructure.Persistence.Procurement;

internal sealed class PostgreSqlHardDeleteProcurementBatchExecutor : IHardDeleteProcurementBatchExecutor
{
    private const string CommandType = "HardDeleteProcurementBatch";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlHardDeleteProcurementBatchExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public async ValueTask<ApplicationResult<HardDeleteProcurementBatchResult>> ExecuteAsync(
        HardDeleteProcurementBatchExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        var validationError = ProcurementBatchHardDeleteValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ApplicationResult<HardDeleteProcurementBatchResult>.Failure(validationError);
        }

        return await _transactionRunner.ExecuteAsync(
            async ct =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await AcquireAsync(execution, now, ct);
                if (acquisition.Kind == AcquireKind.Replay)
                {
                    return CommandTransactionDecision<ApplicationResult<HardDeleteProcurementBatchResult>>.Rollback(
                        ApplicationResult<HardDeleteProcurementBatchResult>.Success(acquisition.Result!));
                }

                if (acquisition.Kind == AcquireKind.Conflict)
                {
                    return Rollback(ApplicationErrorKind.Conflict, ProcurementTransactionLifecycleErrorCodes.IdempotencyKeyReused);
                }

                var batchId = execution.Command.ProcurementBatchId;
                var rowVersion = await LockBatchAsync(batchId, ct);
                if (rowVersion is null)
                {
                    return Rollback(ApplicationErrorKind.NotFound, ProcurementTransactionLifecycleErrorCodes.BatchNotFound);
                }

                if (rowVersion.Value != execution.Command.ExpectedRowVersion)
                {
                    return Rollback(ApplicationErrorKind.Conflict, ProcurementTransactionLifecycleErrorCodes.StaleRowVersion);
                }

                var entryIds = await ReadIdsAsync(
                    "SELECT id FROM procurement.procurement_entries WHERE procurement_batch_id = @batch_id ORDER BY id FOR UPDATE;",
                    "batch_id",
                    batchId,
                    ct);
                var processingExecutionIds = await ReadIdsAsync(
                    "SELECT id FROM processing.processing_executions WHERE procurement_batch_id = @batch_id ORDER BY id FOR UPDATE;",
                    "batch_id",
                    batchId,
                    ct);
                var outputIds = processingExecutionIds.Length == 0
                    ? []
                    : await ReadIdsAsync(
                        "SELECT id FROM processing.processing_execution_outputs WHERE processing_execution_id = ANY(@ids) ORDER BY id FOR UPDATE;",
                        "ids",
                        processingExecutionIds,
                        ct);
                var directPayableIds = await ReadIdsAsync(
                    "SELECT id FROM finance.payables WHERE procurement_batch_id = @batch_id ORDER BY id FOR UPDATE;",
                    "batch_id",
                    batchId,
                    ct);
                var transportPayableIds = await ReadIdsAsync(
                    """
                    SELECT payable.id
                    FROM finance.payables payable
                    WHERE payable.payable_kind = 'COMPANY_PICKUP_TRANSPORT'
                      AND EXISTS
                          (SELECT 1
                           FROM finance.payable_obligation_items obligation
                           WHERE obligation.payable_id = payable.id
                             AND obligation.procurement_batch_id = @batch_id)
                    ORDER BY payable.id
                    FOR UPDATE OF payable;
                    """,
                    "batch_id",
                    batchId,
                    ct);
                var affectedRevisionIds = await ReadIdsAsync(
                    """
                    SELECT DISTINCT sales_allocation_revision_id
                    FROM sales.sales_allocation_revision_items
                    WHERE procurement_batch_id = @batch_id
                    ORDER BY sales_allocation_revision_id;
                    """,
                    "batch_id",
                    batchId,
                    ct);
                var affectedOperationIds = await ReadIdsAsync(
                    """
                    SELECT DISTINCT operation.id
                    FROM inventory.inventory_operations operation
                    LEFT JOIN inventory.inventory_movements movement
                      ON movement.inventory_operation_id = operation.id
                    WHERE operation.procurement_entry_id = ANY(@entry_ids)
                       OR operation.processing_execution_id = ANY(@execution_ids)
                       OR operation.procurement_batch_id = @batch_id
                       OR movement.procurement_batch_id = @batch_id
                    ORDER BY operation.id;
                    """,
                    new Dictionary<string, object>
                    {
                        ["entry_ids"] = entryIds,
                        ["execution_ids"] = processingExecutionIds,
                        ["batch_id"] = batchId,
                    },
                    ct);

                if (outputIds.Length > 0)
                {
                    await CloseProcessingLaborAsync(outputIds, now, ct);
                }

                await DeletePendingOutboxAsync(
                    batchId,
                    entryIds,
                    processingExecutionIds,
                    directPayableIds.Concat(transportPayableIds).Distinct().ToArray(),
                    ct);
                await DeleteInventoryMovementsAsync(batchId, affectedOperationIds, ct);
                await DeleteSalesSourceReferencesAsync(batchId, affectedRevisionIds, ct);
                await DeleteInventoryOperationsAsync(affectedOperationIds, affectedRevisionIds, ct);
                await DeleteProcessingRowsAsync(processingExecutionIds, ct);
                await CloseFinanceAsync(batchId, entryIds, directPayableIds, transportPayableIds, now, ct);

                await ExecuteAsync(
                    "DELETE FROM procurement.procurement_entries WHERE procurement_batch_id = @batch_id;",
                    ct,
                    ("batch_id", batchId));
                await ExecuteAsync(
                    "DELETE FROM inventory.inventory_positions WHERE procurement_batch_id = @batch_id;",
                    ct,
                    ("batch_id", batchId));

                var deleted = await ExecuteAsync(
                    "DELETE FROM procurement.procurement_batches WHERE id = @batch_id AND row_version = @expected_row_version;",
                    ct,
                    ("batch_id", batchId),
                    ("expected_row_version", execution.Command.ExpectedRowVersion));
                if (deleted != 1)
                {
                    return Rollback(ApplicationErrorKind.Conflict, ProcurementTransactionLifecycleErrorCodes.StaleRowVersion);
                }

                var result = new HardDeleteProcurementBatchResult(batchId);
                var resultJson = JsonSerializer.Serialize(result, JsonOptions);
                await AppendAuditAsync(execution, rowVersion.Value, now, ct);
                await MarkSucceededAsync(execution.CommandId.Value, resultJson, now, ct);

                return CommandTransactionDecision<ApplicationResult<HardDeleteProcurementBatchResult>>.Commit(
                    ApplicationResult<HardDeleteProcurementBatchResult>.Success(result));
            },
            cancellationToken);
    }

    private async ValueTask<long?> LockBatchAsync(Guid batchId, CancellationToken ct)
    {
        await using var command = Sql(
            "SELECT row_version FROM procurement.procurement_batches WHERE id = @batch_id FOR UPDATE;");
        command.Parameters.AddWithValue("batch_id", batchId);
        var value = await command.ExecuteScalarAsync(ct);
        return value is long rowVersion ? rowVersion : null;
    }

    private async ValueTask CloseProcessingLaborAsync(Guid[] outputIds, DateTimeOffset now, CancellationToken ct)
    {
        Guid[] componentIds;
        Guid[] dailyWageIds;
        await using (var read = Sql(
            """
            SELECT source.processing_wage_component_id, component.employee_daily_wage_id
            FROM labor.processing_wage_component_sources source
            JOIN labor.processing_wage_components component
              ON component.id = source.processing_wage_component_id
            WHERE source.processing_execution_output_id = ANY(@output_ids)
            ORDER BY source.processing_wage_component_id
            FOR UPDATE OF source, component;
            """))
        {
            read.Parameters.AddWithValue("output_ids", outputIds);
            var components = new HashSet<Guid>();
            var wages = new HashSet<Guid>();
            await using var reader = await read.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                components.Add(reader.GetGuid(0));
                wages.Add(reader.GetGuid(1));
            }
            componentIds = components.OrderBy(id => id).ToArray();
            dailyWageIds = wages.OrderBy(id => id).ToArray();
        }

        if (componentIds.Length == 0)
        {
            return;
        }

        await using (var lockWages = Sql(
            "SELECT id FROM labor.employee_daily_wages WHERE id = ANY(@ids) ORDER BY id FOR UPDATE;"))
        {
            lockWages.Parameters.AddWithValue("ids", dailyWageIds);
            await using var reader = await lockWages.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) { }
        }

        await ExecuteAsync(
            "DELETE FROM labor.processing_wage_component_sources WHERE processing_execution_output_id = ANY(@ids);",
            ct,
            ("ids", outputIds));
        await ExecuteAsync(
            """
            DELETE FROM labor.processing_wage_components component
            WHERE component.id = ANY(@ids)
              AND NOT EXISTS
                  (SELECT 1 FROM labor.processing_wage_component_sources source
                   WHERE source.processing_wage_component_id = component.id);
            """,
            ct,
            ("ids", componentIds));
        await ExecuteAsync(
            """
            UPDATE labor.processing_wage_components component
            SET aggregated_quantity = source_total.quantity,
                amount_thb = floor(source_total.quantity * component.applied_wage_rate)::bigint
            FROM
            (
                SELECT processing_wage_component_id, SUM(quantity_snapshot) AS quantity
                FROM labor.processing_wage_component_sources
                WHERE processing_wage_component_id = ANY(@ids)
                GROUP BY processing_wage_component_id
            ) source_total
            WHERE component.id = source_total.processing_wage_component_id;
            """,
            ct,
            ("ids", componentIds));
        await ExecuteAsync(
            """
            UPDATE labor.employee_daily_wages wage
            SET processing_wage_total_thb = COALESCE(
                    (SELECT SUM(component.amount_thb)::bigint
                     FROM labor.processing_wage_components component
                     WHERE component.employee_daily_wage_id = wage.id), 0),
                total_wage_thb = COALESCE(
                    (SELECT SUM(component.amount_thb)::bigint
                     FROM labor.processing_wage_components component
                     WHERE component.employee_daily_wage_id = wage.id), 0)
                    + wage.sales_packaging_wage_total_thb,
                row_version = wage.row_version + 1
            WHERE wage.id = ANY(@ids);
            """,
            ct,
            ("ids", dailyWageIds));
        await ExecuteAsync(
            """
            UPDATE finance.payable_obligation_items obligation
            SET amount_thb = wage.total_wage_thb
            FROM labor.employee_daily_wages wage
            WHERE obligation.employee_daily_wage_id = wage.id
              AND wage.id = ANY(@ids);
            """,
            ct,
            ("ids", dailyWageIds));
        await ExecuteAsync(
            """
            UPDATE finance.payable_outstanding_positions position
            SET original_obligation_thb = wage.total_wage_thb,
                outstanding_thb = wage.total_wage_thb + position.adjustment_total_thb - position.settlement_total_thb,
                row_version = position.row_version + 1,
                updated_at = @now
            FROM finance.payables payable
            JOIN labor.employee_daily_wages wage ON wage.id = payable.employee_daily_wage_id
            WHERE position.payable_id = payable.id
              AND wage.id = ANY(@ids);
            """,
            ct,
            ("ids", dailyWageIds),
            ("now", now));
        await ExecuteAsync(
            """
            DELETE FROM system.outbox_messages
            WHERE message_type = 'labor.employee-daily-wage.confirmed'
              AND published_at IS NULL
              AND payload ->> 'employeeDailyWageId' = ANY(@ids);
            """,
            ct,
            ("ids", dailyWageIds.Select(id => id.ToString()).ToArray()));
    }

    private async ValueTask DeletePendingOutboxAsync(
        Guid batchId,
        Guid[] entryIds,
        Guid[] processingExecutionIds,
        Guid[] payableIds,
        CancellationToken ct)
    {
        await ExecuteAsync(
            """
            DELETE FROM system.outbox_messages
            WHERE published_at IS NULL
              AND (
                    payload ->> 'procurementBatchId' = @batch_id
                 OR payload ->> 'procurementEntryId' = ANY(@entry_ids)
                 OR payload ->> 'processingExecutionId' = ANY(@execution_ids)
                 OR payload ->> 'payableId' = ANY(@payable_ids)
              );
            """,
            ct,
            ("batch_id", batchId.ToString()),
            ("entry_ids", entryIds.Select(id => id.ToString()).ToArray()),
            ("execution_ids", processingExecutionIds.Select(id => id.ToString()).ToArray()),
            ("payable_ids", payableIds.Select(id => id.ToString()).ToArray()));
    }

    private async ValueTask DeleteInventoryMovementsAsync(
        Guid batchId,
        Guid[] affectedOperationIds,
        CancellationToken ct)
    {
        if (affectedOperationIds.Length > 0)
        {
            await ExecuteAsync(
                "DELETE FROM inventory.inventory_movements WHERE inventory_operation_id = ANY(@ids);",
                ct,
                ("ids", affectedOperationIds));
        }

        await ExecuteAsync(
            "DELETE FROM inventory.inventory_movements WHERE procurement_batch_id = @batch_id;",
            ct,
            ("batch_id", batchId));
    }

    private async ValueTask DeleteSalesSourceReferencesAsync(
        Guid batchId,
        Guid[] affectedRevisionIds,
        CancellationToken ct)
    {
        await ExecuteAsync(
            """
            DELETE FROM sales.sales_allocations allocation
            USING sales.sales_allocation_revision_items item
            WHERE allocation.sales_allocation_revision_item_id = item.id
              AND item.procurement_batch_id = @batch_id;
            """,
            ct,
            ("batch_id", batchId));
        await ExecuteAsync(
            "DELETE FROM sales.sales_allocation_revision_items WHERE procurement_batch_id = @batch_id;",
            ct,
            ("batch_id", batchId));

        if (affectedRevisionIds.Length > 0)
        {
            await ExecuteAsync(
                """
                DELETE FROM inventory.inventory_operations operation
                WHERE operation.sales_allocation_revision_id = ANY(@ids)
                  AND NOT EXISTS
                      (SELECT 1 FROM inventory.inventory_movements movement
                       WHERE movement.inventory_operation_id = operation.id);
                """,
                ct,
                ("ids", affectedRevisionIds));
            await ExecuteAsync(
                """
                DELETE FROM sales.sales_allocation_revisions revision
                WHERE revision.id = ANY(@ids)
                  AND NOT EXISTS
                      (SELECT 1 FROM sales.sales_allocation_revision_items item
                       WHERE item.sales_allocation_revision_id = revision.id);
                """,
                ct,
                ("ids", affectedRevisionIds));
        }
    }

    private async ValueTask DeleteInventoryOperationsAsync(
        Guid[] affectedOperationIds,
        Guid[] affectedRevisionIds,
        CancellationToken ct)
    {
        if (affectedOperationIds.Length > 0)
        {
            await ExecuteAsync(
                """
                DELETE FROM inventory.inventory_operations operation
                WHERE operation.id = ANY(@ids)
                  AND NOT EXISTS
                      (SELECT 1 FROM inventory.inventory_movements movement
                       WHERE movement.inventory_operation_id = operation.id);
                """,
                ct,
                ("ids", affectedOperationIds));
        }

        if (affectedRevisionIds.Length > 0)
        {
            await ExecuteAsync(
                """
                DELETE FROM inventory.inventory_operations operation
                WHERE operation.sales_allocation_revision_id = ANY(@ids)
                  AND NOT EXISTS
                      (SELECT 1 FROM inventory.inventory_movements movement
                       WHERE movement.inventory_operation_id = operation.id);
                """,
                ct,
                ("ids", affectedRevisionIds));
        }
    }

    private async ValueTask DeleteProcessingRowsAsync(Guid[] executionIds, CancellationToken ct)
    {
        if (executionIds.Length == 0)
        {
            return;
        }

        await ExecuteAsync(
            "DELETE FROM processing.processing_execution_inputs WHERE processing_execution_id = ANY(@ids);",
            ct,
            ("ids", executionIds));
        await ExecuteAsync(
            "DELETE FROM processing.processing_execution_outputs WHERE processing_execution_id = ANY(@ids);",
            ct,
            ("ids", executionIds));
        await ExecuteAsync(
            "DELETE FROM processing.processing_executions WHERE id = ANY(@ids);",
            ct,
            ("ids", executionIds));
    }

    private async ValueTask CloseFinanceAsync(
        Guid batchId,
        Guid[] entryIds,
        Guid[] directPayableIds,
        Guid[] transportPayableIds,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await ExecuteAsync(
            """
            DELETE FROM finance.company_pickup_transport_obligation_basis_items link
            WHERE link.transport_obligation_item_id IN
                  (SELECT id FROM finance.payable_obligation_items WHERE procurement_batch_id = @batch_id)
               OR link.transport_basis_id IN
                  (SELECT id FROM finance.company_pickup_transport_bases WHERE procurement_entry_id = ANY(@entry_ids));
            """,
            ct,
            ("batch_id", batchId),
            ("entry_ids", entryIds));

        if (directPayableIds.Length > 0)
        {
            await ExecuteAsync("DELETE FROM finance.payments WHERE payable_id = ANY(@ids);", ct, ("ids", directPayableIds));
            await ExecuteAsync("DELETE FROM finance.payable_adjustments WHERE payable_id = ANY(@ids);", ct, ("ids", directPayableIds));
            await ExecuteAsync("DELETE FROM finance.payable_outstanding_positions WHERE payable_id = ANY(@ids);", ct, ("ids", directPayableIds));
            await ExecuteAsync("DELETE FROM finance.payable_obligation_items WHERE payable_id = ANY(@ids);", ct, ("ids", directPayableIds));
        }

        await ExecuteAsync(
            "DELETE FROM finance.payable_obligation_items WHERE procurement_batch_id = @batch_id;",
            ct,
            ("batch_id", batchId));
        await ExecuteAsync(
            "DELETE FROM finance.company_pickup_transport_bases WHERE procurement_entry_id = ANY(@entry_ids);",
            ct,
            ("entry_ids", entryIds));

        if (transportPayableIds.Length > 0)
        {
            await ExecuteAsync(
                """
                UPDATE finance.payable_outstanding_positions position
                SET original_obligation_thb = COALESCE(
                        (SELECT SUM(item.amount_thb)::bigint
                         FROM finance.payable_obligation_items item
                         WHERE item.payable_id = position.payable_id), 0),
                    outstanding_thb = COALESCE(
                        (SELECT SUM(item.amount_thb)::bigint
                         FROM finance.payable_obligation_items item
                         WHERE item.payable_id = position.payable_id), 0)
                        + position.adjustment_total_thb - position.settlement_total_thb,
                    row_version = position.row_version + 1,
                    updated_at = @now
                WHERE position.payable_id = ANY(@ids);
                """,
                ct,
                ("ids", transportPayableIds),
                ("now", now));

            await ExecuteAsync(
                """
                DELETE FROM finance.payments payment
                WHERE payment.payable_id = ANY(@ids)
                  AND NOT EXISTS
                      (SELECT 1 FROM finance.payable_obligation_items item
                       WHERE item.payable_id = payment.payable_id);
                """,
                ct,
                ("ids", transportPayableIds));
            await ExecuteAsync(
                """
                DELETE FROM finance.payable_adjustments adjustment
                WHERE adjustment.payable_id = ANY(@ids)
                  AND NOT EXISTS
                      (SELECT 1 FROM finance.payable_obligation_items item
                       WHERE item.payable_id = adjustment.payable_id);
                """,
                ct,
                ("ids", transportPayableIds));
            await ExecuteAsync(
                """
                DELETE FROM finance.payable_outstanding_positions position
                WHERE position.payable_id = ANY(@ids)
                  AND NOT EXISTS
                      (SELECT 1 FROM finance.payable_obligation_items item
                       WHERE item.payable_id = position.payable_id);
                """,
                ct,
                ("ids", transportPayableIds));
            await ExecuteAsync(
                """
                DELETE FROM finance.payables payable
                WHERE payable.id = ANY(@ids)
                  AND payable.payable_kind = 'COMPANY_PICKUP_TRANSPORT'
                  AND NOT EXISTS
                      (SELECT 1 FROM finance.payable_obligation_items item
                       WHERE item.payable_id = payable.id);
                """,
                ct,
                ("ids", transportPayableIds));
        }

        if (directPayableIds.Length > 0)
        {
            await ExecuteAsync("DELETE FROM finance.payables WHERE id = ANY(@ids);", ct, ("ids", directPayableIds));
        }
    }

    private async ValueTask<Acquisition> AcquireAsync(
        HardDeleteProcurementBatchExecution execution,
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
            insert.Parameters.AddWithValue("command_id", execution.CommandId.Value);
            insert.Parameters.AddWithValue("command_type", CommandType);
            insert.Parameters.AddWithValue("request_hash", execution.RequestHash.Bytes.ToArray());
            insert.Parameters.AddWithValue("actor_account_id", execution.ActorAccountId.Value);
            insert.Parameters.AddWithValue("started_at", startedAt);
            if (await insert.ExecuteNonQueryAsync(ct) == 1)
            {
                return Acquisition.Acquired();
            }
        }

        await using var select = Sql(
            """
            SELECT command_type, request_hash, status, actor_account_id, result_payload::text
            FROM system.command_executions
            WHERE command_id = @command_id;
            """);
        select.Parameters.AddWithValue("command_id", execution.CommandId.Value);
        await using var reader = await select.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            throw new InvalidOperationException("HardDeleteProcurementBatch command conflict could not be loaded.");
        }

        if (!string.Equals(reader.GetString(0), CommandType, StringComparison.Ordinal)
            || reader.GetGuid(3) != execution.ActorAccountId.Value
            || !execution.RequestHash.Bytes.Span.SequenceEqual(reader.GetFieldValue<byte[]>(1)))
        {
            return Acquisition.Conflict();
        }

        if (!string.Equals(reader.GetString(2), "SUCCEEDED", StringComparison.Ordinal) || reader.IsDBNull(4))
        {
            throw new InvalidOperationException("A committed HardDeleteProcurementBatch command is not replayable.");
        }

        var replay = JsonSerializer.Deserialize<HardDeleteProcurementBatchResult>(reader.GetString(4), JsonOptions)
            ?? throw new InvalidOperationException("Stored HardDeleteProcurementBatch result could not be deserialized.");
        return Acquisition.Replay(replay);
    }

    private async ValueTask AppendAuditAsync(
        HardDeleteProcurementBatchExecution execution,
        long beforeRowVersion,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        var auditId = Guid.CreateVersion7();
        var key = JsonSerializer.Serialize(new { id = execution.Command.ProcurementBatchId }, JsonOptions);
        await ExecuteAsync(
            """
            INSERT INTO audit.audit_events
                (id, command_id, command_type, event_kind, actor_account_id, occurred_at, reason_text)
            VALUES
                (@id, @command_id, @command_type, 'HARD_DELETE', @actor, @occurred_at, NULL);
            """,
            ct,
            ("id", auditId),
            ("command_id", execution.CommandId.Value),
            ("command_type", CommandType),
            ("actor", execution.ActorAccountId.Value),
            ("occurred_at", occurredAt));
        await ExecuteAsync(
            """
            INSERT INTO audit.audit_event_subjects
                (audit_event_id, sequence, subject_kind, subject_key, change_kind,
                 before_row_version, after_row_version, change_summary)
            VALUES
                (@audit_id, 1, 'procurement.batch', CAST(@key AS jsonb), 'HARD_DELETE',
                 @before, NULL, NULL);
            """,
            ct,
            ("audit_id", auditId),
            ("key", key),
            ("before", beforeRowVersion));
    }

    private async ValueTask MarkSucceededAsync(Guid commandId, string resultJson, DateTimeOffset now, CancellationToken ct)
    {
        var affected = await ExecuteAsync(
            """
            UPDATE system.command_executions
            SET status = 'SUCCEEDED', result_payload = CAST(@result AS jsonb), executed_at = @now
            WHERE command_id = @command_id AND status = 'IN_PROGRESS';
            """,
            ct,
            ("result", resultJson),
            ("now", now),
            ("command_id", commandId));
        if (affected != 1)
        {
            throw new InvalidOperationException("HardDeleteProcurementBatch command could not transition to SUCCEEDED.");
        }
    }

    private async ValueTask<Guid[]> ReadIdsAsync(
        string sql,
        string parameterName,
        object parameterValue,
        CancellationToken ct)
    {
        return await ReadIdsAsync(sql, new Dictionary<string, object> { [parameterName] = parameterValue }, ct);
    }

    private async ValueTask<Guid[]> ReadIdsAsync(
        string sql,
        IReadOnlyDictionary<string, object> parameters,
        CancellationToken ct)
    {
        await using var command = Sql(sql);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ids.Add(reader.GetGuid(0));
        }
        return ids.ToArray();
    }

    private async ValueTask<int> ExecuteAsync(
        string sql,
        CancellationToken ct,
        params (string Name, object Value)[] parameters)
    {
        await using var command = Sql(sql);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }
        return await command.ExecuteNonQueryAsync(ct);
    }

    private NpgsqlCommand Sql(string sql)
    {
        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("HardDeleteProcurementBatch SQL requires an active command transaction.");
        return new NpgsqlCommand(
            sql,
            (NpgsqlConnection)_dbContext.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction());
    }

    private static CommandTransactionDecision<ApplicationResult<HardDeleteProcurementBatchResult>> Rollback(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<HardDeleteProcurementBatchResult>>.Rollback(
            ApplicationResult<HardDeleteProcurementBatchResult>.Failure(ApplicationError.Create(kind, code)));

    private enum AcquireKind { Acquired, Replay, Conflict }

    private sealed record Acquisition(AcquireKind Kind, HardDeleteProcurementBatchResult? Result)
    {
        public static Acquisition Acquired() => new(AcquireKind.Acquired, null);
        public static Acquisition Replay(HardDeleteProcurementBatchResult result) => new(AcquireKind.Replay, result);
        public static Acquisition Conflict() => new(AcquireKind.Conflict, null);
    }
}
