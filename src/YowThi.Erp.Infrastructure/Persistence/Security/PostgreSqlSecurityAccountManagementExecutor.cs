using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Security;

namespace YowThi.Erp.Infrastructure.Persistence.Security;

internal sealed class PostgreSqlSecurityAccountManagementExecutor : ISecurityAccountManagementExecutor
{
    private const string CreateCommandType = "CreateSecurityAccount";
    private const string UpdateCommandType = "UpdateSecurityAccount";
    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlSecurityAccountManagementExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public ValueTask<ApplicationResult<SecurityAccountWriteResult>> CreateAsync(
        CreateSecurityAccountExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        var validationError = SecurityAccountValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ValueTask.FromResult(ApplicationResult<SecurityAccountWriteResult>.Failure(validationError));
        }

        return _transactionRunner.ExecuteAsync(
            async ct =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await AcquireCommandAsync<SecurityAccountWriteResult>(
                    execution.CommandId,
                    execution.RequestHash,
                    execution.ActorAccountId,
                    CreateCommandType,
                    now,
                    ct);

                if (acquisition.Kind == CommandAcquisitionKind.Replay)
                {
                    return CommandTransactionDecision<ApplicationResult<SecurityAccountWriteResult>>.Rollback(
                        ApplicationResult<SecurityAccountWriteResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == CommandAcquisitionKind.Conflict)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, SecurityAccountErrorCodes.IdempotencyKeyReused);
                }

