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

internal sealed class PostgreSqlSalesTransactionLifecycleExecutor : ISalesTransactionLifecycleExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlSalesTransactionLifecycleExecutor(ErpDbContext dbContext, ICommandTransactionRunner transactionRunner, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public ValueTask<ApplicationResult<SalesTransactionLifecycleResult>> SoftDeleteSaleAsync(SoftDeleteSaleExecution x, CancellationToken ct) =>
        RunAsync(x.CommandId, x.RequestHash, x.ActorAccountId, x.Command.SalesId, x.Command.ExpectedRowVersion, Target.Sale, Op.SoftDelete, SalesTransactionLifecycleValidation.Validate(x.Command), ct);

    public ValueTask<ApplicationResult<SalesTransactionLifecycleResult>> RestoreSaleAsync(RestoreSaleExecution x, CancellationToken ct) =>
        RunAsync(x.CommandId, x.RequestHash, x.ActorAccountId, x.Command.SalesId, x.Command.ExpectedRowVersion, Target.Sale, Op.Restore, SalesTransactionLifecycleValidation.Validate(x.Command), ct);

    public ValueTask<ApplicationResult<SalesTransactionLifecycleResult>> SoftDeleteDetailAsync(SoftDeleteSalesDetailExecution x, CancellationToken ct) =>
        RunAsync(x.CommandId, x.RequestHash, x.ActorAccountId, x.Command.SalesDetailId, x.Command.ExpectedRowVersion, Target.Detail, Op.SoftDelete, SalesTransactionLifecycleValidation.Validate(x.Command), ct);

    public ValueTask<ApplicationResult<SalesTransactionLifecycleResult>> RestoreDetailAsync(RestoreSalesDetailExecution x, CancellationToken ct) =>
        RunAsync(x.CommandId, x.RequestHash, x.ActorAccountId, x.Command.SalesDetailId, x.Command.ExpectedRowVersion, Target.Detail, Op.Restore, SalesTransactionLifecycleValidation.Validate(x.Command), ct);

    private async ValueTask<ApplicationResult<SalesTransactionLifecycleResult>> RunAsync(
        CommandId commandId, CommandRequestHash requestHash, ActorAccountId actorAccountId,
        Guid id, long expectedRowVersion, Target target, Op op, ApplicationError? validationError, CancellationToken ct)
    {
        if (validationError is not null) return ApplicationResult<SalesTransactionLifecycleResult>.Failure(validationError);
        var commandType = (target, op) switch
        {
            (Target.Sale, Op.SoftDelete) => "SoftDeleteSale",
            (Target.Sale, Op.Restore) => "RestoreSale",
            (Target.Detail, Op.SoftDelete) => "SoftDeleteSalesDetail",
            (Target.Detail, Op.Restore) => "RestoreSalesDetail",
            _ => throw new ArgumentOutOfRangeException(),
        };

        return await _transactionRunner.ExecuteAsync(async token =>
        {
            var now = _timeProvider.GetUtcNow();
            var acquire = await AcquireAsync(commandId.Value, requestHash, actorAccountId.Value, commandType, now, token);
            if (acquire.Kind == AcquireKind.Replay)
                return CommandTransactionDecision<ApplicationResult<SalesTransactionLifecycleResult>>.Rollback(ApplicationResult<SalesTransactionLifecycleResult>.Success(acquire.Result!));
            if (acquire.Kind == AcquireKind.Conflict)
                return Rollback(ApplicationErrorKind.Conflict, SalesTransactionLifecycleErrorCodes.IdempotencyKeyReused);

            var state = await LockAsync(id, target, token);
            if (state is null)
                return Rollback(ApplicationErrorKind.NotFound, target == Target.Sale ? SalesTransactionLifecycleErrorCodes.SaleNotFound : SalesTransactionLifecycleErrorCodes.DetailNotFound);
            if (state.RowVersion != expectedRowVersion)
                return Rollback(ApplicationErrorKind.Conflict, SalesTransactionLifecycleErrorCodes.StaleRowVersion);
            if (op == Op.SoftDelete && state.DeletedAt is not null)
                return Rollback(ApplicationErrorKind.Conflict, SalesTransactionLifecycleErrorCodes.AlreadyDeleted);
            if (op == Op.Restore && state.DeletedAt is null)
                return Rollback(ApplicationErrorKind.Conflict, SalesTransactionLifecycleErrorCodes.NotDeleted);

            var nextVersion = await MutateAsync(id, expectedRowVersion, actorAccountId.Value, now, target, op, token);
            if (nextVersion is null) return Rollback(ApplicationErrorKind.Conflict, SalesTransactionLifecycleErrorCodes.StaleRowVersion);

            var result = new SalesTransactionLifecycleResult(id, nextVersion.Value, op == Op.SoftDelete);
            var resultJson = JsonSerializer.Serialize(result, JsonOptions);
            await AuditAsync(commandId.Value, actorAccountId.Value, commandType, id, target, op, state.RowVersion, nextVersion.Value, now, token);
            await CompleteAsync(commandId.Value, resultJson, now, token);
            return CommandTransactionDecision<ApplicationResult<SalesTransactionLifecycleResult>>.Commit(ApplicationResult<SalesTransactionLifecycleResult>.Success(result));
        }, ct);
    }

    private async ValueTask<State?> LockAsync(Guid id, Target target, CancellationToken ct)
    {
        var table = target == Target.Sale ? "sales.sales" : "sales.sales_details";
        await using var cmd = Sql($"SELECT row_version, deleted_at FROM {table} WHERE id = @id FOR UPDATE;");
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new State(reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1));
    }

    private async ValueTask<long?> MutateAsync(Guid id, long version, Guid actor, DateTimeOffset now, Target target, Op op, CancellationToken ct)
    {
        var table = target == Target.Sale ? "sales.sales" : "sales.sales_details";
        var text = op == Op.SoftDelete
            ? $"UPDATE {table} SET deleted_at=@now, deleted_by_account_id=@actor, row_version=row_version+1 WHERE id=@id AND row_version=@version AND deleted_at IS NULL RETURNING row_version;"
            : $"UPDATE {table} SET deleted_at=NULL, deleted_by_account_id=NULL, row_version=row_version+1 WHERE id=@id AND row_version=@version AND deleted_at IS NOT NULL RETURNING row_version;";
        await using var cmd = Sql(text);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("version", version);
        if (op == Op.SoftDelete)
        {
            cmd.Parameters.AddWithValue("now", now);
            cmd.Parameters.AddWithValue("actor", actor);
        }
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is null ? null : Convert.ToInt64(value);
    }

    private async ValueTask<Acquisition> AcquireAsync(Guid id, CommandRequestHash hash, Guid actor, string type, DateTimeOffset now, CancellationToken ct)
    {
        await using (var insert = Sql("INSERT INTO system.command_executions (command_id,command_type,request_hash,status,result_payload,actor_account_id,started_at,executed_at) VALUES (@id,@type,@hash,'IN_PROGRESS',NULL,@actor,@now,NULL) ON CONFLICT (command_id) DO NOTHING;"))
        {
            insert.Parameters.AddWithValue("id", id); insert.Parameters.AddWithValue("type", type); insert.Parameters.AddWithValue("hash", hash.Bytes.ToArray()); insert.Parameters.AddWithValue("actor", actor); insert.Parameters.AddWithValue("now", now);
            if (await insert.ExecuteNonQueryAsync(ct) == 1) return Acquisition.Acquired();
        }
        await using var select = Sql("SELECT command_type,request_hash,status,actor_account_id,result_payload::text FROM system.command_executions WHERE command_id=@id;");
        select.Parameters.AddWithValue("id", id);
        await using var reader = await select.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Sales lifecycle command conflict could not be loaded.");
        if (!string.Equals(reader.GetString(0), type, StringComparison.Ordinal) || reader.GetGuid(3) != actor || !hash.Bytes.Span.SequenceEqual(reader.GetFieldValue<byte[]>(1))) return Acquisition.Conflict();
        if (!string.Equals(reader.GetString(2), "SUCCEEDED", StringComparison.Ordinal) || reader.IsDBNull(4)) throw new InvalidOperationException($"A committed {type} command is not replayable.");
        return Acquisition.Replay(JsonSerializer.Deserialize<SalesTransactionLifecycleResult>(reader.GetString(4), JsonOptions) ?? throw new InvalidOperationException($"Stored {type} result is invalid."));
    }

    private async ValueTask AuditAsync(Guid commandId, Guid actor, string type, Guid id, Target target, Op op, long before, long after, DateTimeOffset now, CancellationToken ct)
    {
        var auditId = Guid.CreateVersion7();
        await using (var audit = Sql("INSERT INTO audit.audit_events (id,command_id,command_type,event_kind,actor_account_id,occurred_at,reason_text) VALUES (@id,@command,@type,'DATA_LIFECYCLE',@actor,@now,NULL);"))
        {
            audit.Parameters.AddWithValue("id", auditId); audit.Parameters.AddWithValue("command", commandId); audit.Parameters.AddWithValue("type", type); audit.Parameters.AddWithValue("actor", actor); audit.Parameters.AddWithValue("now", now);
            await audit.ExecuteNonQueryAsync(ct);
        }
        await using var subject = Sql("INSERT INTO audit.audit_event_subjects (audit_event_id,sequence,subject_kind,subject_key,change_kind,before_row_version,after_row_version,change_summary) VALUES (@audit,1,@kind,CAST(@key AS jsonb),@change,@before,@after,NULL);");
        subject.Parameters.AddWithValue("audit", auditId); subject.Parameters.AddWithValue("kind", target == Target.Sale ? "sales.sale" : "sales.detail"); subject.Parameters.AddWithValue("key", JsonSerializer.Serialize(new { id }, JsonOptions)); subject.Parameters.AddWithValue("change", op == Op.SoftDelete ? "SOFT_DELETE" : "RESTORE"); subject.Parameters.AddWithValue("before", before); subject.Parameters.AddWithValue("after", after);
        await subject.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask CompleteAsync(Guid id, string result, DateTimeOffset now, CancellationToken ct)
    {
        await using var cmd = Sql("UPDATE system.command_executions SET status='SUCCEEDED', result_payload=CAST(@result AS jsonb), executed_at=@now WHERE command_id=@id AND status='IN_PROGRESS';");
        cmd.Parameters.AddWithValue("result", result); cmd.Parameters.AddWithValue("now", now); cmd.Parameters.AddWithValue("id", id);
        if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Sales lifecycle command could not transition to SUCCEEDED.");
    }

    private NpgsqlCommand Sql(string text)
    {
        var tx = _dbContext.Database.CurrentTransaction ?? throw new InvalidOperationException("Sales lifecycle SQL requires an active command transaction.");
        return new NpgsqlCommand(text, (NpgsqlConnection)_dbContext.Database.GetDbConnection(), (NpgsqlTransaction)tx.GetDbTransaction());
    }

    private static CommandTransactionDecision<ApplicationResult<SalesTransactionLifecycleResult>> Rollback(ApplicationErrorKind kind, string code) => CommandTransactionDecision<ApplicationResult<SalesTransactionLifecycleResult>>.Rollback(ApplicationResult<SalesTransactionLifecycleResult>.Failure(ApplicationError.Create(kind, code)));
    private sealed record State(long RowVersion, DateTimeOffset? DeletedAt);
    private enum Target { Sale, Detail }
    private enum Op { SoftDelete, Restore }
    private enum AcquireKind { Acquired, Replay, Conflict }
    private sealed record Acquisition(AcquireKind Kind, SalesTransactionLifecycleResult? Result)
    {
        public static Acquisition Acquired() => new(AcquireKind.Acquired, null);
        public static Acquisition Replay(SalesTransactionLifecycleResult result) => new(AcquireKind.Replay, result);
        public static Acquisition Conflict() => new(AcquireKind.Conflict, null);
    }
}
