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
using YowThi.Erp.Application.Party;

namespace YowThi.Erp.Infrastructure.Persistence.Party;

internal sealed class PostgreSqlSupplierMasterExecutor : ISupplierMasterExecutor
{
    private const string CreateCommandType = "CreateSupplier";
    private const string UpdateCommandType = "UpdateSupplier";
    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlSupplierMasterExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public ValueTask<ApplicationResult<SupplierMasterWriteResult>> CreateAsync(
        CreateSupplierExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        var validationError = SupplierMasterValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ValueTask.FromResult(ApplicationResult<SupplierMasterWriteResult>.Failure(validationError));
        }

        return _transactionRunner.ExecuteAsync(
            async ct =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await AcquireCommandAsync<SupplierMasterWriteResult>(
                    execution.CommandId,
                    execution.RequestHash,
                    execution.ActorAccountId,
                    CreateCommandType,
                    now,
                    ct);

                if (acquisition.Kind == CommandAcquisitionKind.Replay)
                {
                    return CommandTransactionDecision<ApplicationResult<SupplierMasterWriteResult>>.Rollback(
                        ApplicationResult<SupplierMasterWriteResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == CommandAcquisitionKind.Conflict)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        SupplierMasterErrorCodes.IdempotencyKeyReused);
                }

                var supplierId = Guid.CreateVersion7();
                await InsertSupplierAsync(
                    supplierId,
                    execution.Command,
                    execution.ActorAccountId.Value,
                    now,
                    ct);

                var result = new SupplierMasterWriteResult(supplierId, 1);
                await AppendAuditAsync(
                    execution.CommandId,
                    execution.ActorAccountId,
                    CreateCommandType,
                    supplierId,
                    "CREATE",
                    null,
                    1,
                    now,
                    ct);
                await MarkCommandSucceededAsync(
                    execution.CommandId,
                    JsonSerializer.Serialize(result, StoredJsonOptions),
                    now,
                    ct);

