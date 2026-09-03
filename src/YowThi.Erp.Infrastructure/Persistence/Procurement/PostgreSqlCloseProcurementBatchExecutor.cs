using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Procurement;
using YowThi.Erp.Infrastructure.Persistence.Inventory;

namespace YowThi.Erp.Infrastructure.Persistence.Procurement;

internal sealed class PostgreSqlCloseProcurementBatchExecutor : ICloseProcurementBatchExecutor
{
    private const string CommandType = "CloseProcurementBatch";
    private const string OutboxMessageType = "procurement.batch.closed";
    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlCloseProcurementBatchExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public async ValueTask<ApplicationResult<CloseProcurementBatchResult>> ExecuteAsync(
        CloseProcurementBatchExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);

        var validationError = CloseProcurementBatchValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ApplicationResult<CloseProcurementBatchResult>.Failure(validationError);
        }

        return await _transactionRunner.ExecuteAsync(
            async ct =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await InventoryCommandPersistence.AcquireAsync<CloseProcurementBatchResult>(
                    _dbContext,
                    CommandType,
                    execution.CommandId,
                    execution.RequestHash,
                    execution.ActorAccountId,
                    now,
                    StoredJsonOptions,
                    ct);

                if (acquisition.Kind == InventoryCommandAcquisitionKind.Replay)
                {
                    return CommandTransactionDecision<ApplicationResult<CloseProcurementBatchResult>>.Rollback(
                        ApplicationResult<CloseProcurementBatchResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == InventoryCommandAcquisitionKind.Conflict)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        ProcurementBatchCloseErrorCodes.IdempotencyKeyReused);
                }

                var command = execution.Command;
                var batch = await LockBatchAsync(command.ProcurementBatchId, ct);
                if (batch is null)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.NotFound,
                        ProcurementBatchCloseErrorCodes.BatchNotFound);
                }

                if (batch.Value.Deleted)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        ProcurementBatchCloseErrorCodes.BatchUnavailable);
                }

                if (batch.Value.RowVersion != command.ExpectedRowVersion)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        ProcurementBatchCloseErrorCodes.ConcurrentChange);
                }

                if (!batch.Value.Active)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        ProcurementBatchCloseErrorCodes.AlreadyClosed);
                }

                var remaining = await LockRemainingPositionsAsync(command.ProcurementBatchId, ct);
                if (remaining.Any(x => x.Sellable))
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        ProcurementBatchCloseErrorCodes.SellableInventoryRemaining);
                }

                Guid? inventoryOperationId = null;
                if (remaining.Count > 0)
                {
                    inventoryOperationId = Guid.CreateVersion7();
                    await InsertReconciliationOperationAsync(
                        inventoryOperationId.Value,
                        command.ProcurementBatchId,
                        execution.ActorAccountId.Value,
                        now,
                        ct);

                    var sequence = 1;
                    foreach (var position in remaining)
                    {
                        await ReconcilePositionAsync(
                            inventoryOperationId.Value,
                            sequence++,
                            position,
                            now,
                            ct);
                    }
                }

                if (await HasAnyNonZeroPositionAsync(command.ProcurementBatchId, ct))
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        ProcurementBatchCloseErrorCodes.ConcurrentChange);
                }

                var closedRowVersion = await CloseBatchAsync(
                    command.ProcurementBatchId,
                    command.ExpectedRowVersion,
                    execution.ActorAccountId.Value,
                    now,
                    ct);

                if (closedRowVersion is null)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        ProcurementBatchCloseErrorCodes.ConcurrentChange);
                }

                var result = new CloseProcurementBatchResult(
                    command.ProcurementBatchId,
                    inventoryOperationId,
                    remaining.Count,
                    closedRowVersion.Value);
                var resultJson = JsonSerializer.Serialize(result, StoredJsonOptions);

                await AppendAuditAsync(
                    execution,
                    command.ExpectedRowVersion,
                    closedRowVersion.Value,
                    now,
                    ct);
                await InventoryCommandPersistence.EnqueueOutboxAsync(
                    _dbContext,
                    OutboxMessageType,
                    execution.CommandId,
                    resultJson,
                    now,
                    ct);
                await InventoryCommandPersistence.MarkSucceededAsync(
                    _dbContext,
                    execution.CommandId,
                    resultJson,
                    now,
                    ct);

                return CommandTransactionDecision<ApplicationResult<CloseProcurementBatchResult>>.Commit(
                    ApplicationResult<CloseProcurementBatchResult>.Success(result));
            },
            cancellationToken);
    }

    private async ValueTask<BatchSnapshot?> LockBatchAsync(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            SELECT lifecycle_status, row_version, deleted_at
            FROM procurement.procurement_batches
            WHERE id = @id
            FOR UPDATE;
            """);
        command.Parameters.AddWithValue("id", batchId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new BatchSnapshot(
            Active: string.Equals(reader.GetString(0), "ACTIVE", StringComparison.Ordinal),
            RowVersion: reader.GetInt64(1),
            Deleted: !reader.IsDBNull(2));
    }

    private async ValueTask<List<PositionSnapshot>> LockRemainingPositionsAsync(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            SELECT id, inventory_object_kind, balance_quantity, row_version
            FROM inventory.inventory_positions
            WHERE origin = 'IN_HOUSE'
              AND procurement_batch_id = @batch_id
              AND balance_quantity <> 0
            ORDER BY id
            FOR UPDATE;
            """);
        command.Parameters.AddWithValue("batch_id", batchId);

        var result = new List<PositionSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new PositionSnapshot(
                reader.GetGuid(0),
                string.Equals(reader.GetString(1), "SALES_PRODUCT", StringComparison.Ordinal),
                reader.GetFieldValue<decimal>(2),
                reader.GetInt64(3)));
        }

        return result;
    }

    private async ValueTask InsertReconciliationOperationAsync(
        Guid operationId,
        Guid batchId,
        Guid actorAccountId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            INSERT INTO inventory.inventory_operations
                (id, operation_type, procurement_entry_id, processing_execution_id,
                 outsourced_supply_detail_id, sales_id, sales_allocation_revision_id,
                 procurement_batch_id, recorded_at, recorded_by_account_id)
            VALUES
                (@id, 'BATCH_RECONCILIATION', NULL, NULL,
                 NULL, NULL, NULL,
                 @batch_id, @recorded_at, @recorded_by);
            """);
        command.Parameters.AddWithValue("id", operationId);
        command.Parameters.AddWithValue("batch_id", batchId);
        command.Parameters.AddWithValue("recorded_at", now);
        command.Parameters.AddWithValue("recorded_by", actorAccountId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask ReconcilePositionAsync(
        Guid operationId,
        int sequence,
        PositionSnapshot position,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using (var movement = CreateSqlCommand(
            """
            INSERT INTO inventory.inventory_movements
                (id, inventory_operation_id, sequence, movement_type, origin,
                 procurement_batch_id, outsourced_supply_batch_id, inventory_object_kind,
                 procurement_product_id, process_material_id, sales_product_id,
                 storage_location_id, raw_source_kind, supplier_id, quantity_delta,
                 sales_allocation_revision_item_id, recorded_at)
            SELECT
                @movement_id, @operation_id, @sequence, 'BATCH_RECONCILIATION', origin,
                procurement_batch_id, outsourced_supply_batch_id, inventory_object_kind,
                procurement_product_id, process_material_id, sales_product_id,
                storage_location_id, raw_source_kind, supplier_id, -balance_quantity,
                NULL, @recorded_at
            FROM inventory.inventory_positions
            WHERE id = @position_id
              AND row_version = @expected_row_version
              AND balance_quantity = @expected_balance
              AND balance_quantity <> 0;
            """))
        {
            movement.Parameters.AddWithValue("movement_id", Guid.CreateVersion7());
            movement.Parameters.AddWithValue("operation_id", operationId);
            movement.Parameters.AddWithValue("sequence", sequence);
            movement.Parameters.AddWithValue("recorded_at", now);
            movement.Parameters.AddWithValue("position_id", position.Id);
            movement.Parameters.AddWithValue("expected_row_version", position.RowVersion);
            movement.Parameters.AddWithValue("expected_balance", position.BalanceQuantity);

            if (await movement.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("Procurement Batch reconciliation movement could not be created from the locked Inventory Position.");
            }
        }

        await using var update = CreateSqlCommand(
            """
            UPDATE inventory.inventory_positions
            SET balance_quantity = 0,
                row_version = row_version + 1
            WHERE id = @position_id
              AND row_version = @expected_row_version
              AND balance_quantity = @expected_balance;
            """);
        update.Parameters.AddWithValue("position_id", position.Id);
        update.Parameters.AddWithValue("expected_row_version", position.RowVersion);
        update.Parameters.AddWithValue("expected_balance", position.BalanceQuantity);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Procurement Batch reconciliation could not zero the locked Inventory Position.");
        }
    }

    private async ValueTask<bool> HasAnyNonZeroPositionAsync(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM inventory.inventory_positions
                WHERE origin = 'IN_HOUSE'
                  AND procurement_batch_id = @batch_id
                  AND balance_quantity <> 0);
            """);
        command.Parameters.AddWithValue("batch_id", batchId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Procurement Batch final inventory validation returned no result."));
    }

    private async ValueTask<long?> CloseBatchAsync(
        Guid batchId,
        long expectedRowVersion,
        Guid actorAccountId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            UPDATE procurement.procurement_batches
            SET lifecycle_status = 'CLOSED',
                closed_at = @closed_at,
                closed_by_account_id = @closed_by,
                row_version = row_version + 1
            WHERE id = @id
              AND lifecycle_status = 'ACTIVE'
              AND deleted_at IS NULL
              AND row_version = @expected_row_version
            RETURNING row_version;
            """);
        command.Parameters.AddWithValue("closed_at", now);
        command.Parameters.AddWithValue("closed_by", actorAccountId);
        command.Parameters.AddWithValue("id", batchId);
        command.Parameters.AddWithValue("expected_row_version", expectedRowVersion);
        return (long?)(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async ValueTask AppendAuditAsync(
        CloseProcurementBatchExecution execution,
        long beforeRowVersion,
        long afterRowVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var auditEventId = Guid.CreateVersion7();
        var subjectKeyJson = JsonSerializer.Serialize(
            new { id = execution.Command.ProcurementBatchId },
            StoredJsonOptions);

        await using (var auditEvent = CreateSqlCommand(
            """
            INSERT INTO audit.audit_events
                (id, command_id, command_type, event_kind, actor_account_id, occurred_at, reason_text)
            VALUES
                (@id, @command_id, @command_type, 'BUSINESS_COMMAND', @actor_account_id, @occurred_at, NULL);
            """))
        {
            auditEvent.Parameters.AddWithValue("id", auditEventId);
            auditEvent.Parameters.AddWithValue("command_id", execution.CommandId.Value);
            auditEvent.Parameters.AddWithValue("command_type", CommandType);
            auditEvent.Parameters.AddWithValue("actor_account_id", execution.ActorAccountId.Value);
            auditEvent.Parameters.AddWithValue("occurred_at", now);
            await auditEvent.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var subject = CreateSqlCommand(
            """
            INSERT INTO audit.audit_event_subjects
                (audit_event_id, sequence, subject_kind, subject_key, change_kind,
                 before_row_version, after_row_version, change_summary)
            VALUES
                (@audit_event_id, 1, 'procurement.batch', CAST(@subject_key AS jsonb), 'UPDATE',
                 @before_row_version, @after_row_version, NULL);
            """);
        subject.Parameters.AddWithValue("audit_event_id", auditEventId);
        subject.Parameters.AddWithValue("subject_key", subjectKeyJson);
        subject.Parameters.AddWithValue("before_row_version", beforeRowVersion);
        subject.Parameters.AddWithValue("after_row_version", afterRowVersion);
        await subject.ExecuteNonQueryAsync(cancellationToken);
    }

    private NpgsqlCommand CreateSqlCommand(string sql)
    {
        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("CloseProcurementBatch SQL requires an active command transaction.");

        return new NpgsqlCommand(
            sql,
            (NpgsqlConnection)_dbContext.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction());
    }

    private static CommandTransactionDecision<ApplicationResult<CloseProcurementBatchResult>> RollbackFailure(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<CloseProcurementBatchResult>>.Rollback(
            ApplicationResult<CloseProcurementBatchResult>.Failure(
                ApplicationError.Create(kind, code)));

    private readonly record struct BatchSnapshot(
        bool Active,
        long RowVersion,
        bool Deleted);

    private readonly record struct PositionSnapshot(
        Guid Id,
        bool Sellable,
        decimal BalanceQuantity,
        long RowVersion);
}
