using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Procurement;

namespace YowThi.Erp.Infrastructure.Persistence.Procurement;

internal sealed class PostgreSqlProcurementTransactionLifecycleExecutor : IProcurementTransactionLifecycleExecutor
{
    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlProcurementTransactionLifecycleExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public ValueTask<ApplicationResult<ProcurementTransactionLifecycleResult>> SoftDeleteBatchAsync(
        SoftDeleteProcurementBatchExecution execution,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            execution.CommandId,
            execution.RequestHash,
            execution.ActorAccountId,
            execution.Command.ProcurementBatchId,
            execution.Command.ExpectedRowVersion,
            TargetKind.Batch,
            LifecycleOperation.SoftDelete,
            ProcurementTransactionLifecycleValidation.Validate(execution.Command),
            cancellationToken);

    public ValueTask<ApplicationResult<ProcurementTransactionLifecycleResult>> RestoreBatchAsync(
        RestoreProcurementBatchExecution execution,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            execution.CommandId,
            execution.RequestHash,
            execution.ActorAccountId,
            execution.Command.ProcurementBatchId,
            execution.Command.ExpectedRowVersion,
            TargetKind.Batch,
            LifecycleOperation.Restore,
            ProcurementTransactionLifecycleValidation.Validate(execution.Command),
            cancellationToken);

    public ValueTask<ApplicationResult<ProcurementTransactionLifecycleResult>> SoftDeleteEntryAsync(
        SoftDeleteProcurementEntryExecution execution,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            execution.CommandId,
            execution.RequestHash,
            execution.ActorAccountId,
            execution.Command.ProcurementEntryId,
            execution.Command.ExpectedRowVersion,
            TargetKind.Entry,
            LifecycleOperation.SoftDelete,
            ProcurementTransactionLifecycleValidation.Validate(execution.Command),
            cancellationToken);

    public ValueTask<ApplicationResult<ProcurementTransactionLifecycleResult>> RestoreEntryAsync(
        RestoreProcurementEntryExecution execution,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            execution.CommandId,
            execution.RequestHash,
            execution.ActorAccountId,
            execution.Command.ProcurementEntryId,
            execution.Command.ExpectedRowVersion,
            TargetKind.Entry,
            LifecycleOperation.Restore,
            ProcurementTransactionLifecycleValidation.Validate(execution.Command),
            cancellationToken);

