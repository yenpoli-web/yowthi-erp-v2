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

internal sealed class PostgreSqlOutsourcedTransactionLifecycleExecutor : IOutsourcedTransactionLifecycleExecutor
{
    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlOutsourcedTransactionLifecycleExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public ValueTask<ApplicationResult<OutsourcedTransactionLifecycleResult>> SoftDeleteBatchAsync(
        SoftDeleteOutsourcedSupplyBatchExecution execution,
        CancellationToken cancellationToken) =>
        ExecuteAsync(execution.CommandId, execution.RequestHash, execution.ActorAccountId,
            execution.Command.OutsourcedSupplyBatchId, execution.Command.ExpectedRowVersion,
            TargetKind.Batch, LifecycleOperation.SoftDelete,
            OutsourcedTransactionLifecycleValidation.Validate(execution.Command), cancellationToken);

    public ValueTask<ApplicationResult<OutsourcedTransactionLifecycleResult>> RestoreBatchAsync(
        RestoreOutsourcedSupplyBatchExecution execution,
        CancellationToken cancellationToken) =>
        ExecuteAsync(execution.CommandId, execution.RequestHash, execution.ActorAccountId,
            execution.Command.OutsourcedSupplyBatchId, execution.Command.ExpectedRowVersion,
            TargetKind.Batch, LifecycleOperation.Restore,
            OutsourcedTransactionLifecycleValidation.Validate(execution.Command), cancellationToken);

    public ValueTask<ApplicationResult<OutsourcedTransactionLifecycleResult>> SoftDeleteDetailAsync(
        SoftDeleteOutsourcedSupplyDetailExecution execution,
        CancellationToken cancellationToken) =>
        ExecuteAsync(execution.CommandId, execution.RequestHash, execution.ActorAccountId,
            execution.Command.OutsourcedSupplyDetailId, execution.Command.ExpectedRowVersion,
            TargetKind.Detail, LifecycleOperation.SoftDelete,
            OutsourcedTransactionLifecycleValidation.Validate(execution.Command), cancellationToken);

    public ValueTask<ApplicationResult<OutsourcedTransactionLifecycleResult>> RestoreDetailAsync(
        RestoreOutsourcedSupplyDetailExecution execution,
        CancellationToken cancellationToken) =>
        ExecuteAsync(execution.CommandId, execution.RequestHash, execution.ActorAccountId,
            execution.Command.OutsourcedSupplyDetailId, execution.Command.ExpectedRowVersion,
            TargetKind.Detail, LifecycleOperation.Restore,
            OutsourcedTransactionLifecycleValidation.Validate(execution.Command), cancellationToken);

    private async ValueTask<ApplicationResult<OutsourcedTransactionLifecycleResult>> ExecuteAsync(
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
            return ApplicationResult<OutsourcedTransactionLifecycleResult>.Failure(validationError);
        }

        var commandType = (target, operation) switch
        {
            (TargetKind.Batch, LifecycleOperation.SoftDelete) => "SoftDeleteOutsourcedSupplyBatch",
            (TargetKind.Batch, LifecycleOperation.Restore) => "RestoreOutsourcedSupplyBatch",
            (TargetKind.Detail, LifecycleOperation.SoftDelete) => "SoftDeleteOutsourcedSupplyDetail",
            (TargetKind.Detail, LifecycleOperation.Restore) => "RestoreOutsourcedSupplyDetail",
            _ => throw new ArgumentOutOfRangeException(),
        };

