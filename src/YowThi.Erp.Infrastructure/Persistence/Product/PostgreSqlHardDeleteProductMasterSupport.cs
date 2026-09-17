using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.DataProtection;

namespace YowThi.Erp.Infrastructure.Persistence.Product;

internal sealed class PostgreSqlHardDeleteProductMasterSupport(
    ErpDbContext dbContext,
    ICommandTransactionRunner transactionRunner,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ValueTask<ApplicationResult<HardDeleteProductMasterResult>> ExecuteAsync(
        HardDeleteProductMasterExecution execution,
        string commandType,
        string tableName,
        string subjectKind,
        string dependencySql,
        string notFoundCode,
        string dependencyBlockedCode,
        CancellationToken cancellationToken)
    {
        var validation = HardDeleteProductMasterValidation.Validate(execution.Command);
        if (validation is not null)
            return ValueTask.FromResult(ApplicationResult<HardDeleteProductMasterResult>.Failure(validation));

        return transactionRunner.ExecuteAsync(async ct =>
        {
            var now = timeProvider.GetUtcNow();
            var acquisition = await AcquireAsync(execution, commandType, now, ct);
            if (acquisition.Kind == AcquisitionKind.Replay)
                return CommandTransactionDecision<ApplicationResult<HardDeleteProductMasterResult>>.Rollback(ApplicationResult<HardDeleteProductMasterResult>.Success(acquisition.Result!));
            if (acquisition.Kind == AcquisitionKind.Conflict)
                return Rollback(ApplicationErrorKind.Conflict, ProductMasterHardDeleteErrorCodes.IdempotencyKeyReused);

            var rowVersion = await LockAsync(tableName, execution.Command.Id, ct);
            if (rowVersion is null) return Rollback(ApplicationErrorKind.NotFound, notFoundCode);
            if (rowVersion != execution.Command.ExpectedRowVersion) return Rollback(ApplicationErrorKind.Conflict, ProductMasterHardDeleteErrorCodes.StaleRowVersion);
            if (await HasDependenciesAsync(dependencySql, execution.Command.Id, ct)) return Rollback(ApplicationErrorKind.Conflict, dependencyBlockedCode);

            await using (var delete = CreateCommand($"DELETE FROM {tableName} WHERE id = @id AND row_version = @row_version;"))
            {
                delete.Parameters.AddWithValue("id", execution.Command.Id);
                delete.Parameters.AddWithValue("row_version", execution.Command.ExpectedRowVersion);
                if (await delete.ExecuteNonQueryAsync(ct) != 1) return Rollback(ApplicationErrorKind.Conflict, ProductMasterHardDeleteErrorCodes.StaleRowVersion);
            }

            var result = new HardDeleteProductMasterResult(execution.Command.Id);
            await AppendAuditAsync(execution, commandType, subjectKind, rowVersion.Value, now, ct);
            await MarkSucceededAsync(execution.CommandId.Value, result, now, ct);
            return CommandTransactionDecision<ApplicationResult<HardDeleteProductMasterResult>>.Commit(ApplicationResult<HardDeleteProductMasterResult>.Success(result));
        }, cancellationToken);
    }

    private async ValueTask<long?> LockAsync(string tableName, Guid id, CancellationToken ct)
    {
        await using var command = CreateCommand($"SELECT row_version FROM {tableName} WHERE id = @id FOR UPDATE;");
        command.Parameters.AddWithValue("id", id);
        var value = await command.ExecuteScalarAsync(ct);
        return value is null ? null : Convert.ToInt64(value);
    }

    private async ValueTask<bool> HasDependenciesAsync(string sql, Guid id, CancellationToken ct)
    {
        await using var command = CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        return (bool)(await command.ExecuteScalarAsync(ct) ?? false);
    }

    private async ValueTask<Acquisition> AcquireAsync(HardDeleteProductMasterExecution execution, string commandType, DateTimeOffset now, CancellationToken ct)
    {
        await using (var insert = CreateCommand("INSERT INTO system.command_executions (command_id, command_type, request_hash, status, result_payload, actor_account_id, started_at, executed_at) VALUES (@command_id, @command_type, @request_hash, 'IN_PROGRESS', NULL, @actor_account_id, @started_at, NULL) ON CONFLICT (command_id) DO NOTHING;"))
        {
            insert.Parameters.AddWithValue("command_id", execution.CommandId.Value);
            insert.Parameters.AddWithValue("command_type", commandType);
            insert.Parameters.AddWithValue("request_hash", execution.RequestHash.Bytes.ToArray());
            insert.Parameters.AddWithValue("actor_account_id", execution.ActorAccountId.Value);
            insert.Parameters.AddWithValue("started_at", now);
            if (await insert.ExecuteNonQueryAsync(ct) == 1) return Acquisition.Acquired();
        }

        await using var select = CreateCommand("SELECT command_type, request_hash, status, actor_account_id, result_payload::text FROM system.command_executions WHERE command_id = @command_id;");
        select.Parameters.AddWithValue("command_id", execution.CommandId.Value);
        await using var reader = await select.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Product hard-delete command could not be loaded.");
        if (!string.Equals(reader.GetString(0), commandType, StringComparison.Ordinal) || reader.GetGuid(3) != execution.ActorAccountId.Value || !execution.RequestHash.Bytes.Span.SequenceEqual(reader.GetFieldValue<byte[]>(1))) return Acquisition.Conflict();
        if (!string.Equals(reader.GetString(2), "SUCCEEDED", StringComparison.Ordinal) || reader.IsDBNull(4)) throw new InvalidOperationException("Product hard-delete command is not replayable.");
        return Acquisition.Replay(JsonSerializer.Deserialize<HardDeleteProductMasterResult>(reader.GetString(4), JsonOptions) ?? throw new InvalidOperationException("Stored result invalid."));
    }

    private async ValueTask AppendAuditAsync(HardDeleteProductMasterExecution execution, string commandType, string subjectKind, long beforeRowVersion, DateTimeOffset now, CancellationToken ct)
    {
        var auditId = Guid.CreateVersion7();
        await using (var audit = CreateCommand("INSERT INTO audit.audit_events (id, command_id, command_type, event_kind, actor_account_id, occurred_at, reason_text) VALUES (@id, @command_id, @command_type, 'HARD_DELETE', @actor_account_id, @occurred_at, NULL);"))
        {
            audit.Parameters.AddWithValue("id", auditId);
            audit.Parameters.AddWithValue("command_id", execution.CommandId.Value);
            audit.Parameters.AddWithValue("command_type", commandType);
            audit.Parameters.AddWithValue("actor_account_id", execution.ActorAccountId.Value);
            audit.Parameters.AddWithValue("occurred_at", now);
            await audit.ExecuteNonQueryAsync(ct);
        }
        await using var subject = CreateCommand("INSERT INTO audit.audit_event_subjects (audit_event_id, sequence, subject_kind, subject_key, change_kind, before_row_version, after_row_version, change_summary) VALUES (@audit_event_id, 1, @subject_kind, CAST(@subject_key AS jsonb), 'HARD_DELETE', @before_row_version, NULL, NULL);");
        subject.Parameters.AddWithValue("audit_event_id", auditId);
        subject.Parameters.AddWithValue("subject_kind", subjectKind);
        subject.Parameters.AddWithValue("subject_key", JsonSerializer.Serialize(new { id = execution.Command.Id }, JsonOptions));
        subject.Parameters.AddWithValue("before_row_version", beforeRowVersion);
        await subject.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask MarkSucceededAsync(Guid commandId, HardDeleteProductMasterResult result, DateTimeOffset now, CancellationToken ct)
    {
        await using var command = CreateCommand("UPDATE system.command_executions SET status = 'SUCCEEDED', result_payload = CAST(@result AS jsonb), executed_at = @executed_at WHERE command_id = @command_id AND status = 'IN_PROGRESS';");
        command.Parameters.AddWithValue("result", JsonSerializer.Serialize(result, JsonOptions));
        command.Parameters.AddWithValue("executed_at", now);
        command.Parameters.AddWithValue("command_id", commandId);
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Product hard-delete command could not transition to SUCCEEDED.");
    }

    private NpgsqlCommand CreateCommand(string sql)
    {
        var transaction = dbContext.Database.CurrentTransaction ?? throw new InvalidOperationException("Active transaction required.");
        return new NpgsqlCommand(sql, (NpgsqlConnection)dbContext.Database.GetDbConnection(), (NpgsqlTransaction)transaction.GetDbTransaction());
    }

    private static CommandTransactionDecision<ApplicationResult<HardDeleteProductMasterResult>> Rollback(ApplicationErrorKind kind, string code) =>
        CommandTransactionDecision<ApplicationResult<HardDeleteProductMasterResult>>.Rollback(ApplicationResult<HardDeleteProductMasterResult>.Failure(ApplicationError.Create(kind, code)));

    private enum AcquisitionKind { Acquired, Replay, Conflict }
    private sealed record Acquisition(AcquisitionKind Kind, HardDeleteProductMasterResult? Result)
    {
        public static Acquisition Acquired() => new(AcquisitionKind.Acquired, null);
        public static Acquisition Replay(HardDeleteProductMasterResult result) => new(AcquisitionKind.Replay, result);
        public static Acquisition Conflict() => new(AcquisitionKind.Conflict, null);
    }
}