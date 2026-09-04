using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.DataProtection;

namespace YowThi.Erp.Infrastructure.Persistence.DataProtection;

internal sealed class PostgreSqlHardDeleteSupplierExecutor : IHardDeleteSupplierExecutor
{
    private const string CommandType = "HardDeleteSupplier";
    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlHardDeleteSupplierExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public async ValueTask<ApplicationResult<HardDeleteSupplierResult>> ExecuteAsync(
        HardDeleteSupplierExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);

        var validationError = HardDeleteSupplierValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ApplicationResult<HardDeleteSupplierResult>.Failure(validationError);
        }

        return await _transactionRunner.ExecuteAsync(
            async ct =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await AcquireCommandAsync(execution, now, ct);
                if (acquisition.Kind == CommandAcquisitionKind.Replay)
                {
                    return CommandTransactionDecision<ApplicationResult<HardDeleteSupplierResult>>.Rollback(
                        ApplicationResult<HardDeleteSupplierResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == CommandAcquisitionKind.Conflict)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        SupplierHardDeleteErrorCodes.IdempotencyKeyReused);
                }

                var command = execution.Command;
                var currentRowVersion = await LockSupplierAsync(command.SupplierId, ct);
                if (currentRowVersion is null)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.NotFound,
                        SupplierHardDeleteErrorCodes.SupplierNotFound);
                }

                if (currentRowVersion.Value != command.ExpectedRowVersion)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        SupplierHardDeleteErrorCodes.StaleRowVersion);
                }

                if (await HasDependenciesAsync(command.SupplierId, ct))
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        SupplierHardDeleteErrorCodes.DependencyBlocked);
                }

                if (await DeleteSupplierAsync(command.SupplierId, command.ExpectedRowVersion, ct) != 1)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        SupplierHardDeleteErrorCodes.StaleRowVersion);
                }

                var result = new HardDeleteSupplierResult(command.SupplierId);
                var storedResultJson = JsonSerializer.Serialize(result, StoredJsonOptions);

                await AppendHardDeleteAuditAsync(
                    execution,
                    currentRowVersion.Value,
                    now,
                    ct);
                await MarkCommandSucceededAsync(
                    execution.CommandId.Value,
                    storedResultJson,
                    now,
                    ct);

                return CommandTransactionDecision<ApplicationResult<HardDeleteSupplierResult>>.Commit(
                    ApplicationResult<HardDeleteSupplierResult>.Success(result));
            },
            cancellationToken);
    }

    private async ValueTask<long?> LockSupplierAsync(Guid supplierId, CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            SELECT row_version
            FROM party.suppliers
            WHERE id = @supplier_id
            FOR UPDATE;
            """);
        command.Parameters.AddWithValue("supplier_id", supplierId);
        return (long?)(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async ValueTask<bool> HasDependenciesAsync(Guid supplierId, CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            SELECT
                EXISTS (SELECT 1 FROM procurement.procurement_entries WHERE supplier_id = @supplier_id)
                OR EXISTS (SELECT 1 FROM processing.processing_executions WHERE supplier_id = @supplier_id)
                OR EXISTS (SELECT 1 FROM inventory.inventory_movements WHERE supplier_id = @supplier_id)
                OR EXISTS (SELECT 1 FROM inventory.inventory_positions WHERE supplier_id = @supplier_id)
                OR EXISTS (SELECT 1 FROM finance.payables WHERE supplier_id = @supplier_id);
            """);
        command.Parameters.AddWithValue("supplier_id", supplierId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Supplier dependency assessment returned no result."));
    }

    private async ValueTask<int> DeleteSupplierAsync(
        Guid supplierId,
        long expectedRowVersion,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            DELETE FROM party.suppliers
            WHERE id = @supplier_id
              AND row_version = @expected_row_version;
            """);
        command.Parameters.AddWithValue("supplier_id", supplierId);
        command.Parameters.AddWithValue("expected_row_version", expectedRowVersion);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask<CommandAcquisition> AcquireCommandAsync(
        HardDeleteSupplierExecution execution,
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
        select.Parameters.AddWithValue("command_id", execution.CommandId.Value);

        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Hard Delete CommandExecution conflict was observed but the stored command could not be loaded.");
        }

        if (!string.Equals(reader.GetString(0), CommandType, StringComparison.Ordinal)
            || reader.GetGuid(3) != execution.ActorAccountId.Value
            || !execution.RequestHash.Bytes.Span.SequenceEqual(reader.GetFieldValue<byte[]>(1)))
        {
            return CommandAcquisition.Conflict();
        }

        if (!string.Equals(reader.GetString(2), "SUCCEEDED", StringComparison.Ordinal) || reader.IsDBNull(4))
        {
            throw new InvalidOperationException(
                "A committed HardDeleteSupplier CommandExecution with the same identity is not replayable.");
        }

        var replay = JsonSerializer.Deserialize<HardDeleteSupplierResult>(reader.GetString(4), StoredJsonOptions)
            ?? throw new InvalidOperationException("Stored HardDeleteSupplier result payload could not be deserialized.");
        return CommandAcquisition.Replay(replay);
    }

    private async ValueTask AppendHardDeleteAuditAsync(
        HardDeleteSupplierExecution execution,
        long beforeRowVersion,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var auditEventId = Guid.CreateVersion7();
        var subjectKeyJson = JsonSerializer.Serialize(
            new { id = execution.Command.SupplierId },
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
                (@audit_event_id, 1, 'party.supplier', CAST(@subject_key AS jsonb), 'HARD_DELETE',
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
        {
            throw new InvalidOperationException("Hard Delete CommandExecution could not be transitioned from IN_PROGRESS to SUCCEEDED.");
        }
    }

    private NpgsqlCommand CreateSqlCommand(string sql)
    {
        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("HardDeleteSupplier SQL requires an active command transaction.");

        return new NpgsqlCommand(
            sql,
            (NpgsqlConnection)_dbContext.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction());
    }

    private static CommandTransactionDecision<ApplicationResult<HardDeleteSupplierResult>> RollbackFailure(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<HardDeleteSupplierResult>>.Rollback(
            ApplicationResult<HardDeleteSupplierResult>.Failure(
                ApplicationError.Create(kind, code)));

    private enum CommandAcquisitionKind
    {
        Acquired,
        Replay,
        Conflict,
    }

    private sealed record CommandAcquisition(
        CommandAcquisitionKind Kind,
        HardDeleteSupplierResult? ReplayResult)
    {
        public static CommandAcquisition Acquired() => new(CommandAcquisitionKind.Acquired, null);
        public static CommandAcquisition Replay(HardDeleteSupplierResult result) => new(CommandAcquisitionKind.Replay, result);
        public static CommandAcquisition Conflict() => new(CommandAcquisitionKind.Conflict, null);
    }
}