                return CommandTransactionDecision<ApplicationResult<SupplierMasterWriteResult>>.Commit(
                    ApplicationResult<SupplierMasterWriteResult>.Success(result));
            },
            cancellationToken);
    }

    public ValueTask<ApplicationResult<SupplierMasterWriteResult>> UpdateAsync(
        UpdateSupplierExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        var validationError = SupplierMasterValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ValueTask.FromResult(ApplicationResult<SupplierMasterWriteResult>.Failure(validationError));
        }

        return _transactionRunner.ExecuteAsync(
            async ct =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await AcquireCommandAsync<SupplierMasterWriteResult>(
                    execution.CommandId,
                    execution.RequestHash,
                    execution.ActorAccountId,
                    UpdateCommandType,
                    now,
                    ct);

                if (acquisition.Kind == CommandAcquisitionKind.Replay)
                {
                    return CommandTransactionDecision<ApplicationResult<SupplierMasterWriteResult>>.Rollback(
                        ApplicationResult<SupplierMasterWriteResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == CommandAcquisitionKind.Conflict)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        SupplierMasterErrorCodes.IdempotencyKeyReused);
                }

                var state = await LockSupplierAsync(execution.Command.SupplierId, ct);
                if (state is null)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.NotFound,
                        SupplierMasterErrorCodes.SupplierNotFound);
                }

                if (state.DeletedAt is not null)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        SupplierMasterErrorCodes.SupplierDeleted);
                }

                if (state.RowVersion != execution.Command.ExpectedRowVersion)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        SupplierMasterErrorCodes.StaleRowVersion);
                }

                var nextRowVersion = await UpdateSupplierAsync(execution.Command, ct);
                if (nextRowVersion is null)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        SupplierMasterErrorCodes.StaleRowVersion);
                }

                var result = new SupplierMasterWriteResult(
                    execution.Command.SupplierId,
                    nextRowVersion.Value);
                await AppendAuditAsync(
                    execution.CommandId,
                    execution.ActorAccountId,
                    UpdateCommandType,
                    execution.Command.SupplierId,
                    "UPDATE",
                    state.RowVersion,
                    nextRowVersion.Value,
                    now,
                    ct);
                await MarkCommandSucceededAsync(
                    execution.CommandId,
                    JsonSerializer.Serialize(result, StoredJsonOptions),
                    now,
                    ct);

                return CommandTransactionDecision<ApplicationResult<SupplierMasterWriteResult>>.Commit(
                    ApplicationResult<SupplierMasterWriteResult>.Success(result));
            },
            cancellationToken);
    }

    private async ValueTask InsertSupplierAsync(
        Guid supplierId,
        CreateSupplierCommand command,
        Guid actorAccountId,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        await using var insert = CreateSqlCommand(
            """
            INSERT INTO party.suppliers
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@id, @name_zh_tw, @name_th_th, @bank_name, @bank_account, @phone, @address,
                 @active, 1, @created_at, @created_by_account_id, NULL, NULL);
            """);
        insert.Parameters.AddWithValue("id", supplierId);
        AddNullableText(insert, "name_zh_tw", command.NameZhTw);
        AddNullableText(insert, "name_th_th", command.NameThTh);
        AddNullableText(insert, "bank_name", command.BankName);
        AddNullableText(insert, "bank_account", command.BankAccount);
        AddNullableText(insert, "phone", command.Phone);
        AddNullableText(insert, "address", command.Address);
        insert.Parameters.AddWithValue("active", command.Active);
        insert.Parameters.AddWithValue("created_at", createdAt);
        insert.Parameters.AddWithValue("created_by_account_id", actorAccountId);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask<SupplierState?> LockSupplierAsync(
        Guid supplierId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            SELECT row_version, deleted_at
            FROM party.suppliers
            WHERE id = @supplier_id
            FOR UPDATE;
            """);
        command.Parameters.AddWithValue("supplier_id", supplierId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SupplierState(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1));
    }

    private async ValueTask<long?> UpdateSupplierAsync(
        UpdateSupplierCommand command,
        CancellationToken cancellationToken)
    {
        await using var update = CreateSqlCommand(
            """
            UPDATE party.suppliers
            SET name_zh_tw = @name_zh_tw,
                name_th_th = @name_th_th,
                bank_name = @bank_name,
                bank_account = @bank_account,
                phone = @phone,
                address = @address,
                active = @active,
                row_version = row_version + 1
            WHERE id = @supplier_id
              AND row_version = @expected_row_version
              AND deleted_at IS NULL
            RETURNING row_version;
            """);
        update.Parameters.AddWithValue("supplier_id", command.SupplierId);
        update.Parameters.AddWithValue("expected_row_version", command.ExpectedRowVersion);
        AddNullableText(update, "name_zh_tw", command.NameZhTw);
        AddNullableText(update, "name_th_th", command.NameThTh);
        AddNullableText(update, "bank_name", command.BankName);
        AddNullableText(update, "bank_account", command.BankAccount);
        AddNullableText(update, "phone", command.Phone);
        AddNullableText(update, "address", command.Address);
        update.Parameters.AddWithValue("active", command.Active);

        var value = await update.ExecuteScalarAsync(cancellationToken);
        return value is null ? null : Convert.ToInt64(value);
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
            throw new InvalidOperationException("Supplier master CommandExecution conflict could not be loaded.");
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
        Guid supplierId,
        string changeKind,
        long? beforeRowVersion,
        long afterRowVersion,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var auditEventId = Guid.CreateVersion7();
        var subjectKeyJson = JsonSerializer.Serialize(new { id = supplierId }, StoredJsonOptions);

        await using (var auditEvent = CreateSqlCommand(
            """
            INSERT INTO audit.audit_events
                (id, command_id, command_type, event_kind, actor_account_id, occurred_at, reason_text)
            VALUES
                (@id, @command_id, @command_type, 'BUSINESS_COMMAND', @actor_account_id, @occurred_at, NULL);
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
                (@audit_event_id, 1, 'party.supplier', CAST(@subject_key AS jsonb), @change_kind,
                 @before_row_version, @after_row_version, NULL);
            """);
        subject.Parameters.AddWithValue("audit_event_id", auditEventId);
        subject.Parameters.AddWithValue("subject_key", subjectKeyJson);
        subject.Parameters.AddWithValue("change_kind", changeKind);
        var beforeParameter = subject.Parameters.Add("before_row_version", NpgsqlDbType.Bigint);
        beforeParameter.Value = beforeRowVersion is null ? DBNull.Value : beforeRowVersion.Value;
        subject.Parameters.AddWithValue("after_row_version", afterRowVersion);
        await subject.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask MarkCommandSucceededAsync(
        CommandId commandId,
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
        command.Parameters.AddWithValue("command_id", commandId.Value);

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Supplier master CommandExecution could not transition to SUCCEEDED.");
        }
    }

    private NpgsqlCommand CreateSqlCommand(string sql)
    {
        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Supplier master SQL requires an active command transaction.");

        return new NpgsqlCommand(
            sql,
            (NpgsqlConnection)_dbContext.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction());
    }

    private static void AddNullableText(NpgsqlCommand command, string name, string? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Text);
        parameter.Value = Normalize(value) is { } normalized ? normalized : DBNull.Value;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CommandTransactionDecision<ApplicationResult<SupplierMasterWriteResult>> RollbackFailure(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<SupplierMasterWriteResult>>.Rollback(
            ApplicationResult<SupplierMasterWriteResult>.Failure(ApplicationError.Create(kind, code)));

    private sealed record SupplierState(long RowVersion, DateTimeOffset? DeletedAt);

    private enum CommandAcquisitionKind
    {
        Acquired,
        Replay,
        Conflict,
    }

    private sealed record CommandAcquisition<TResult>(
        CommandAcquisitionKind Kind,
        TResult? ReplayResult)
    {
        public static CommandAcquisition<TResult> Acquired() => new(CommandAcquisitionKind.Acquired, default);
        public static CommandAcquisition<TResult> Replay(TResult result) => new(CommandAcquisitionKind.Replay, result);
        public static CommandAcquisition<TResult> Conflict() => new(CommandAcquisitionKind.Conflict, default);
    }
}