                var command = Normalize(execution.Command);
                if (await ExternalIdentityExistsAsync(command.IdentityIssuer, command.IdentitySubject, null, ct))
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, SecurityAccountErrorCodes.ExternalIdentityConflict);
                }

                var accountId = Guid.CreateVersion7();
                await InsertAccountAsync(accountId, command, now, ct);
                await SynchronizeCapabilitiesAsync(
                    accountId,
                    command.Capabilities,
                    execution.ActorAccountId.Value,
                    now,
                    ct);

                var result = new SecurityAccountWriteResult(accountId, 1);
                await AppendAuditAsync(
                    execution.CommandId,
                    execution.ActorAccountId,
                    CreateCommandType,
                    accountId,
                    "CREATE",
                    null,
                    1,
                    command.Capabilities,
                    now,
                    ct);
                await MarkCommandSucceededAsync(execution.CommandId, result, now, ct);

                return CommandTransactionDecision<ApplicationResult<SecurityAccountWriteResult>>.Commit(
                    ApplicationResult<SecurityAccountWriteResult>.Success(result));
            },
            cancellationToken);
    }

    public ValueTask<ApplicationResult<SecurityAccountWriteResult>> UpdateAsync(
        UpdateSecurityAccountExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        var validationError = SecurityAccountValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ValueTask.FromResult(ApplicationResult<SecurityAccountWriteResult>.Failure(validationError));
        }

        return _transactionRunner.ExecuteAsync(
            async ct =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await AcquireCommandAsync<SecurityAccountWriteResult>(
                    execution.CommandId,
                    execution.RequestHash,
                    execution.ActorAccountId,
                    UpdateCommandType,
                    now,
                    ct);

                if (acquisition.Kind == CommandAcquisitionKind.Replay)
                {
                    return CommandTransactionDecision<ApplicationResult<SecurityAccountWriteResult>>.Rollback(
                        ApplicationResult<SecurityAccountWriteResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == CommandAcquisitionKind.Conflict)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, SecurityAccountErrorCodes.IdempotencyKeyReused);
                }

                var command = Normalize(execution.Command);
                var state = await LockAccountAsync(command.AccountId, ct);
                if (state is null)
                {
                    return RollbackFailure(ApplicationErrorKind.NotFound, SecurityAccountErrorCodes.AccountNotFound);
                }

                if (state.RowVersion != command.ExpectedRowVersion)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, SecurityAccountErrorCodes.StaleRowVersion);
                }

                if (state.IsDevelopmentTestAdmin)
                {
                    return RollbackFailure(ApplicationErrorKind.Forbidden, SecurityAccountErrorCodes.InvalidInput);
                }

                if (await ExternalIdentityExistsAsync(command.IdentityIssuer, command.IdentitySubject, command.AccountId, ct))
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, SecurityAccountErrorCodes.ExternalIdentityConflict);
                }

                var nextRowVersion = await UpdateAccountAsync(command, ct);
                if (nextRowVersion is null)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, SecurityAccountErrorCodes.StaleRowVersion);
                }

                await SynchronizeCapabilitiesAsync(
                    command.AccountId,
                    command.Capabilities,
                    execution.ActorAccountId.Value,
                    now,
                    ct);

                var result = new SecurityAccountWriteResult(command.AccountId, nextRowVersion.Value);
                await AppendAuditAsync(
                    execution.CommandId,
                    execution.ActorAccountId,
                    UpdateCommandType,
                    command.AccountId,
                    "UPDATE",
                    state.RowVersion,
                    nextRowVersion.Value,
                    command.Capabilities,
                    now,
                    ct);
                await MarkCommandSucceededAsync(execution.CommandId, result, now, ct);

                return CommandTransactionDecision<ApplicationResult<SecurityAccountWriteResult>>.Commit(
                    ApplicationResult<SecurityAccountWriteResult>.Success(result));
            },
            cancellationToken);
    }

    private async ValueTask InsertAccountAsync(
        Guid accountId,
        CreateSecurityAccountCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var insert = CreateSqlCommand(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, row_version, created_at)
            VALUES
                (@id, @display_name, @active, @identity_issuer, @identity_subject, 1, @created_at);
            """);
        insert.Parameters.AddWithValue("id", accountId);
        insert.Parameters.AddWithValue("display_name", command.DisplayName);
        insert.Parameters.AddWithValue("active", command.Active);
        AddNullableText(insert, "identity_issuer", command.IdentityIssuer);
        AddNullableText(insert, "identity_subject", command.IdentitySubject);
        insert.Parameters.AddWithValue("created_at", now);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask<AccountState?> LockAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            SELECT row_version, identity_issuer, identity_subject
            FROM system.accounts
            WHERE id = @account_id
            FOR UPDATE;
            """);
        command.Parameters.AddWithValue("account_id", accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var issuer = reader.IsDBNull(1) ? null : reader.GetString(1);
        var subject = reader.IsDBNull(2) ? null : reader.GetString(2);
        return new AccountState(
            reader.GetInt64(0),
            string.Equals(issuer, DevelopmentTestAdmin.IdentityIssuer, StringComparison.Ordinal)
                && string.Equals(subject, DevelopmentTestAdmin.IdentitySubject, StringComparison.Ordinal));
    }

    private async ValueTask<long?> UpdateAccountAsync(
        UpdateSecurityAccountCommand command,
        CancellationToken cancellationToken)
    {
        await using var update = CreateSqlCommand(
            """
            UPDATE system.accounts
            SET display_name = @display_name,
                active = @active,
                identity_issuer = @identity_issuer,
                identity_subject = @identity_subject,
                row_version = row_version + 1
            WHERE id = @account_id
              AND row_version = @expected_row_version
            RETURNING row_version;
            """);
        update.Parameters.AddWithValue("account_id", command.AccountId);
        update.Parameters.AddWithValue("expected_row_version", command.ExpectedRowVersion);
        update.Parameters.AddWithValue("display_name", command.DisplayName);
        update.Parameters.AddWithValue("active", command.Active);
        AddNullableText(update, "identity_issuer", command.IdentityIssuer);
        AddNullableText(update, "identity_subject", command.IdentitySubject);
        var value = await update.ExecuteScalarAsync(cancellationToken);
        return value is null ? null : Convert.ToInt64(value);
    }

    private async ValueTask<bool> ExternalIdentityExistsAsync(
        string? issuer,
        string? subject,
        Guid? exceptAccountId,
        CancellationToken cancellationToken)
    {
        if (issuer is null || subject is null)
        {
            return false;
        }

        await using var command = CreateSqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM system.accounts
                WHERE identity_issuer = @issuer
                  AND identity_subject = @subject
                  AND (@except_account_id IS NULL OR id <> @except_account_id)
            );
            """);
        command.Parameters.AddWithValue("issuer", issuer);
        command.Parameters.AddWithValue("subject", subject);
        var except = command.Parameters.Add("except_account_id", NpgsqlDbType.Uuid);
        except.Value = exceptAccountId is null ? DBNull.Value : exceptAccountId.Value;
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private async ValueTask SynchronizeCapabilitiesAsync(
        Guid accountId,
        IReadOnlyList<string> requestedCapabilities,
        Guid actorAccountId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var capabilities = requestedCapabilities.OrderBy(value => value, StringComparer.Ordinal).ToArray();

        await using (var deactivate = CreateSqlCommand(
            """
            UPDATE system.account_capability_grants
            SET active = false,
                row_version = row_version + 1
            WHERE account_id = @account_id
              AND active = true
              AND NOT (capability_name = ANY(@capabilities));
            """))
        {
            deactivate.Parameters.AddWithValue("account_id", accountId);
            deactivate.Parameters.AddWithValue("capabilities", capabilities);
            await deactivate.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var capability in capabilities)
        {
            await using var grant = CreateSqlCommand(
                """
                INSERT INTO system.account_capability_grants
                    (id, account_id, capability_name, active, row_version, created_at, created_by_account_id)
                VALUES
                    (@id, @account_id, @capability_name, true, 1, @created_at, @created_by_account_id)
                ON CONFLICT (account_id, capability_name) DO UPDATE
                SET active = true,
                    row_version = CASE
                        WHEN system.account_capability_grants.active = false
                            THEN system.account_capability_grants.row_version + 1
                        ELSE system.account_capability_grants.row_version
                    END;
                """);
            grant.Parameters.AddWithValue("id", Guid.CreateVersion7());
            grant.Parameters.AddWithValue("account_id", accountId);
            grant.Parameters.AddWithValue("capability_name", capability);
            grant.Parameters.AddWithValue("created_at", now);
            grant.Parameters.AddWithValue("created_by_account_id", actorAccountId);
            await grant.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async ValueTask<CommandAcquisition<TResult>> AcquireCommandAsync<TResult>(
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
                return CommandAcquisition<TResult>.Acquired();
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
            throw new InvalidOperationException("Security account CommandExecution conflict could not be loaded.");
        }

        if (!string.Equals(reader.GetString(0), commandType, StringComparison.Ordinal)
            || reader.GetGuid(3) != actorAccountId.Value
            || !requestHash.Bytes.Span.SequenceEqual(reader.GetFieldValue<byte[]>(1)))
        {
            return CommandAcquisition<TResult>.Conflict();
        }

        if (!string.Equals(reader.GetString(2), "SUCCEEDED", StringComparison.Ordinal) || reader.IsDBNull(4))
        {
            throw new InvalidOperationException(
                $"A committed {commandType} CommandExecution with the same identity is not replayable.");
        }

        var replay = JsonSerializer.Deserialize<TResult>(reader.GetString(4), StoredJsonOptions)
            ?? throw new InvalidOperationException($"Stored {commandType} result could not be deserialized.");
        return CommandAcquisition<TResult>.Replay(replay);
    }

    private async ValueTask AppendAuditAsync(
        CommandId commandId,
        ActorAccountId actorAccountId,
        string commandType,
        Guid accountId,
        string changeKind,
        long? beforeRowVersion,
        long afterRowVersion,
        IReadOnlyList<string> capabilities,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var auditEventId = Guid.CreateVersion7();
        var subjectKeyJson = JsonSerializer.Serialize(new { id = accountId }, StoredJsonOptions);
        var changeSummaryJson = JsonSerializer.Serialize(new { capabilities }, StoredJsonOptions);

        await using (var auditEvent = CreateSqlCommand(
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

        await using var subject = CreateSqlCommand(
            """
            INSERT INTO audit.audit_event_subjects
                (audit_event_id, sequence, subject_kind, subject_key, change_kind,
                 before_row_version, after_row_version, change_summary)
            VALUES
                (@audit_event_id, 1, 'system.account', CAST(@subject_key AS jsonb), @change_kind,
                 @before_row_version, @after_row_version, CAST(@change_summary AS jsonb));
            """);
        subject.Parameters.AddWithValue("audit_event_id", auditEventId);
        subject.Parameters.AddWithValue("subject_key", subjectKeyJson);
        subject.Parameters.AddWithValue("change_kind", changeKind);
        var before = subject.Parameters.Add("before_row_version", NpgsqlDbType.Bigint);
        before.Value = beforeRowVersion is null ? DBNull.Value : beforeRowVersion.Value;
        subject.Parameters.AddWithValue("after_row_version", afterRowVersion);
        subject.Parameters.AddWithValue("change_summary", changeSummaryJson);
        await subject.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask MarkCommandSucceededAsync(
        CommandId commandId,
        SecurityAccountWriteResult result,
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
        command.Parameters.AddWithValue("result_payload", JsonSerializer.Serialize(result, StoredJsonOptions));
        command.Parameters.AddWithValue("executed_at", executedAt);
        command.Parameters.AddWithValue("command_id", commandId.Value);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Security account CommandExecution could not transition to SUCCEEDED.");
        }
    }

    private NpgsqlCommand CreateSqlCommand(string sql)
    {
        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Security account SQL requires an active command transaction.");

        return new NpgsqlCommand(
            sql,
            (NpgsqlConnection)_dbContext.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction());
    }

    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Text);
        parameter.Value = value is null ? DBNull.Value : value;
    }

    private static CreateSecurityAccountCommand Normalize(CreateSecurityAccountCommand command) =>
        new(
            command.DisplayName.Trim(),
            command.Active,
            NormalizeOptional(command.IdentityIssuer),
            NormalizeOptional(command.IdentitySubject),
            command.Capabilities.OrderBy(value => value, StringComparer.Ordinal).ToArray());

    private static UpdateSecurityAccountCommand Normalize(UpdateSecurityAccountCommand command) =>
        new(
            command.AccountId,
            command.ExpectedRowVersion,
            command.DisplayName.Trim(),
            command.Active,
            NormalizeOptional(command.IdentityIssuer),
            NormalizeOptional(command.IdentitySubject),
            command.Capabilities.OrderBy(value => value, StringComparer.Ordinal).ToArray());

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CommandTransactionDecision<ApplicationResult<SecurityAccountWriteResult>> RollbackFailure(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<SecurityAccountWriteResult>>.Rollback(
            ApplicationResult<SecurityAccountWriteResult>.Failure(ApplicationError.Create(kind, code)));

    private sealed record AccountState(long RowVersion, bool IsDevelopmentTestAdmin);

    private enum CommandAcquisitionKind
    {
        Acquired,
        Replay,
        Conflict,
    }

    private sealed record CommandAcquisition<TResult>(CommandAcquisitionKind Kind, TResult? ReplayResult)
    {
        public static CommandAcquisition<TResult> Acquired() => new(CommandAcquisitionKind.Acquired, default);
        public static CommandAcquisition<TResult> Replay(TResult result) => new(CommandAcquisitionKind.Replay, result);
        public static CommandAcquisition<TResult> Conflict() => new(CommandAcquisitionKind.Conflict, default);
    }
}
