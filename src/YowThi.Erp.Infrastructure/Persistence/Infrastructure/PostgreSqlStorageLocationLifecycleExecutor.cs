using System.Text.Json;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Infrastructure;
using YowThi.Erp.Infrastructure.Persistence.Commands;

namespace YowThi.Erp.Infrastructure.Persistence.Infrastructure;

internal sealed class PostgreSqlStorageLocationLifecycleExecutor : IStorageLocationLifecycleExecutor
{
    private const string SoftDeleteCommandType = "SoftDeleteStorageLocation";
    private const string RestoreCommandType = "RestoreStorageLocation";
    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;
    private readonly MasterCommandSupport _support;

    public PostgreSqlStorageLocationLifecycleExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
        _support = new MasterCommandSupport(dbContext);
    }

    public ValueTask<ApplicationResult<StorageLocationLifecycleResult>> SoftDeleteAsync(
        SoftDeleteStorageLocationExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        var validationError = StorageLocationLifecycleValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ValueTask.FromResult(ApplicationResult<StorageLocationLifecycleResult>.Failure(validationError));
        }

        return ExecuteAsync(
            execution.CommandId,
            execution.RequestHash,
            execution.ActorAccountId,
            execution.Command.StorageLocationId,
            execution.Command.ExpectedRowVersion,
            LifecycleOperation.SoftDelete,
            cancellationToken);
    }

    public ValueTask<ApplicationResult<StorageLocationLifecycleResult>> RestoreAsync(
        RestoreStorageLocationExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        var validationError = StorageLocationLifecycleValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ValueTask.FromResult(ApplicationResult<StorageLocationLifecycleResult>.Failure(validationError));
        }

        return ExecuteAsync(
            execution.CommandId,
            execution.RequestHash,
            execution.ActorAccountId,
            execution.Command.StorageLocationId,
            execution.Command.ExpectedRowVersion,
            LifecycleOperation.Restore,
            cancellationToken);
    }

    private async ValueTask<ApplicationResult<StorageLocationLifecycleResult>> ExecuteAsync(
        CommandId commandId,
        CommandRequestHash requestHash,
        ActorAccountId actorAccountId,
        Guid storageLocationId,
        long expectedRowVersion,
        LifecycleOperation operation,
        CancellationToken cancellationToken)
    {
        var commandType = operation == LifecycleOperation.SoftDelete
            ? SoftDeleteCommandType
            : RestoreCommandType;

        return await _transactionRunner.ExecuteAsync(async ct =>
        {
            var now = _timeProvider.GetUtcNow();
            var acquisition = await _support.AcquireAsync<StorageLocationLifecycleResult>(
                commandId,
                requestHash,
                actorAccountId,
                commandType,
                now,
                ct);

            if (acquisition.Kind == MasterCommandAcquisitionKind.Replay)
            {
                return CommandTransactionDecision<ApplicationResult<StorageLocationLifecycleResult>>.Rollback(
                    ApplicationResult<StorageLocationLifecycleResult>.Success(acquisition.ReplayResult!));
            }

            if (acquisition.Kind == MasterCommandAcquisitionKind.Conflict)
            {
                return Rollback(ApplicationErrorKind.Conflict, StorageLocationLifecycleErrorCodes.IdempotencyKeyReused);
            }

            var state = await LockAsync(storageLocationId, ct);
            if (state is null)
            {
                return Rollback(ApplicationErrorKind.NotFound, StorageLocationLifecycleErrorCodes.NotFound);
            }

            if (state.RowVersion != expectedRowVersion)
            {
                return Rollback(ApplicationErrorKind.Conflict, StorageLocationLifecycleErrorCodes.StaleRowVersion);
            }

            if (operation == LifecycleOperation.SoftDelete && state.DeletedAt is not null)
            {
                return Rollback(ApplicationErrorKind.Conflict, StorageLocationLifecycleErrorCodes.AlreadyDeleted);
            }

            if (operation == LifecycleOperation.Restore && state.DeletedAt is null)
            {
                return Rollback(ApplicationErrorKind.Conflict, StorageLocationLifecycleErrorCodes.NotDeleted);
            }

            var nextRowVersion = await MutateAsync(
                storageLocationId,
                expectedRowVersion,
                actorAccountId.Value,
                now,
                operation,
                ct);
            if (nextRowVersion is null)
            {
                return Rollback(ApplicationErrorKind.Conflict, StorageLocationLifecycleErrorCodes.StaleRowVersion);
            }

            var result = new StorageLocationLifecycleResult(
                storageLocationId,
                nextRowVersion.Value,
                operation == LifecycleOperation.SoftDelete);

            await AppendLifecycleAuditAsync(
                commandId,
                actorAccountId,
                storageLocationId,
                commandType,
                operation,
                state.RowVersion,
                nextRowVersion.Value,
                now,
                ct);
            await _support.MarkSucceededAsync(commandId, result, now, ct);

            return CommandTransactionDecision<ApplicationResult<StorageLocationLifecycleResult>>.Commit(
                ApplicationResult<StorageLocationLifecycleResult>.Success(result));
        }, cancellationToken);
    }

    private async ValueTask<LocationState?> LockAsync(Guid storageLocationId, CancellationToken cancellationToken)
    {
        await using var command = _support.CreateSqlCommand(
            "SELECT row_version, deleted_at FROM infrastructure.storage_locations WHERE id = @id FOR UPDATE;");
        command.Parameters.AddWithValue("id", storageLocationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new LocationState(reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1))
            : null;
    }

    private async ValueTask<long?> MutateAsync(
        Guid storageLocationId,
        long expectedRowVersion,
        Guid actorAccountId,
        DateTimeOffset occurredAt,
        LifecycleOperation operation,
        CancellationToken cancellationToken)
    {
        var sql = operation == LifecycleOperation.SoftDelete
            ? """
              UPDATE infrastructure.storage_locations
              SET deleted_at = @occurred_at,
                  deleted_by_account_id = @actor_account_id,
                  row_version = row_version + 1
              WHERE id = @id
                AND row_version = @expected_row_version
                AND deleted_at IS NULL
              RETURNING row_version;
              """
            : """
              UPDATE infrastructure.storage_locations
              SET deleted_at = NULL,
                  deleted_by_account_id = NULL,
                  row_version = row_version + 1
              WHERE id = @id
                AND row_version = @expected_row_version
                AND deleted_at IS NOT NULL
              RETURNING row_version;
              """;

        await using var command = _support.CreateSqlCommand(sql);
        command.Parameters.AddWithValue("id", storageLocationId);
        command.Parameters.AddWithValue("expected_row_version", expectedRowVersion);
        if (operation == LifecycleOperation.SoftDelete)
        {
            command.Parameters.AddWithValue("occurred_at", occurredAt);
            command.Parameters.AddWithValue("actor_account_id", actorAccountId);
        }

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null ? null : Convert.ToInt64(value);
    }

    private async ValueTask AppendLifecycleAuditAsync(
        CommandId commandId,
        ActorAccountId actorAccountId,
        Guid storageLocationId,
        string commandType,
        LifecycleOperation operation,
        long beforeRowVersion,
        long afterRowVersion,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var auditEventId = Guid.CreateVersion7();
        var changeKind = operation == LifecycleOperation.SoftDelete ? "SOFT_DELETE" : "RESTORE";

        await using (var auditEvent = _support.CreateSqlCommand(
            """
            INSERT INTO audit.audit_events
                (id, command_id, command_type, event_kind, actor_account_id, occurred_at, reason_text)
            VALUES
                (@id, @command_id, @command_type, 'DATA_LIFECYCLE', @actor_account_id, @occurred_at, NULL);
            """))
        {
            auditEvent.Parameters.AddWithValue("id", auditEventId);
            auditEvent.Parameters.AddWithValue("command_id", commandId.Value);
            auditEvent.Parameters.AddWithValue("command_type", commandType);
            auditEvent.Parameters.AddWithValue("actor_account_id", actorAccountId.Value);
            auditEvent.Parameters.AddWithValue("occurred_at", occurredAt);
            await auditEvent.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var subject = _support.CreateSqlCommand(
            """
            INSERT INTO audit.audit_event_subjects
                (audit_event_id, sequence, subject_kind, subject_key, change_kind,
                 before_row_version, after_row_version, change_summary)
            VALUES
                (@audit_event_id, 1, 'infrastructure.storage-location', CAST(@subject_key AS jsonb), @change_kind,
                 @before_row_version, @after_row_version, NULL);
            """);
        subject.Parameters.AddWithValue("audit_event_id", auditEventId);
        subject.Parameters.AddWithValue("subject_key", JsonSerializer.Serialize(new { id = storageLocationId }, StoredJsonOptions));
        subject.Parameters.AddWithValue("change_kind", changeKind);
        subject.Parameters.AddWithValue("before_row_version", beforeRowVersion);
        subject.Parameters.AddWithValue("after_row_version", afterRowVersion);
        await subject.ExecuteNonQueryAsync(cancellationToken);
    }

    private static CommandTransactionDecision<ApplicationResult<StorageLocationLifecycleResult>> Rollback(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<StorageLocationLifecycleResult>>.Rollback(
            ApplicationResult<StorageLocationLifecycleResult>.Failure(ApplicationError.Create(kind, code)));

    private sealed record LocationState(long RowVersion, DateTimeOffset? DeletedAt);

    private enum LifecycleOperation
    {
        SoftDelete,
        Restore,
    }
}
