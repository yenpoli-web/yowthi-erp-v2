using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;

namespace YowThi.Erp.Infrastructure.Persistence.Inventory;

internal static class InventoryCommandPersistence
{
    public static async ValueTask<InventoryCommandAcquisition<TResult>> AcquireAsync<TResult>(
        ErpDbContext dbContext,
        string commandType,
        CommandId commandId,
        CommandRequestHash requestHash,
        ActorAccountId actorAccountId,
        DateTimeOffset startedAt,
        JsonSerializerOptions jsonOptions,
        CancellationToken cancellationToken)
    {
        await using (var insert = CreateSqlCommand(
            dbContext,
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
                return InventoryCommandAcquisition<TResult>.Acquired();
            }
        }

        await using var select = CreateSqlCommand(
            dbContext,
            """
            SELECT command_type, request_hash, status, actor_account_id, result_payload::text
            FROM system.command_executions
            WHERE command_id = @command_id;
            """);
        select.Parameters.AddWithValue("command_id", commandId.Value);

        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "CommandExecution conflict was observed but the stored command could not be loaded.");
        }

        if (!string.Equals(reader.GetString(0), commandType, StringComparison.Ordinal)
            || reader.GetGuid(3) != actorAccountId.Value
            || !requestHash.Bytes.Span.SequenceEqual(reader.GetFieldValue<byte[]>(1)))
        {
            return InventoryCommandAcquisition<TResult>.Conflict();
        }

        if (!string.Equals(reader.GetString(2), "SUCCEEDED", StringComparison.Ordinal) || reader.IsDBNull(4))
        {
            throw new InvalidOperationException(
                $"A committed {commandType} CommandExecution with the same identity is not replayable.");
        }

        var replay = JsonSerializer.Deserialize<TResult>(reader.GetString(4), jsonOptions)
            ?? throw new InvalidOperationException(
                $"Stored {commandType} result payload could not be deserialized.");
        return InventoryCommandAcquisition<TResult>.Replay(replay);
    }

    public static async ValueTask AppendAuditAsync(
        ErpDbContext dbContext,
        CommandId commandId,
        string commandType,
        ActorAccountId actorAccountId,
        string subjectKind,
        Guid subjectId,
        string? reasonText,
        DateTimeOffset occurredAt,
        JsonSerializerOptions jsonOptions,
        CancellationToken cancellationToken)
    {
        var auditEventId = Guid.CreateVersion7();
        var subjectKeyJson = JsonSerializer.Serialize(new { id = subjectId }, jsonOptions);

        await using (var auditEvent = CreateSqlCommand(
            dbContext,
            """
            INSERT INTO audit.audit_events
                (id, command_id, command_type, event_kind, actor_account_id, occurred_at, reason_text)
            VALUES
                (@id, @command_id, @command_type, 'BUSINESS_COMMAND', @actor_account_id, @occurred_at, @reason_text);
            """))
        {
            auditEvent.Parameters.AddWithValue("id", auditEventId);
            auditEvent.Parameters.AddWithValue("command_id", commandId.Value);
            auditEvent.Parameters.AddWithValue("command_type", commandType);
            auditEvent.Parameters.AddWithValue("actor_account_id", actorAccountId.Value);
            auditEvent.Parameters.AddWithValue("occurred_at", occurredAt);
            auditEvent.Parameters.AddWithValue("reason_text", (object?)reasonText ?? DBNull.Value);
            await auditEvent.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var subject = CreateSqlCommand(
            dbContext,
            """
            INSERT INTO audit.audit_event_subjects
                (audit_event_id, sequence, subject_kind, subject_key, change_kind,
                 before_row_version, after_row_version, change_summary)
            VALUES
                (@audit_event_id, 1, @subject_kind, CAST(@subject_key AS jsonb), 'CREATE',
                 NULL, NULL, NULL);
            """);
        subject.Parameters.AddWithValue("audit_event_id", auditEventId);
        subject.Parameters.AddWithValue("subject_kind", subjectKind);
        subject.Parameters.AddWithValue("subject_key", subjectKeyJson);
        await subject.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async ValueTask EnqueueOutboxAsync(
        ErpDbContext dbContext,
        string messageType,
        CommandId commandId,
        string payloadJson,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            dbContext,
            """
            INSERT INTO system.outbox_messages
                (id, message_type, message_version, payload, command_id,
                 occurred_at, available_at, published_at, delivery_attempt_count,
                 next_attempt_at, locked_until, lock_token, last_error_summary)
            VALUES
                (@id, @message_type, 1, CAST(@payload AS jsonb), @command_id,
                 @occurred_at, @occurred_at, NULL, 0,
                 NULL, NULL, NULL, NULL);
            """);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("message_type", messageType);
        command.Parameters.AddWithValue("payload", payloadJson);
        command.Parameters.AddWithValue("command_id", commandId.Value);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async ValueTask MarkSucceededAsync(
        ErpDbContext dbContext,
        CommandId commandId,
        string resultJson,
        DateTimeOffset executedAt,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            dbContext,
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
        command.Parameters.AddWithValue("command_id", commandId.Value);

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException(
                "CommandExecution could not be transitioned from IN_PROGRESS to SUCCEEDED.");
        }
    }

    public static NpgsqlCommand CreateSqlCommand(ErpDbContext dbContext, string sql)
    {
        var transaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Inventory SQL requires the active command transaction.");

        return new NpgsqlCommand(
            sql,
            (NpgsqlConnection)dbContext.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction());
    }
}

internal enum InventoryCommandAcquisitionKind
{
    Acquired,
    Replay,
    Conflict,
}

internal sealed record InventoryCommandAcquisition<TResult>(
    InventoryCommandAcquisitionKind Kind,
    TResult? ReplayResult)
{
    public static InventoryCommandAcquisition<TResult> Acquired() =>
        new(InventoryCommandAcquisitionKind.Acquired, default);

    public static InventoryCommandAcquisition<TResult> Replay(TResult result) =>
        new(InventoryCommandAcquisitionKind.Replay, result);

    public static InventoryCommandAcquisition<TResult> Conflict() =>
        new(InventoryCommandAcquisitionKind.Conflict, default);
}
