using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Outsourced;

namespace YowThi.Erp.Infrastructure.Persistence.Outsourced;

internal sealed class PostgreSqlHardDeleteOutsourcedTransactionExecutor : IHardDeleteOutsourcedTransactionExecutor
{
    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlHardDeleteOutsourcedTransactionExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public ValueTask<ApplicationResult<HardDeleteOutsourcedTransactionResult>> HardDeleteBatchAsync(
        HardDeleteOutsourcedSupplyBatchExecution execution,
        CancellationToken cancellationToken) =>
        ExecuteAsync(execution.CommandId, execution.RequestHash, execution.ActorAccountId,
            execution.Command.OutsourcedSupplyBatchId, execution.Command.ExpectedRowVersion,
            TargetKind.Batch, OutsourcedTransactionLifecycleValidation.Validate(execution.Command), cancellationToken);

    public ValueTask<ApplicationResult<HardDeleteOutsourcedTransactionResult>> HardDeleteDetailAsync(
        HardDeleteOutsourcedSupplyDetailExecution execution,
        CancellationToken cancellationToken) =>
        ExecuteAsync(execution.CommandId, execution.RequestHash, execution.ActorAccountId,
            execution.Command.OutsourcedSupplyDetailId, execution.Command.ExpectedRowVersion,
            TargetKind.Detail, OutsourcedTransactionLifecycleValidation.Validate(execution.Command), cancellationToken);

    private async ValueTask<ApplicationResult<HardDeleteOutsourcedTransactionResult>> ExecuteAsync(
        CommandId commandId,
        CommandRequestHash requestHash,
        ActorAccountId actorAccountId,
        Guid id,
        long expectedRowVersion,
        TargetKind target,
        ApplicationError? validationError,
        CancellationToken cancellationToken)
    {
        if (validationError is not null)
            return ApplicationResult<HardDeleteOutsourcedTransactionResult>.Failure(validationError);

        var commandType = target == TargetKind.Batch
            ? "HardDeleteOutsourcedSupplyBatch"
            : "HardDeleteOutsourcedSupplyDetail";

        return await _transactionRunner.ExecuteAsync(async ct =>
        {
            var now = _timeProvider.GetUtcNow();
            var acquisition = await AcquireAsync(commandId.Value, requestHash, actorAccountId.Value, commandType, now, ct);
            if (acquisition.Kind == AcquireKind.Replay)
            {
                return CommandTransactionDecision<ApplicationResult<HardDeleteOutsourcedTransactionResult>>.Rollback(
                    ApplicationResult<HardDeleteOutsourcedTransactionResult>.Success(acquisition.Result!));
            }
            if (acquisition.Kind == AcquireKind.Conflict)
                return Rollback(ApplicationErrorKind.Conflict, OutsourcedTransactionLifecycleErrorCodes.IdempotencyKeyReused);

            var state = await LockTargetAsync(id, target, ct);
            if (state is null)
            {
                return Rollback(ApplicationErrorKind.NotFound,
                    target == TargetKind.Batch
                        ? OutsourcedTransactionLifecycleErrorCodes.BatchNotFound
                        : OutsourcedTransactionLifecycleErrorCodes.DetailNotFound);
            }
            if (state.RowVersion != expectedRowVersion)
                return Rollback(ApplicationErrorKind.Conflict, OutsourcedTransactionLifecycleErrorCodes.StaleRowVersion);

            var closed = target == TargetKind.Batch
                ? await HardDeleteBatchClosureAsync(id, expectedRowVersion, ct)
                : await HardDeleteDetailClosureAsync(state.BatchId, id, expectedRowVersion, ct);
            if (!closed)
                return Rollback(ApplicationErrorKind.Conflict, OutsourcedTransactionLifecycleErrorCodes.HardDeleteClosureInvalid);

            var result = new HardDeleteOutsourcedTransactionResult(id);
            var resultJson = JsonSerializer.Serialize(result, StoredJsonOptions);
            await AppendAuditAsync(commandId.Value, actorAccountId.Value, commandType, id, target, state.RowVersion, now, ct);
            await CompleteAsync(commandId.Value, resultJson, now, ct);

            return CommandTransactionDecision<ApplicationResult<HardDeleteOutsourcedTransactionResult>>.Commit(
                ApplicationResult<HardDeleteOutsourcedTransactionResult>.Success(result));
        }, cancellationToken);
    }

