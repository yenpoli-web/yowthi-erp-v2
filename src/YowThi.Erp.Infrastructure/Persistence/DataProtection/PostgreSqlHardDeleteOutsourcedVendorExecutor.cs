using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.DataProtection;

namespace YowThi.Erp.Infrastructure.Persistence.DataProtection;

internal sealed class PostgreSqlHardDeleteOutsourcedVendorExecutor : IHardDeleteOutsourcedVendorExecutor
{
    private const string CommandType = "HardDeleteOutsourcedVendor";
    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlHardDeleteOutsourcedVendorExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public async ValueTask<ApplicationResult<HardDeleteOutsourcedVendorResult>> ExecuteAsync(
        HardDeleteOutsourcedVendorExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);

        var validationError = HardDeleteOutsourcedVendorValidation.Validate(execution.Command);
        if (validationError is not null)
            return ApplicationResult<HardDeleteOutsourcedVendorResult>.Failure(validationError);

        return await _transactionRunner.ExecuteAsync(
            async ct =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await AcquireCommandAsync(execution, now, ct);
                if (acquisition.Kind == CommandAcquisitionKind.Replay)
                {
                    return CommandTransactionDecision<ApplicationResult<HardDeleteOutsourcedVendorResult>>.Rollback(
                        ApplicationResult<HardDeleteOutsourcedVendorResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == CommandAcquisitionKind.Conflict)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        OutsourcedVendorHardDeleteErrorCodes.IdempotencyKeyReused);
                }

