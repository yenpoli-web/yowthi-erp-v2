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
using YowThi.Erp.Application.Product;

namespace YowThi.Erp.Infrastructure.Persistence.Product;

internal sealed class PostgreSqlSalesProductGroupMasterExecutor : ISalesProductGroupMasterExecutor
{
    private const string CreateCommandType = "CreateSalesProductGroup";
    private const string UpdateCommandType = "UpdateSalesProductGroup";
    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlSalesProductGroupMasterExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public ValueTask<ApplicationResult<SalesProductGroupMasterWriteResult>> CreateAsync(
        CreateSalesProductGroupExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        var validationError = SalesProductGroupMasterValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ValueTask.FromResult(ApplicationResult<SalesProductGroupMasterWriteResult>.Failure(validationError));
        }

        return _transactionRunner.ExecuteAsync(
            async ct =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await AcquireCommandAsync<SalesProductGroupMasterWriteResult>(
                    execution.CommandId,
                    execution.RequestHash,
                    execution.ActorAccountId,
                    CreateCommandType,
                    now,
                    ct);

                if (acquisition.Kind == CommandAcquisitionKind.Replay)
                {
                    return CommandTransactionDecision<ApplicationResult<SalesProductGroupMasterWriteResult>>.Rollback(
                        ApplicationResult<SalesProductGroupMasterWriteResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == CommandAcquisitionKind.Conflict)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        SalesProductGroupMasterErrorCodes.IdempotencyKeyReused);
                }

                var salesProductGroupId = Guid.CreateVersion7();
                await InsertSalesProductGroupAsync(
                    salesProductGroupId,
                    execution.Command,
                    execution.ActorAccountId.Value,
                    now,
                    ct);

