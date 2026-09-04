using System.Text.Json;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Procurement;
using YowThi.Erp.Infrastructure.Persistence.Inventory;

namespace YowThi.Erp.Infrastructure.Persistence.Procurement;

internal sealed class PostgreSqlReopenProcurementBatchExecutor : IReopenProcurementBatchExecutor
{
    private const string CommandType = "ReopenProcurementBatch";
    private const string OutboxMessageType = "procurement.batch.reopened";
    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlReopenProcurementBatchExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public async ValueTask<ApplicationResult<ReopenProcurementBatchResult>> ExecuteAsync(
        ReopenProcurementBatchExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);

        var validationError = ReopenProcurementBatchValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ApplicationResult<ReopenProcurementBatchResult>.Failure(validationError);
        }

        return await _transactionRunner.ExecuteAsync(
            async ct =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await InventoryCommandPersistence.AcquireAsync<ReopenProcurementBatchResult>(
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
                    return CommandTransactionDecision<ApplicationResult<ReopenProcurementBatchResult>>.Rollback(
                        ApplicationResult<ReopenProcurementBatchResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == InventoryCommandAcquisitionKind.Conflict)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        ProcurementBatchReopenErrorCodes.IdempotencyKeyReused);
                }

                var command = execution.Command;
                var batch = await LockBatchAsync(command.ProcurementBatchId, ct);
                if (batch is null)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.NotFound,
                        ProcurementBatchReopenErrorCodes.BatchNotFound);
                }

                if (batch.Deleted)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        ProcurementBatchReopenErrorCodes.BatchUnavailable);
                }

                if (batch.RowVersion != command.ExpectedRowVersion)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        ProcurementBatchReopenErrorCodes.ConcurrentChange);
                }

                if (!batch.Closed)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        ProcurementBatchReopenErrorCodes.BatchNotClosed);
                }

                var reopenedRowVersion = await ReopenBatchAsync(
                    command.ProcurementBatchId,
                    command.ExpectedRowVersion,
                    ct);
                if (reopenedRowVersion is null)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        ProcurementBatchReopenErrorCodes.ConcurrentChange);
                }

                var result = new ReopenProcurementBatchResult(
                    command.ProcurementBatchId,
                    reopenedRowVersion.Value,
                    now);
                var resultJson = JsonSerializer.Serialize(result, StoredJsonOptions);

                await AppendLifecycleAuditAsync(
                    execution,
                    batch.RowVersion,
                    reopenedRowVersion.Value,
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

                return CommandTransactionDecision<ApplicationResult<ReopenProcurementBatchResult>>.Commit(
                    ApplicationResult<ReopenProcurementBatchResult>.Success(result));
            },
            cancellationToken);
    }

    private async ValueTask<BatchSnapshot?> LockBatchAsync(
        Guid batchId,
        CancellationToken cancellationToken)
    {
        await using var command = InventoryCommandPersistence.CreateSqlCommand(
            _dbContext,
            """
            SELECT lifecycle_status, row_version, deleted_at, closed_at, closed_by_account_id
            FROM procurement.procurement_batches
            WHERE id = @batch_id
            FOR UPDATE;
            """);
        command.Parameters.AddWithValue("batch_id", batchId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new BatchSnapshot(
            Closed: string.Equals(reader.GetString(0), "CLOSED", StringComparison.Ordinal),
            RowVersion: reader.GetInt64(1),
            Deleted: !reader.IsDBNull(2),
            ClosedAt: reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
            ClosedByAccountId: reader.IsDBNull(4) ? null : reader.GetGuid(4));
    }

    private async ValueTask<long?> ReopenBatchAsync(
        Guid batchId,
        long expectedRowVersion,
        CancellationToken cancellationToken)
    {
        await using var command = InventoryCommandPersistence.CreateSqlCommand(
            _dbContext,
            """
            UPDATE procurement.procurement_batches
            SET lifecycle_status = 'ACTIVE',
                closed_at = NULL,
                closed_by_account_id = NULL,
                row_version = row_version + 1
            WHERE id = @batch_id
              AND lifecycle_status = 'CLOSED'
              AND deleted_at IS NULL
              AND row_version = @expected_row_version
            RETURNING row_version;
            """);
        command.Parameters.AddWithValue("batch_id", batchId);
        command.Parameters.AddWithValue("expected_row_version", expectedRowVersion);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null ? null : Convert.ToInt64(value);
    }

    private async ValueTask AppendLifecycleAuditAsync(
        ReopenProcurementBatchExecution execution,
        long beforeRowVersion,
        long afterRowVersion,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var auditEventId = Guid.CreateVersion7();
        var subjectKeyJson = JsonSerializer.Serialize(
            new { id = execution.Command.ProcurementBatchId },
            StoredJsonOptions);
        var changeSummaryJson = JsonSerializer.Serialize(
            new
            {
                lifecycleStatus = new { before = "CLOSED", after = "ACTIVE" },
                closeMarkers = new { cleared = true },
            },
            StoredJsonOptions);

        await using (var auditEvent = InventoryCommandPersistence.CreateSqlCommand(
            _dbContext,
            """
            INSERT INTO audit.audit_events
                (id, command_id, command_type, event_kind, actor_account_id, occurred_at, reason_text)
            VALUES
                (@id, @command_id, @command_type, 'DATA_LIFECYCLE', @actor_account_id, @occurred_at, @reason_text);
            """))
        {
            auditEvent.Parameters.AddWithValue("id", auditEventId);
            auditEvent.Parameters.AddWithValue("command_id", execution.CommandId.Value);
            auditEvent.Parameters.AddWithValue("command_type", CommandType);
            auditEvent.Parameters.AddWithValue("actor_account_id", execution.ActorAccountId.Value);
            auditEvent.Parameters.AddWithValue("occurred_at", occurredAt);
            auditEvent.Parameters.AddWithValue("reason_text", (object?)execution.Command.ReasonText ?? DBNull.Value);
            await auditEvent.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var subject = InventoryCommandPersistence.CreateSqlCommand(
            _dbContext,
            """
            INSERT INTO audit.audit_event_subjects
                (audit_event_id, sequence, subject_kind, subject_key, change_kind,
                 before_row_version, after_row_version, change_summary)
            VALUES
                (@audit_event_id, 1, 'procurement.batch', CAST(@subject_key AS jsonb), 'UPDATE',
                 @before_row_version, @after_row_version, CAST(@change_summary AS jsonb));
            """);
        subject.Parameters.AddWithValue("audit_event_id", auditEventId);
        subject.Parameters.AddWithValue("subject_key", subjectKeyJson);
        subject.Parameters.AddWithValue("before_row_version", beforeRowVersion);
        subject.Parameters.AddWithValue("after_row_version", afterRowVersion);
        subject.Parameters.AddWithValue("change_summary", changeSummaryJson);
        await subject.ExecuteNonQueryAsync(cancellationToken);
    }

    private static CommandTransactionDecision<ApplicationResult<ReopenProcurementBatchResult>> RollbackFailure(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<ReopenProcurementBatchResult>>.Rollback(
            ApplicationResult<ReopenProcurementBatchResult>.Failure(ApplicationError.Create(kind, code)));

    private sealed record BatchSnapshot(
        bool Closed,
        long RowVersion,
        bool Deleted,
        DateTimeOffset? ClosedAt,
        Guid? ClosedByAccountId);
}