                var command = execution.Command;
                var currentRowVersion = await LockOutsourcedVendorAsync(command.OutsourcedVendorId, ct);
                if (currentRowVersion is null)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.NotFound,
                        OutsourcedVendorHardDeleteErrorCodes.OutsourcedVendorNotFound);
                }

                if (currentRowVersion.Value != command.ExpectedRowVersion)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        OutsourcedVendorHardDeleteErrorCodes.StaleRowVersion);
                }

                if (await HasDependenciesAsync(command.OutsourcedVendorId, ct))
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        OutsourcedVendorHardDeleteErrorCodes.DependencyBlocked);
                }

                if (await DeleteOutsourcedVendorAsync(command.OutsourcedVendorId, command.ExpectedRowVersion, ct) != 1)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        OutsourcedVendorHardDeleteErrorCodes.StaleRowVersion);
                }

                var result = new HardDeleteOutsourcedVendorResult(command.OutsourcedVendorId);
                var storedResultJson = JsonSerializer.Serialize(result, StoredJsonOptions);

                await AppendHardDeleteAuditAsync(execution, currentRowVersion.Value, now, ct);
                await MarkCommandSucceededAsync(execution.CommandId.Value, storedResultJson, now, ct);

                return CommandTransactionDecision<ApplicationResult<HardDeleteOutsourcedVendorResult>>.Commit(
                    ApplicationResult<HardDeleteOutsourcedVendorResult>.Success(result));
            },
            cancellationToken);
    }

    private async ValueTask<long?> LockOutsourcedVendorAsync(
        Guid outsourcedVendorId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            SELECT row_version
            FROM party.outsourced_vendors
            WHERE id = @outsourced_vendor_id
            FOR UPDATE;
            """);
        command.Parameters.AddWithValue("outsourced_vendor_id", outsourcedVendorId);
        return (long?)(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async ValueTask<bool> HasDependenciesAsync(
        Guid outsourcedVendorId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM outsourced.outsourced_supply_batches
                WHERE outsourced_vendor_id = @outsourced_vendor_id
            );
            """);
        command.Parameters.AddWithValue("outsourced_vendor_id", outsourcedVendorId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Outsourced Vendor dependency assessment returned no result."));
    }

    private async ValueTask<int> DeleteOutsourcedVendorAsync(
        Guid outsourcedVendorId,
        long expectedRowVersion,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            DELETE FROM party.outsourced_vendors
            WHERE id = @outsourced_vendor_id
              AND row_version = @expected_row_version;
            """);
        command.Parameters.AddWithValue("outsourced_vendor_id", outsourcedVendorId);
        command.Parameters.AddWithValue("expected_row_version", expectedRowVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask<CommandAcquisition> AcquireCommandAsync(
        HardDeleteOutsourcedVendorExecution execution,
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
            insert.Parameters.AddWithValue("command_id", execution.CommandId.Value);
            insert.Parameters.AddWithValue("command_type", CommandType);
            insert.Parameters.AddWithValue("request_hash", execution.RequestHash.Bytes.ToArray());
            insert.Parameters.AddWithValue("actor_account_id", execution.ActorAccountId.Value);
            insert.Parameters.AddWithValue("started_at", startedAt);

            if (await insert.ExecuteNonQueryAsync(cancellationToken) == 1)
                return CommandAcquisition.Acquired();
        }

        await using var select = CreateSqlCommand(
            """
            SELECT command_type, request_hash, status, actor_account_id, result_payload::text
            FROM system.command_executions
            WHERE command_id = @command_id;
            """);
        select.Parameters.AddWithValue("command_id", execution.CommandId.Value);

        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Outsourced Vendor Hard Delete CommandExecution conflict could not be loaded.");

        if (!string.Equals(reader.GetString(0), CommandType, StringComparison.Ordinal)
            || reader.GetGuid(3) != execution.ActorAccountId.Value
            || !execution.RequestHash.Bytes.Span.SequenceEqual(reader.GetFieldValue<byte[]>(1)))
        {
            return CommandAcquisition.Conflict();
        }

        if (!string.Equals(reader.GetString(2), "SUCCEEDED", StringComparison.Ordinal) || reader.IsDBNull(4))
        {
            throw new InvalidOperationException(
                "A committed HardDeleteOutsourcedVendor CommandExecution with the same identity is not replayable.");
        }

        var replay = JsonSerializer.Deserialize<HardDeleteOutsourcedVendorResult>(
            reader.GetString(4), StoredJsonOptions)
            ?? throw new InvalidOperationException("Stored HardDeleteOutsourcedVendor result could not be deserialized.");
        return CommandAcquisition.Replay(replay);
    }

    private async ValueTask AppendHardDeleteAuditAsync(
        HardDeleteOutsourcedVendorExecution execution,
        long beforeRowVersion,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var auditEventId = Guid.CreateVersion7();
        var subjectKeyJson = JsonSerializer.Serialize(
            new { id = execution.Command.OutsourcedVendorId },
            StoredJsonOptions);

        await using (var auditEvent = CreateSqlCommand(
            """
            INSERT INTO audit.audit_events
                (id, command_id, command_type, event_kind, actor_account_id, occurred_at, reason_text)
            VALUES
                (@id, @command_id, @command_type, 'HARD_DELETE', @actor_account_id, @occurred_at, NULL);
            """))
        {
            auditEvent.Parameters.AddWithValue("id", auditEventId);
            auditEvent.Parameters.AddWithValue("command_id", execution.CommandId.Value);
            auditEvent.Parameters.AddWithValue("command_type", CommandType);
            auditEvent.Parameters.AddWithValue("actor_account_id", execution.ActorAccountId.Value);
            auditEvent.Parameters.AddWithValue("occurred_at", occurredAt);
            await auditEvent.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var subject = CreateSqlCommand(
            """
            INSERT INTO audit.audit_event_subjects
                (audit_event_id, sequence, subject_kind, subject_key, change_kind,
                 before_row_version, after_row_version, change_summary)
            VALUES
                (@audit_event_id, 1, 'party.outsourced-vendor', CAST(@subject_key AS jsonb), 'HARD_DELETE',
                 @before_row_version, NULL, NULL);
            """);
        subject.Parameters.AddWithValue("audit_event_id", auditEventId);
        subject.Parameters.AddWithValue("subject_key", subjectKeyJson);
        subject.Parameters.AddWithValue("before_row_version", beforeRowVersion);
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
            throw new InvalidOperationException("Outsourced Vendor Hard Delete CommandExecution could not transition to SUCCEEDED.");
    }

    private NpgsqlCommand CreateSqlCommand(string sql)
    {
        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("HardDeleteOutsourcedVendor SQL requires an active command transaction.");

        return new NpgsqlCommand(
            sql,
            (NpgsqlConnection)_dbContext.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction());
    }

    private static CommandTransactionDecision<ApplicationResult<HardDeleteOutsourcedVendorResult>> RollbackFailure(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<HardDeleteOutsourcedVendorResult>>.Rollback(
            ApplicationResult<HardDeleteOutsourcedVendorResult>.Failure(ApplicationError.Create(kind, code)));

    private enum CommandAcquisitionKind
    {
        Acquired,
        Replay,
        Conflict,
    }

    private sealed record CommandAcquisition(
        CommandAcquisitionKind Kind,
        HardDeleteOutsourcedVendorResult? ReplayResult)
    {
        public static CommandAcquisition Acquired() => new(CommandAcquisitionKind.Acquired, null);
        public static CommandAcquisition Replay(HardDeleteOutsourcedVendorResult result) =>
            new(CommandAcquisitionKind.Replay, result);
        public static CommandAcquisition Conflict() => new(CommandAcquisitionKind.Conflict, null);
    }
}