    private async ValueTask<TargetState?> LockTargetAsync(Guid id, TargetKind target, CancellationToken ct)
    {
        if (target == TargetKind.Batch)
        {
            await using var batch = Sql("SELECT id, row_version FROM outsourced.outsourced_supply_batches WHERE id=@id FOR UPDATE;");
            batch.Parameters.AddWithValue("id", id);
            await using var reader = await batch.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            return new TargetState(reader.GetGuid(0), reader.GetInt64(1));
        }

        await using var detail = Sql("""
            SELECT detail.outsourced_supply_batch_id, detail.row_version
            FROM outsourced.outsourced_supply_details detail
            JOIN outsourced.outsourced_supply_batches batch ON batch.id = detail.outsourced_supply_batch_id
            WHERE detail.id=@id
            FOR UPDATE OF detail, batch;
            """);
        detail.Parameters.AddWithValue("id", id);
        await using var detailReader = await detail.ExecuteReaderAsync(ct);
        if (!await detailReader.ReadAsync(ct)) return null;
        return new TargetState(detailReader.GetGuid(0), detailReader.GetInt64(1));
    }

    private async ValueTask<bool> HardDeleteDetailClosureAsync(Guid batchId, Guid detailId, long expectedRowVersion, CancellationToken ct)
    {
        await ReverseDetailInventoryAsync(detailId, ct);
        var payableIds = await ReadIdsAsync(
            "SELECT id FROM finance.payables WHERE outsourced_supply_detail_id=@detail_id ORDER BY id FOR UPDATE;",
            "detail_id", detailId, ct);
        await DeletePendingOutboxAsync(includeBatch: false, batchId, [detailId], payableIds, ct);
        await DeleteDetailInventoryRowsAsync(detailId, ct);
        await DeleteFinanceAsync(payableIds, ct);

        await using var delete = Sql("DELETE FROM outsourced.outsourced_supply_details WHERE id=@id AND row_version=@version;");
        delete.Parameters.AddWithValue("id", detailId);
        delete.Parameters.AddWithValue("version", expectedRowVersion);
        return await delete.ExecuteNonQueryAsync(ct) == 1;
    }

    private async ValueTask<bool> HardDeleteBatchClosureAsync(Guid batchId, long expectedRowVersion, CancellationToken ct)
    {
        var detailIds = await ReadIdsAsync(
            "SELECT id FROM outsourced.outsourced_supply_details WHERE outsourced_supply_batch_id=@batch_id ORDER BY id FOR UPDATE;",
            "batch_id", batchId, ct);
        var payableIds = detailIds.Length == 0
            ? []
            : await ReadIdsAsync(
                "SELECT id FROM finance.payables WHERE outsourced_supply_detail_id = ANY(@detail_ids) ORDER BY id FOR UPDATE;",
                "detail_ids", detailIds, ct);
        var revisionIds = await ReadIdsAsync(
            "SELECT DISTINCT sales_allocation_revision_id FROM sales.sales_allocation_revision_items WHERE outsourced_supply_batch_id=@batch_id ORDER BY sales_allocation_revision_id;",
            "batch_id", batchId, ct);
        var directOperationIds = detailIds.Length == 0
            ? []
            : await ReadIdsAsync(
                "SELECT id FROM inventory.inventory_operations WHERE outsourced_supply_detail_id = ANY(@detail_ids) ORDER BY id;",
                "detail_ids", detailIds, ct);
        var movementOperationIds = await ReadIdsAsync(
            "SELECT DISTINCT inventory_operation_id FROM inventory.inventory_movements WHERE outsourced_supply_batch_id=@batch_id ORDER BY inventory_operation_id;",
            "batch_id", batchId, ct);
        var affectedOperationIds = directOperationIds
            .Concat(movementOperationIds)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        await DeletePendingOutboxAsync(includeBatch: true, batchId, detailIds, payableIds, ct);
        await DeleteBatchInventoryMovementsAsync(batchId, directOperationIds, ct);
        await DeleteSalesSourceReferencesAsync(batchId, revisionIds, ct);
        await DeleteInventoryOperationsAsync(affectedOperationIds, revisionIds, ct);
        await DeleteFinanceAsync(payableIds, ct);

        await ExecuteAsync("DELETE FROM outsourced.outsourced_supply_details WHERE outsourced_supply_batch_id=@batch_id;", ct, ("batch_id", batchId));
        await ExecuteAsync("DELETE FROM inventory.inventory_positions WHERE outsourced_supply_batch_id=@batch_id;", ct, ("batch_id", batchId));

        await using var delete = Sql("DELETE FROM outsourced.outsourced_supply_batches WHERE id=@id AND row_version=@version;");
        delete.Parameters.AddWithValue("id", batchId);
        delete.Parameters.AddWithValue("version", expectedRowVersion);
        return await delete.ExecuteNonQueryAsync(ct) == 1;
    }

