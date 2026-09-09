using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;

namespace YowThi.Erp.Infrastructure.Persistence.Commands;

internal sealed class MasterCommandSupport
{
    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ErpDbContext _dbContext;

    public MasterCommandSupport(ErpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<MasterCommandAcquisition<TResult>> AcquireAsync<TResult>(
        CommandId commandId,
        CommandRequestHash requestHash,
        ActorAccountId actorAccountId,
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
            insert.Parameters.AddWithValue("command_id", commandId.Value);
            insert.Parameters.AddWithValue("command_type", commandType);
            insert.Parameters.AddWithValue("request_hash", requestHash.Bytes.ToArray());
            insert.Parameters.AddWithValue("actor_account_id", actorAccountId.Value);
            insert.Parameters.AddWithValue("started_at", startedAt);
            if (await insert.ExecuteNonQueryAsync(cancellationToken) == 1)
            {
                return MasterCommandAcquisition<TResult>.Acquired();
            }
        }

        await using var select = CreateSqlCommand(
            """
            SELECT command_type, request_hash, status, actor_account_id, result_payload::text
            FROM system.command_executions
            WHERE command_id = @command_id;
            """);
        select.Parameters.AddWithValue("command_id", commandId.Value);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Master-data CommandExecution conflict could not be loaded.");
        }

        if (!string.Equals(reader.GetString(0), commandType, StringComparison.Ordinal)
            || reader.GetGuid(3) != actorAccountId.Value
            || !requestHash.Bytes.Span.SequenceEqual(reader.GetFieldValue<byte[]>(1)))
        {
            return MasterCommandAcquisition<TResult>.Conflict();
        }

        if (!string.Equals(reader.GetString(2), "SUCCEEDED", StringComparison.Ordinal) || reader.IsDBNull(4))
        {
            throw new InvalidOperationException($"A committed {commandType} CommandExecution with the same identity is not replayable.");
        }

        var replay = JsonSerializer.Deserialize<TResult>(reader.GetString(4), StoredJsonOptions)
            ?? throw new InvalidOperationException($"Stored {commandType} result could not be deserialized.");
        return MasterCommandAcquisition<TResult>.Replay(replay);
    }

    public async ValueTask MarkSucceededAsync(CommandId commandId, object result, DateTimeOffset executedAt, CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            UPDATE system.command_executions
            SET status = 'SUCCEEDED', result_payload = CAST(@result_payload AS jsonb), executed_at = @executed_at
            WHERE command_id = @command_id AND status = 'IN_PROGRESS';
            """);
        command.Parameters.AddWithValue("result_payload", JsonSerializer.Serialize(result, StoredJsonOptions));
        command.Parameters.AddWithValue("executed_at", executedAt);
        command.Parameters.AddWithValue("command_id", commandId.Value);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Master-data CommandExecution could not transition to SUCCEEDED.");
        }
    }

    public async ValueTask AppendAuditAsync(
        CommandId commandId,
        ActorAccountId actorAccountId,
        string commandType,
        string subjectKind,
        Guid subjectId,
        string changeKind,
        long? beforeRowVersion,
        long afterRowVersion,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var auditEventId = Guid.CreateVersion7();
        await using (var audit = CreateSqlCommand(
            """
            INSERT INTO audit.audit_events
                (id, command_id, command_type, event_kind, actor_account_id, occurred_at, reason_text)
            VALUES
                (@id, @command_id, @command_type, 'BUSINESS_COMMAND', @actor_account_id, @occurred_at, NULL);
            """))
        {
            audit.Parameters.AddWithValue("id", auditEventId);
            audit.Parameters.AddWithValue("command_id", commandId.Value);
            audit.Parameters.AddWithValue("command_type", commandType);
            audit.Parameters.AddWithValue("actor_account_id", actorAccountId.Value);
            audit.Parameters.AddWithValue("occurred_at", occurredAt);
            await audit.ExecuteNonQueryAsync(cancellationToken);
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
        subject.Parameters.AddWithValue("subject_key", JsonSerializer.Serialize(new { id = subjectId }, StoredJsonOptions));
        subject.Parameters.AddWithValue("change_kind", changeKind);
        var before = subject.Parameters.Add("before_row_version", NpgsqlDbType.Bigint);
        before.Value = beforeRowVersion is null ? DBNull.Value : beforeRowVersion.Value;
        subject.Parameters.AddWithValue("after_row_version", afterRowVersion);
        await subject.ExecuteNonQueryAsync(cancellationToken);
    }

    public NpgsqlCommand CreateSqlCommand(string sql)
    {
        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Master-data SQL requires an active command transaction.");
        return new NpgsqlCommand(sql, (NpgsqlConnection)_dbContext.Database.GetDbConnection(), (NpgsqlTransaction)transaction.GetDbTransaction());
    }

    public static void AddNullableText(NpgsqlCommand command, string name, string? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Text);
        parameter.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }

    public static void AddNullableGuid(NpgsqlCommand command, string name, Guid? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Uuid);
        parameter.Value = value is null ? DBNull.Value : value.Value;
    }

    public static void AddNullableNumeric(NpgsqlCommand command, string name, decimal? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Numeric);
        parameter.Value = value is null ? DBNull.Value : value.Value;
    }
}

internal enum MasterCommandAcquisitionKind
{
    Acquired,
    Replay,
    Conflict,
}

internal sealed record MasterCommandAcquisition<TResult>(MasterCommandAcquisitionKind Kind, TResult? ReplayResult)
{
    public static MasterCommandAcquisition<TResult> Acquired() => new(MasterCommandAcquisitionKind.Acquired, default);
    public static MasterCommandAcquisition<TResult> Replay(TResult result) => new(MasterCommandAcquisitionKind.Replay, result);
    public static MasterCommandAcquisition<TResult> Conflict() => new(MasterCommandAcquisitionKind.Conflict, default);
}