        return await _transactionRunner.ExecuteAsync(async ct =>
        {
            var now = _timeProvider.GetUtcNow();
            var acquisition = await AcquireAsync(commandId.Value, requestHash, actorAccountId.Value, commandType, now, ct);
            if (acquisition.Kind == AcquireKind.Replay)
            {
                return CommandTransactionDecision<ApplicationResult<OutsourcedTransactionLifecycleResult>>.Rollback(
                    ApplicationResult<OutsourcedTransactionLifecycleResult>.Success(acquisition.Result!));
            }

            if (acquisition.Kind == AcquireKind.Conflict)
            {
                return Rollback(ApplicationErrorKind.Conflict, OutsourcedTransactionLifecycleErrorCodes.IdempotencyKeyReused);
            }

            var state = await LockAsync(id, target, ct);
            if (state is null)
            {
                return Rollback(ApplicationErrorKind.NotFound,
                    target == TargetKind.Batch
                        ? OutsourcedTransactionLifecycleErrorCodes.BatchNotFound
                        : OutsourcedTransactionLifecycleErrorCodes.DetailNotFound);
            }

            if (state.RowVersion != expectedRowVersion)
            {
                return Rollback(ApplicationErrorKind.Conflict, OutsourcedTransactionLifecycleErrorCodes.StaleRowVersion);
            }

            if (operation == LifecycleOperation.SoftDelete && state.DeletedAt is not null)
            {
                return Rollback(ApplicationErrorKind.Conflict, OutsourcedTransactionLifecycleErrorCodes.AlreadyDeleted);
            }

            if (operation == LifecycleOperation.Restore && state.DeletedAt is null)
            {
                return Rollback(ApplicationErrorKind.Conflict, OutsourcedTransactionLifecycleErrorCodes.NotDeleted);
            }

            var nextVersion = await MutateAsync(id, expectedRowVersion, actorAccountId.Value, now, target, operation, ct);
            if (nextVersion is null)
            {
                return Rollback(ApplicationErrorKind.Conflict, OutsourcedTransactionLifecycleErrorCodes.StaleRowVersion);
            }

            var result = new OutsourcedTransactionLifecycleResult(id, nextVersion.Value, operation == LifecycleOperation.SoftDelete);
            var resultJson = JsonSerializer.Serialize(result, StoredJsonOptions);
            await AppendAuditAsync(commandId.Value, actorAccountId.Value, commandType, id, target, operation,
                state.RowVersion, nextVersion.Value, now, ct);
            await CompleteAsync(commandId.Value, resultJson, now, ct);

            return CommandTransactionDecision<ApplicationResult<OutsourcedTransactionLifecycleResult>>.Commit(
                ApplicationResult<OutsourcedTransactionLifecycleResult>.Success(result));
        }, cancellationToken);
    }

    private async ValueTask<TargetState?> LockAsync(Guid id, TargetKind target, CancellationToken ct)
    {
        var table = target == TargetKind.Batch
            ? "outsourced.outsourced_supply_batches"
            : "outsourced.outsourced_supply_details";
        await using var command = Sql($"SELECT row_version, deleted_at FROM {table} WHERE id = @id FOR UPDATE;");
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new TargetState(reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1));
    }

    private async ValueTask<long?> MutateAsync(
        Guid id,
        long expectedRowVersion,
        Guid actorAccountId,
        DateTimeOffset now,
        TargetKind target,
        LifecycleOperation operation,
        CancellationToken ct)
    {
        var table = target == TargetKind.Batch
            ? "outsourced.outsourced_supply_batches"
            : "outsourced.outsourced_supply_details";
        var sql = operation == LifecycleOperation.SoftDelete
            ? $"UPDATE {table} SET deleted_at=@now, deleted_by_account_id=@actor, row_version=row_version+1 WHERE id=@id AND row_version=@version AND deleted_at IS NULL RETURNING row_version;"
            : $"UPDATE {table} SET deleted_at=NULL, deleted_by_account_id=NULL, row_version=row_version+1 WHERE id=@id AND row_version=@version AND deleted_at IS NOT NULL RETURNING row_version;";
        await using var command = Sql(sql);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("version", expectedRowVersion);
        if (operation == LifecycleOperation.SoftDelete)
        {
            command.Parameters.AddWithValue("now", now);
            command.Parameters.AddWithValue("actor", actorAccountId);
        }
        var value = await command.ExecuteScalarAsync(ct);
        return value is null ? null : Convert.ToInt64(value);
    }

    private async ValueTask<Acquisition> AcquireAsync(
        Guid commandId,
        CommandRequestHash requestHash,
        Guid actorAccountId,
        string commandType,
        DateTimeOffset startedAt,
        CancellationToken ct)
    {
        await using (var insert = Sql("""
            INSERT INTO system.command_executions
                (command_id, command_type, request_hash, status, result_payload, actor_account_id, started_at, executed_at)
            VALUES (@id, @type, @hash, 'IN_PROGRESS', NULL, @actor, @started, NULL)
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

        await using var select = Sql("SELECT command_type, request_hash, status, actor_account_id, result_payload::text FROM system.command_executions WHERE command_id=@id;");
        select.Parameters.AddWithValue("id", commandId);
        await using var reader = await select.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Outsourced lifecycle command conflict could not be loaded.");
        if (!string.Equals(reader.GetString(0), commandType, StringComparison.Ordinal)
            || reader.GetGuid(3) != actorAccountId
            || !requestHash.Bytes.Span.SequenceEqual(reader.GetFieldValue<byte[]>(1)))
        {
            return Acquisition.Conflict();
        }
        if (!string.Equals(reader.GetString(2), "SUCCEEDED", StringComparison.Ordinal) || reader.IsDBNull(4))
            throw new InvalidOperationException($"A committed {commandType} command is not replayable.");
        var replay = JsonSerializer.Deserialize<OutsourcedTransactionLifecycleResult>(reader.GetString(4), StoredJsonOptions)
            ?? throw new InvalidOperationException($"Stored {commandType} result is invalid.");
        return Acquisition.Replay(replay);
    }

    private async ValueTask AppendAuditAsync(
        Guid commandId,
        Guid actorAccountId,
        string commandType,
        Guid id,
        TargetKind target,
        LifecycleOperation operation,
        long before,
        long after,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var auditId = Guid.CreateVersion7();
        await using (var audit = Sql("INSERT INTO audit.audit_events (id,command_id,command_type,event_kind,actor_account_id,occurred_at,reason_text) VALUES (@id,@command,@type,'DATA_LIFECYCLE',@actor,@now,NULL);"))
        {
            audit.Parameters.AddWithValue("id", auditId);
            audit.Parameters.AddWithValue("command", commandId);
            audit.Parameters.AddWithValue("type", commandType);
            audit.Parameters.AddWithValue("actor", actorAccountId);
            audit.Parameters.AddWithValue("now", now);
            await audit.ExecuteNonQueryAsync(ct);
        }

        await using var subject = Sql("INSERT INTO audit.audit_event_subjects (audit_event_id,sequence,subject_kind,subject_key,change_kind,before_row_version,after_row_version,change_summary) VALUES (@audit,1,@kind,CAST(@key AS jsonb),@change,@before,@after,NULL);");
        subject.Parameters.AddWithValue("audit", auditId);
        subject.Parameters.AddWithValue("kind", target == TargetKind.Batch ? "outsourced.supply-batch" : "outsourced.supply-detail");
        subject.Parameters.AddWithValue("key", JsonSerializer.Serialize(new { id }, StoredJsonOptions));
        subject.Parameters.AddWithValue("change", operation == LifecycleOperation.SoftDelete ? "SOFT_DELETE" : "RESTORE");
        subject.Parameters.AddWithValue("before", before);
        subject.Parameters.AddWithValue("after", after);
        await subject.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask CompleteAsync(Guid commandId, string resultJson, DateTimeOffset now, CancellationToken ct)
    {
        await using var command = Sql("UPDATE system.command_executions SET status='SUCCEEDED', result_payload=CAST(@result AS jsonb), executed_at=@now WHERE command_id=@id AND status='IN_PROGRESS';");
        command.Parameters.AddWithValue("result", resultJson);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("id", commandId);
        if (await command.ExecuteNonQueryAsync(ct) != 1)
            throw new InvalidOperationException("Outsourced lifecycle command could not transition to SUCCEEDED.");
    }

    private NpgsqlCommand Sql(string text)
    {
        var tx = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Outsourced lifecycle SQL requires an active command transaction.");
        return new NpgsqlCommand(text, (NpgsqlConnection)_dbContext.Database.GetDbConnection(), (NpgsqlTransaction)tx.GetDbTransaction());
    }

    private static CommandTransactionDecision<ApplicationResult<OutsourcedTransactionLifecycleResult>> Rollback(ApplicationErrorKind kind, string code) =>
        CommandTransactionDecision<ApplicationResult<OutsourcedTransactionLifecycleResult>>.Rollback(
            ApplicationResult<OutsourcedTransactionLifecycleResult>.Failure(ApplicationError.Create(kind, code)));

    private sealed record TargetState(long RowVersion, DateTimeOffset? DeletedAt);
    private enum TargetKind { Batch, Detail }
    private enum LifecycleOperation { SoftDelete, Restore }
    private enum AcquireKind { Acquired, Replay, Conflict }
    private sealed record Acquisition(AcquireKind Kind, OutsourcedTransactionLifecycleResult? Result)
    {
        public static Acquisition Acquired() => new(AcquireKind.Acquired, null);
        public static Acquisition Replay(OutsourcedTransactionLifecycleResult result) => new(AcquireKind.Replay, result);
        public static Acquisition Conflict() => new(AcquireKind.Conflict, null);
    }
}