    private async ValueTask<ApplicationResult<ProcurementTransactionLifecycleResult>> ExecuteAsync(
        CommandId commandId,
        CommandRequestHash requestHash,
        ActorAccountId actorAccountId,
        Guid id,
        long expectedRowVersion,
        TargetKind target,
        LifecycleOperation operation,
        ApplicationError? validationError,
        CancellationToken cancellationToken)
    {
        if (validationError is not null)
        {
            return ApplicationResult<ProcurementTransactionLifecycleResult>.Failure(validationError);
        }

        var commandType = (target, operation) switch
        {
            (TargetKind.Batch, LifecycleOperation.SoftDelete) => "SoftDeleteProcurementBatch",
            (TargetKind.Batch, LifecycleOperation.Restore) => "RestoreProcurementBatch",
            (TargetKind.Entry, LifecycleOperation.SoftDelete) => "SoftDeleteProcurementEntry",
            (TargetKind.Entry, LifecycleOperation.Restore) => "RestoreProcurementEntry",
            _ => throw new ArgumentOutOfRangeException(),
        };

        return await _transactionRunner.ExecuteAsync(
            async ct =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await AcquireCommandAsync(
                    commandId.Value,
                    requestHash,
                    actorAccountId.Value,
                    commandType,
                    now,
                    ct);

                if (acquisition.Kind == CommandAcquisitionKind.Replay)
                {
                    return CommandTransactionDecision<ApplicationResult<ProcurementTransactionLifecycleResult>>.Rollback(
                        ApplicationResult<ProcurementTransactionLifecycleResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == CommandAcquisitionKind.Conflict)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, ProcurementTransactionLifecycleErrorCodes.IdempotencyKeyReused);
                }

                var state = await LockTargetAsync(id, target, ct);
                if (state is null)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.NotFound,
                        target == TargetKind.Batch
                            ? ProcurementTransactionLifecycleErrorCodes.BatchNotFound
                            : ProcurementTransactionLifecycleErrorCodes.EntryNotFound);
                }

                if (state.RowVersion != expectedRowVersion)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, ProcurementTransactionLifecycleErrorCodes.StaleRowVersion);
                }

                if (operation == LifecycleOperation.SoftDelete && state.DeletedAt is not null)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, ProcurementTransactionLifecycleErrorCodes.AlreadyDeleted);
                }

                if (operation == LifecycleOperation.Restore && state.DeletedAt is null)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, ProcurementTransactionLifecycleErrorCodes.NotDeleted);
                }

                var nextRowVersion = await MutateAsync(
                    id,
                    expectedRowVersion,
                    actorAccountId.Value,
                    now,
                    target,
                    operation,
                    ct);
                if (nextRowVersion is null)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, ProcurementTransactionLifecycleErrorCodes.StaleRowVersion);
                }

                var result = new ProcurementTransactionLifecycleResult(
                    id,
                    nextRowVersion.Value,
                    operation == LifecycleOperation.SoftDelete);
                var storedResultJson = JsonSerializer.Serialize(result, StoredJsonOptions);

                await AppendAuditAsync(
                    commandId.Value,
                    actorAccountId.Value,
                    id,
                    target,
                    operation,
                    commandType,
                    state.RowVersion,
                    nextRowVersion.Value,
                    now,
                    ct);
                await MarkCommandSucceededAsync(commandId.Value, storedResultJson, now, ct);

                return CommandTransactionDecision<ApplicationResult<ProcurementTransactionLifecycleResult>>.Commit(
                    ApplicationResult<ProcurementTransactionLifecycleResult>.Success(result));
            },
            cancellationToken);
    }

    private async ValueTask<TargetState?> LockTargetAsync(Guid id, TargetKind target, CancellationToken cancellationToken)
    {
        var table = target == TargetKind.Batch
            ? "procurement.procurement_batches"
            : "procurement.procurement_entries";

        await using var command = CreateSqlCommand($"SELECT row_version, deleted_at FROM {table} WHERE id = @id FOR UPDATE;");
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new TargetState(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1));
    }

    private async ValueTask<long?> MutateAsync(
        Guid id,
        long expectedRowVersion,
        Guid actorAccountId,
        DateTimeOffset occurredAt,
        TargetKind target,
        LifecycleOperation operation,
        CancellationToken cancellationToken)
    {
        var table = target == TargetKind.Batch
            ? "procurement.procurement_batches"
            : "procurement.procurement_entries";
        var sql = operation == LifecycleOperation.SoftDelete
            ? $"""
              UPDATE {table}
              SET deleted_at = @occurred_at,
                  deleted_by_account_id = @actor_account_id,
                  row_version = row_version + 1
              WHERE id = @id
                AND row_version = @expected_row_version
                AND deleted_at IS NULL
              RETURNING row_version;
              """
            : $"""
              UPDATE {table}
              SET deleted_at = NULL,
                  deleted_by_account_id = NULL,
                  row_version = row_version + 1
              WHERE id = @id
                AND row_version = @expected_row_version
                AND deleted_at IS NOT NULL
              RETURNING row_version;
              """;

        await using var command = CreateSqlCommand(sql);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("expected_row_version", expectedRowVersion);
        if (operation == LifecycleOperation.SoftDelete)
        {
            command.Parameters.AddWithValue("occurred_at", occurredAt);
            command.Parameters.AddWithValue("actor_account_id", actorAccountId);
        }

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null ? null : Convert.ToInt64(value);
    }

    private async ValueTask<CommandAcquisition> AcquireCommandAsync(
        Guid commandId,
        CommandRequestHash requestHash,
        Guid actorAccountId,
        string commandType,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        await using (var insert = CreateSqlCommand(
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
            if (await insert.ExecuteNonQueryAsync(cancellationToken) == 1)
            {
                return CommandAcquisition.Acquired();
            }
        }

        await using var select = CreateSqlCommand(
            """
            SELECT command_type, request_hash, status, actor_account_id, result_payload::text
            FROM system.command_executions
            WHERE command_id = @command_id;
            """);
        select.Parameters.AddWithValue("command_id", commandId);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Procurement lifecycle command conflict could not be loaded.");
        }

        if (!string.Equals(reader.GetString(0), commandType, StringComparison.Ordinal)
            || reader.GetGuid(3) != actorAccountId
            || !requestHash.Bytes.Span.SequenceEqual(reader.GetFieldValue<byte[]>(1)))
        {
            return CommandAcquisition.Conflict();
        }

        if (!string.Equals(reader.GetString(2), "SUCCEEDED", StringComparison.Ordinal) || reader.IsDBNull(4))
        {
            throw new InvalidOperationException($"A committed {commandType} command is not replayable.");
        }

        var replay = JsonSerializer.Deserialize<ProcurementTransactionLifecycleResult>(reader.GetString(4), StoredJsonOptions)
            ?? throw new InvalidOperationException($"Stored {commandType} result could not be deserialized.");
        return CommandAcquisition.Replay(replay);
    }

    private async ValueTask AppendAuditAsync(
        Guid commandId,
        Guid actorAccountId,
        Guid id,
        TargetKind target,
        LifecycleOperation operation,
        string commandType,
        long beforeRowVersion,
        long afterRowVersion,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var auditEventId = Guid.CreateVersion7();
        var subjectKind = target == TargetKind.Batch ? "procurement.batch" : "procurement.entry";
        var changeKind = operation == LifecycleOperation.SoftDelete ? "SOFT_DELETE" : "RESTORE";
        var subjectKeyJson = JsonSerializer.Serialize(new { id }, StoredJsonOptions);

        await using (var auditEvent = CreateSqlCommand(
            """
            INSERT INTO audit.audit_events
                (id, command_id, command_type, event_kind, actor_account_id, occurred_at, reason_text)
            VALUES
                (@id, @command_id, @command_type, 'DATA_LIFECYCLE', @actor_account_id, @occurred_at, NULL);
            """))
        {
            auditEvent.Parameters.AddWithValue("id", auditEventId);
            auditEvent.Parameters.AddWithValue("command_id", commandId);
            auditEvent.Parameters.AddWithValue("command_type", commandType);
            auditEvent.Parameters.AddWithValue("actor_account_id", actorAccountId);
            auditEvent.Parameters.AddWithValue("occurred_at", occurredAt);
            await auditEvent.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var subject = CreateSqlCommand(
            """
            INSERT INTO audit.audit_event_subjects
                (audit_event_id, sequence, subject_kind, subject_key, change_kind,
                 before_row_version, after_row_version, change_summary)
            VALUES
                (@audit_event_id, 1, @subject_kind, CAST(@subject_key AS jsonb), @change_kind,
                 @before_row_version, @after_row_version, NULL);
            """);
        subject.Parameters.AddWithValue("audit_event_id", auditEventId);
        subject.Parameters.AddWithValue("subject_kind", subjectKind);
        subject.Parameters.AddWithValue("subject_key", subjectKeyJson);
        subject.Parameters.AddWithValue("change_kind", changeKind);
        subject.Parameters.AddWithValue("before_row_version", beforeRowVersion);
        subject.Parameters.AddWithValue("after_row_version", afterRowVersion);
        await subject.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask MarkCommandSucceededAsync(
        Guid commandId,
        string resultJson,
        DateTimeOffset executedAt,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            UPDATE system.command_executions
            SET status = 'SUCCEEDED',
                result_payload = CAST(@result_payload AS jsonb),
                executed_at = @executed_at
            WHERE command_id = @command_id
              AND status = 'IN_PROGRESS';
            """);
        command.Parameters.AddWithValue("result_payload", resultJson);
        command.Parameters.AddWithValue("executed_at", executedAt);
        command.Parameters.AddWithValue("command_id", commandId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Procurement lifecycle command could not transition to SUCCEEDED.");
        }
    }

    private NpgsqlCommand CreateSqlCommand(string sql)
    {
        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Procurement lifecycle SQL requires an active command transaction.");
        return new NpgsqlCommand(
            sql,
            (NpgsqlConnection)_dbContext.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction());
    }

    private static CommandTransactionDecision<ApplicationResult<ProcurementTransactionLifecycleResult>> RollbackFailure(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<ProcurementTransactionLifecycleResult>>.Rollback(
            ApplicationResult<ProcurementTransactionLifecycleResult>.Failure(ApplicationError.Create(kind, code)));

    private sealed record TargetState(long RowVersion, DateTimeOffset? DeletedAt);

    private enum TargetKind { Batch, Entry }
    private enum LifecycleOperation { SoftDelete, Restore }
    private enum CommandAcquisitionKind { Acquired, Replay, Conflict }

    private sealed record CommandAcquisition(
        CommandAcquisitionKind Kind,
        ProcurementTransactionLifecycleResult? ReplayResult)
    {
        public static CommandAcquisition Acquired() => new(CommandAcquisitionKind.Acquired, null);
        public static CommandAcquisition Replay(ProcurementTransactionLifecycleResult result) => new(CommandAcquisitionKind.Replay, result);
        public static CommandAcquisition Conflict() => new(CommandAcquisitionKind.Conflict, null);
    }
}