    private async ValueTask ReverseDetailInventoryAsync(Guid detailId, CancellationToken ct)
    {
        await using var command = Sql("""
            WITH target_delta AS
            (
                SELECT movement.origin, movement.procurement_batch_id, movement.outsourced_supply_batch_id,
                       movement.inventory_object_kind, movement.procurement_product_id, movement.process_material_id,
                       movement.sales_product_id, movement.storage_location_id, movement.raw_source_kind,
                       movement.supplier_id, SUM(movement.quantity_delta) AS quantity_delta
                FROM inventory.inventory_operations operation
                JOIN inventory.inventory_movements movement ON movement.inventory_operation_id = operation.id
                WHERE operation.outsourced_supply_detail_id=@detail_id
                GROUP BY movement.origin, movement.procurement_batch_id, movement.outsourced_supply_batch_id,
                         movement.inventory_object_kind, movement.procurement_product_id, movement.process_material_id,
                         movement.sales_product_id, movement.storage_location_id, movement.raw_source_kind, movement.supplier_id
            )
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
              AND position.supplier_id IS NOT DISTINCT FROM target_delta.supplier_id;
            """);
        command.Parameters.AddWithValue("detail_id", detailId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask DeleteDetailInventoryRowsAsync(Guid detailId, CancellationToken ct)
    {
        await ExecuteAsync("""
            DELETE FROM inventory.inventory_movements movement
            USING inventory.inventory_operations operation
            WHERE movement.inventory_operation_id = operation.id
              AND operation.outsourced_supply_detail_id=@detail_id;
            """, ct, ("detail_id", detailId));
        await ExecuteAsync("DELETE FROM inventory.inventory_operations WHERE outsourced_supply_detail_id=@detail_id;", ct, ("detail_id", detailId));
    }

    private async ValueTask DeleteBatchInventoryMovementsAsync(Guid batchId, Guid[] directOperationIds, CancellationToken ct)
    {
        if (directOperationIds.Length > 0)
            await ExecuteAsync("DELETE FROM inventory.inventory_movements WHERE inventory_operation_id = ANY(@ids);", ct, ("ids", directOperationIds));
        await ExecuteAsync("DELETE FROM inventory.inventory_movements WHERE outsourced_supply_batch_id=@batch_id;", ct, ("batch_id", batchId));
    }

    private async ValueTask DeleteSalesSourceReferencesAsync(Guid batchId, Guid[] revisionIds, CancellationToken ct)
    {
        await ExecuteAsync("""
            DELETE FROM sales.sales_allocations allocation
            USING sales.sales_allocation_revision_items item
            WHERE allocation.sales_allocation_revision_item_id = item.id
              AND item.outsourced_supply_batch_id=@batch_id;
            """, ct, ("batch_id", batchId));
        await ExecuteAsync("DELETE FROM sales.sales_allocation_revision_items WHERE outsourced_supply_batch_id=@batch_id;", ct, ("batch_id", batchId));

        if (revisionIds.Length == 0) return;
        await ExecuteAsync("""
            DELETE FROM inventory.inventory_operations operation
            WHERE operation.sales_allocation_revision_id = ANY(@ids)
              AND NOT EXISTS (SELECT 1 FROM inventory.inventory_movements movement WHERE movement.inventory_operation_id=operation.id);
            """, ct, ("ids", revisionIds));
        await ExecuteAsync("""
            DELETE FROM sales.sales_allocation_revisions revision
            WHERE revision.id = ANY(@ids)
              AND NOT EXISTS (SELECT 1 FROM sales.sales_allocation_revision_items item WHERE item.sales_allocation_revision_id=revision.id);
            """, ct, ("ids", revisionIds));
    }

    private async ValueTask DeleteInventoryOperationsAsync(Guid[] operationIds, Guid[] revisionIds, CancellationToken ct)
    {
        if (operationIds.Length > 0)
        {
            await ExecuteAsync("""
                DELETE FROM inventory.inventory_operations operation
                WHERE operation.id = ANY(@ids)
                  AND NOT EXISTS (SELECT 1 FROM inventory.inventory_movements movement WHERE movement.inventory_operation_id=operation.id);
                """, ct, ("ids", operationIds));
        }
        if (revisionIds.Length > 0)
        {
            await ExecuteAsync("""
                DELETE FROM inventory.inventory_operations operation
                WHERE operation.sales_allocation_revision_id = ANY(@ids)
                  AND NOT EXISTS (SELECT 1 FROM inventory.inventory_movements movement WHERE movement.inventory_operation_id=operation.id);
                """, ct, ("ids", revisionIds));
        }
    }

    private async ValueTask DeleteFinanceAsync(Guid[] payableIds, CancellationToken ct)
    {
        if (payableIds.Length == 0) return;
        await ExecuteAsync("DELETE FROM finance.payments WHERE payable_id = ANY(@ids);", ct, ("ids", payableIds));
        await ExecuteAsync("DELETE FROM finance.payable_adjustments WHERE payable_id = ANY(@ids);", ct, ("ids", payableIds));
        await ExecuteAsync("DELETE FROM finance.payable_outstanding_positions WHERE payable_id = ANY(@ids);", ct, ("ids", payableIds));
        await ExecuteAsync("DELETE FROM finance.payable_obligation_items WHERE payable_id = ANY(@ids);", ct, ("ids", payableIds));
        await ExecuteAsync("DELETE FROM finance.payables WHERE id = ANY(@ids);", ct, ("ids", payableIds));
    }

    private async ValueTask DeletePendingOutboxAsync(bool includeBatch, Guid batchId, Guid[] detailIds, Guid[] payableIds, CancellationToken ct)
    {
        await using var command = Sql("""
            DELETE FROM system.outbox_messages
            WHERE published_at IS NULL
              AND (
                    (@include_batch AND payload ->> 'outsourcedSupplyBatchId' = @batch_id)
                 OR payload ->> 'outsourcedSupplyDetailId' = ANY(@detail_ids)
                 OR payload ->> 'payableId' = ANY(@payable_ids)
              );
            """);
        command.Parameters.AddWithValue("include_batch", includeBatch);
        command.Parameters.AddWithValue("batch_id", batchId.ToString());
        command.Parameters.AddWithValue("detail_ids", detailIds.Select(id => id.ToString()).ToArray());
        command.Parameters.AddWithValue("payable_ids", payableIds.Select(id => id.ToString()).ToArray());
        await command.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask<Acquisition> AcquireAsync(
        Guid commandId, CommandRequestHash requestHash, Guid actorAccountId, string commandType,
        DateTimeOffset startedAt, CancellationToken ct)
    {
        await using (var insert = Sql("""
            INSERT INTO system.command_executions
                (command_id,command_type,request_hash,status,result_payload,actor_account_id,started_at,executed_at)
            VALUES (@id,@type,@hash,'IN_PROGRESS',NULL,@actor,@started,NULL)
            ON CONFLICT (command_id) DO NOTHING;
            """))
        {
            insert.Parameters.AddWithValue("id", commandId);
            insert.Parameters.AddWithValue("type", commandType);
            insert.Parameters.AddWithValue("hash", requestHash.Bytes.ToArray());
            insert.Parameters.AddWithValue("actor", actorAccountId);
            insert.Parameters.AddWithValue("started", startedAt);
            if (await insert.ExecuteNonQueryAsync(ct) == 1) return Acquisition.Acquired();
        }

        await using var select = Sql("SELECT command_type,request_hash,status,actor_account_id,result_payload::text FROM system.command_executions WHERE command_id=@id;");
        select.Parameters.AddWithValue("id", commandId);
        await using var reader = await select.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Outsourced hard-delete command conflict could not be loaded.");
        if (!string.Equals(reader.GetString(0), commandType, StringComparison.Ordinal)
            || reader.GetGuid(3) != actorAccountId
            || !requestHash.Bytes.Span.SequenceEqual(reader.GetFieldValue<byte[]>(1)))
            return Acquisition.Conflict();
        if (!string.Equals(reader.GetString(2), "SUCCEEDED", StringComparison.Ordinal) || reader.IsDBNull(4))
            throw new InvalidOperationException($"A committed {commandType} command is not replayable.");
        var replay = JsonSerializer.Deserialize<HardDeleteOutsourcedTransactionResult>(reader.GetString(4), StoredJsonOptions)
            ?? throw new InvalidOperationException($"Stored {commandType} result is invalid.");
        return Acquisition.Replay(replay);
    }

    private async ValueTask AppendAuditAsync(
        Guid commandId, Guid actorAccountId, string commandType, Guid id, TargetKind target,
        long beforeRowVersion, DateTimeOffset now, CancellationToken ct)
    {
        var auditId = Guid.CreateVersion7();
        await using (var audit = Sql("INSERT INTO audit.audit_events (id,command_id,command_type,event_kind,actor_account_id,occurred_at,reason_text) VALUES (@id,@command,@type,'HARD_DELETE',@actor,@now,NULL);"))
        {
            audit.Parameters.AddWithValue("id", auditId);
            audit.Parameters.AddWithValue("command", commandId);
            audit.Parameters.AddWithValue("type", commandType);
            audit.Parameters.AddWithValue("actor", actorAccountId);
            audit.Parameters.AddWithValue("now", now);
            await audit.ExecuteNonQueryAsync(ct);
        }
        await using var subject = Sql("INSERT INTO audit.audit_event_subjects (audit_event_id,sequence,subject_kind,subject_key,change_kind,before_row_version,after_row_version,change_summary) VALUES (@audit,1,@kind,CAST(@key AS jsonb),'HARD_DELETE',@before,NULL,NULL);");
        subject.Parameters.AddWithValue("audit", auditId);
        subject.Parameters.AddWithValue("kind", target == TargetKind.Batch ? "outsourced.supply-batch" : "outsourced.supply-detail");
        subject.Parameters.AddWithValue("key", JsonSerializer.Serialize(new { id }, StoredJsonOptions));
        subject.Parameters.AddWithValue("before", beforeRowVersion);
        await subject.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask CompleteAsync(Guid commandId, string resultJson, DateTimeOffset now, CancellationToken ct)
    {
        await ExecuteAsync("UPDATE system.command_executions SET status='SUCCEEDED',result_payload=CAST(@result AS jsonb),executed_at=@now WHERE command_id=@id AND status='IN_PROGRESS';",
            ct, ("result", resultJson), ("now", now), ("id", commandId));
    }

    private async ValueTask<Guid[]> ReadIdsAsync(string sql, string name, object value, CancellationToken ct) =>
        await ReadIdsAsync(sql, new Dictionary<string, object> { [name] = value }, ct);

    private async ValueTask<Guid[]> ReadIdsAsync(string sql, IReadOnlyDictionary<string, object> parameters, CancellationToken ct)
    {
        await using var command = Sql(sql);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) ids.Add(reader.GetGuid(0));
        return ids.ToArray();
    }

    private async ValueTask<int> ExecuteAsync(string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var command = Sql(sql);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        return await command.ExecuteNonQueryAsync(ct);
    }

    private NpgsqlCommand Sql(string text)
    {
        var tx = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Outsourced hard-delete SQL requires an active command transaction.");
        return new NpgsqlCommand(text, (NpgsqlConnection)_dbContext.Database.GetDbConnection(), (NpgsqlTransaction)tx.GetDbTransaction());
    }

    private static CommandTransactionDecision<ApplicationResult<HardDeleteOutsourcedTransactionResult>> Rollback(ApplicationErrorKind kind, string code) =>
        CommandTransactionDecision<ApplicationResult<HardDeleteOutsourcedTransactionResult>>.Rollback(
            ApplicationResult<HardDeleteOutsourcedTransactionResult>.Failure(ApplicationError.Create(kind, code)));

    private sealed record TargetState(Guid BatchId, long RowVersion);
    private enum TargetKind { Batch, Detail }
    private enum AcquireKind { Acquired, Replay, Conflict }
    private sealed record Acquisition(AcquireKind Kind, HardDeleteOutsourcedTransactionResult? Result)
    {
        public static Acquisition Acquired() => new(AcquireKind.Acquired, null);
        public static Acquisition Replay(HardDeleteOutsourcedTransactionResult result) => new(AcquireKind.Replay, result);
        public static Acquisition Conflict() => new(AcquireKind.Conflict, null);
    }
}