                var result = new SalesProductGroupMasterWriteResult(salesProductGroupId, 1);
                await AppendAuditAsync(
                    execution.CommandId,
                    execution.ActorAccountId,
                    CreateCommandType,
                    salesProductGroupId,
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

                return CommandTransactionDecision<ApplicationResult<SalesProductGroupMasterWriteResult>>.Commit(
                    ApplicationResult<SalesProductGroupMasterWriteResult>.Success(result));
            },
            cancellationToken);
    }

    public ValueTask<ApplicationResult<SalesProductGroupMasterWriteResult>> UpdateAsync(
        UpdateSalesProductGroupExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        var validationError = SalesProductGroupMasterValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ValueTask.FromResult(ApplicationResult<SalesProductGroupMasterWriteResult>.Failure(validationError));
        }

        return _transactionRunner.ExecuteAsync(
            async ct =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await AcquireCommandAsync<SalesProductGroupMasterWriteResult>(
                    execution.CommandId,
                    execution.RequestHash,
                    execution.ActorAccountId,
                    UpdateCommandType,
                    now,
                    ct);

                if (acquisition.Kind == CommandAcquisitionKind.Replay)
                {
                    return CommandTransactionDecision<ApplicationResult<SalesProductGroupMasterWriteResult>>.Rollback(
                        ApplicationResult<SalesProductGroupMasterWriteResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == CommandAcquisitionKind.Conflict)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        SalesProductGroupMasterErrorCodes.IdempotencyKeyReused);
                }

                var state = await LockSalesProductGroupAsync(execution.Command.SalesProductGroupId, ct);
                if (state is null)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.NotFound,
                        SalesProductGroupMasterErrorCodes.ItemNotFound);
                }

                if (state.DeletedAt is not null)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        SalesProductGroupMasterErrorCodes.ItemDeleted);
                }

                if (state.RowVersion != execution.Command.ExpectedRowVersion)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        SalesProductGroupMasterErrorCodes.StaleRowVersion);
                }

                var nextRowVersion = await UpdateSalesProductGroupAsync(execution.Command, ct);
                if (nextRowVersion is null)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        SalesProductGroupMasterErrorCodes.StaleRowVersion);
                }

                var result = new SalesProductGroupMasterWriteResult(
                    execution.Command.SalesProductGroupId,
                    nextRowVersion.Value);
                await AppendAuditAsync(
                    execution.CommandId,
                    execution.ActorAccountId,
                    UpdateCommandType,
                    execution.Command.SalesProductGroupId,
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

                return CommandTransactionDecision<ApplicationResult<SalesProductGroupMasterWriteResult>>.Commit(
                    ApplicationResult<SalesProductGroupMasterWriteResult>.Success(result));
            },
            cancellationToken);
    }

    private async ValueTask InsertSalesProductGroupAsync(
        Guid salesProductGroupId,
        CreateSalesProductGroupCommand command,
        Guid actorAccountId,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        await using var insert = CreateSqlCommand(
            """
            INSERT INTO product.sales_product_groups
                (id, name_zh_tw, name_th_th,
                 active, row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@id, @name_zh_tw, @name_th_th,
                 @active, 1, @created_at, @created_by_account_id, NULL, NULL);
            """);
        insert.Parameters.AddWithValue("id", salesProductGroupId);
        AddNullableText(insert, "name_zh_tw", command.NameZhTw);
        AddNullableText(insert, "name_th_th", command.NameThTh);
        insert.Parameters.AddWithValue("active", command.Active);
        insert.Parameters.AddWithValue("created_at", createdAt);
        insert.Parameters.AddWithValue("created_by_account_id", actorAccountId);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask<SalesProductGroupState?> LockSalesProductGroupAsync(
        Guid salesProductGroupId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            SELECT row_version, deleted_at
            FROM product.sales_product_groups
            WHERE id = @sales_product_group_id
            FOR UPDATE;
            """);
        command.Parameters.AddWithValue("sales_product_group_id", salesProductGroupId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SalesProductGroupState(
            reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1));
    }

    private async ValueTask<long?> UpdateSalesProductGroupAsync(
        UpdateSalesProductGroupCommand command,
        CancellationToken cancellationToken)
    {
        await using var update = CreateSqlCommand(
            """
            UPDATE product.sales_product_groups
            SET name_zh_tw = @name_zh_tw,
                name_th_th = @name_th_th,
                active = @active,
                row_version = row_version + 1
            WHERE id = @sales_product_group_id
              AND row_version = @expected_row_version
              AND deleted_at IS NULL
            RETURNING row_version;
            """);
        update.Parameters.AddWithValue("sales_product_group_id", command.SalesProductGroupId);
        update.Parameters.AddWithValue("expected_row_version", command.ExpectedRowVersion);
        AddNullableText(update, "name_zh_tw", command.NameZhTw);
        AddNullableText(update, "name_th_th", command.NameThTh);
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
            throw new InvalidOperationException("Sales Product Group master CommandExecution conflict could not be loaded.");
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
        Guid salesProductGroupId,
        string changeKind,
        long? beforeRowVersion,
        long afterRowVersion,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var auditEventId = Guid.CreateVersion7();
        var subjectKeyJson = JsonSerializer.Serialize(new { id = salesProductGroupId }, StoredJsonOptions);

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
                (@audit_event_id, 1, 'product.sales-product-group', CAST(@subject_key AS jsonb), @change_kind,
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
            throw new InvalidOperationException("Sales Product Group master CommandExecution could not transition to SUCCEEDED.");
        }
    }

    private NpgsqlCommand CreateSqlCommand(string sql)
    {
        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Sales Product Group master SQL requires an active command transaction.");

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

    private static CommandTransactionDecision<ApplicationResult<SalesProductGroupMasterWriteResult>> RollbackFailure(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<SalesProductGroupMasterWriteResult>>.Rollback(
            ApplicationResult<SalesProductGroupMasterWriteResult>.Failure(ApplicationError.Create(kind, code)));

    private sealed record SalesProductGroupState(long RowVersion, DateTimeOffset? DeletedAt);

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
