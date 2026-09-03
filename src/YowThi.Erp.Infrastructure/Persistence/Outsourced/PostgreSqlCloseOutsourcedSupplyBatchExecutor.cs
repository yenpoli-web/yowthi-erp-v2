using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Outsourced;
using YowThi.Erp.Infrastructure.Persistence.Inventory;

namespace YowThi.Erp.Infrastructure.Persistence.Outsourced;

internal sealed class PostgreSqlCloseOutsourcedSupplyBatchExecutor : ICloseOutsourcedSupplyBatchExecutor
{
    private const string CommandType = "CloseOutsourcedSupplyBatch";
    private const string OutboxMessageType = "outsourced.batch.closed";
    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlCloseOutsourcedSupplyBatchExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public async ValueTask<ApplicationResult<CloseOutsourcedSupplyBatchResult>> ExecuteAsync(
        CloseOutsourcedSupplyBatchExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);

        var validationError = CloseOutsourcedSupplyBatchValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ApplicationResult<CloseOutsourcedSupplyBatchResult>.Failure(validationError);
        }

        return await _transactionRunner.ExecuteAsync(
            async ct =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await InventoryCommandPersistence.AcquireAsync<CloseOutsourcedSupplyBatchResult>(
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
                    return CommandTransactionDecision<ApplicationResult<CloseOutsourcedSupplyBatchResult>>.Rollback(
                        ApplicationResult<CloseOutsourcedSupplyBatchResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == InventoryCommandAcquisitionKind.Conflict)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        OutsourcedBatchCloseErrorCodes.IdempotencyKeyReused);
                }

                var command = execution.Command;
                var batch = await LockBatchAsync(command.OutsourcedSupplyBatchId, ct);
                if (batch is null)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.NotFound,
                        OutsourcedBatchCloseErrorCodes.BatchNotFound);
                }

                if (batch.Value.Deleted)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        OutsourcedBatchCloseErrorCodes.BatchUnavailable);
                }

                if (batch.Value.RowVersion != command.ExpectedRowVersion)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        OutsourcedBatchCloseErrorCodes.ConcurrentChange);
                }

                if (!batch.Value.Active)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        OutsourcedBatchCloseErrorCodes.AlreadyClosed);
                }

                if (await HasRemainingSellableInventoryAsync(command.OutsourcedSupplyBatchId, ct))
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        OutsourcedBatchCloseErrorCodes.SellableInventoryRemaining);
                }

                var closedRowVersion = await CloseBatchAsync(
                    command.OutsourcedSupplyBatchId,
                    command.ExpectedRowVersion,
                    execution.ActorAccountId.Value,
                    now,
                    ct);
                if (closedRowVersion is null)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        OutsourcedBatchCloseErrorCodes.ConcurrentChange);
                }

                var result = new CloseOutsourcedSupplyBatchResult(
                    command.OutsourcedSupplyBatchId,
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

                return CommandTransactionDecision<ApplicationResult<CloseOutsourcedSupplyBatchResult>>.Commit(
                    ApplicationResult<CloseOutsourcedSupplyBatchResult>.Success(result));
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
            FROM outsourced.outsourced_supply_batches
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

    private async ValueTask<bool> HasRemainingSellableInventoryAsync(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            SELECT id
            FROM inventory.inventory_positions
            WHERE origin = 'OUTSOURCED'
              AND outsourced_supply_batch_id = @batch_id
              AND inventory_object_kind = 'SALES_PRODUCT'
              AND balance_quantity <> 0
            ORDER BY id
            FOR UPDATE;
            """);
        command.Parameters.AddWithValue("batch_id", batchId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken);
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
            UPDATE outsourced.outsourced_supply_batches
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
        CloseOutsourcedSupplyBatchExecution execution,
        long beforeRowVersion,
        long afterRowVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var auditEventId = Guid.CreateVersion7();
        var subjectKeyJson = JsonSerializer.Serialize(
            new { id = execution.Command.OutsourcedSupplyBatchId },
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
                (@audit_event_id, 1, 'outsourced.supply-batch', CAST(@subject_key AS jsonb), 'UPDATE',
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
            ?? throw new InvalidOperationException("CloseOutsourcedSupplyBatch SQL requires an active command transaction.");

        return new NpgsqlCommand(
            sql,
            (NpgsqlConnection)_dbContext.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction());
    }

    private static CommandTransactionDecision<ApplicationResult<CloseOutsourcedSupplyBatchResult>> RollbackFailure(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<CloseOutsourcedSupplyBatchResult>>.Rollback(
            ApplicationResult<CloseOutsourcedSupplyBatchResult>.Failure(
                ApplicationError.Create(kind, code)));

    private readonly record struct BatchSnapshot(
        bool Active,
        long RowVersion,
        bool Deleted);
}
