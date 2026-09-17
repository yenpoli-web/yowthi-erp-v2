using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Product;

namespace YowThi.Erp.Infrastructure.Persistence.Product;

internal sealed class PostgreSqlProcurementProductLifecycleExecutor : IProcurementProductLifecycleExecutor
{
    private readonly ProductLifecycleSqlSupport _support;

    public PostgreSqlProcurementProductLifecycleExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _support = new ProductLifecycleSqlSupport(dbContext, transactionRunner, timeProvider);
    }

    public ValueTask<ApplicationResult<ProcurementProductLifecycleResult>> SoftDeleteAsync(
        SoftDeleteProcurementProductExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        var validation = ProcurementProductLifecycleValidation.Validate(execution.Command);
        if (validation is not null)
        {
            return ValueTask.FromResult(ApplicationResult<ProcurementProductLifecycleResult>.Failure(validation));
        }

        return _support.ExecuteAsync(
            execution.CommandId,
            execution.RequestHash,
            execution.ActorAccountId,
            execution.Command.ProcurementProductId,
            execution.Command.ExpectedRowVersion,
            true,
            "SoftDeleteProcurementProduct",
            "product.procurement_products",
            "product.procurement-product",
            ProcurementProductLifecycleErrorCodes.ProductNotFound,
            ProcurementProductLifecycleErrorCodes.AlreadyDeleted,
            ProcurementProductLifecycleErrorCodes.NotDeleted,
            ProcurementProductLifecycleErrorCodes.StaleRowVersion,
            ProcurementProductLifecycleErrorCodes.IdempotencyKeyReused,
            (id, rowVersion, deleted) => new ProcurementProductLifecycleResult(id, rowVersion, deleted),
            cancellationToken);
    }

    public ValueTask<ApplicationResult<ProcurementProductLifecycleResult>> RestoreAsync(
        RestoreProcurementProductExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        var validation = ProcurementProductLifecycleValidation.Validate(execution.Command);
        if (validation is not null)
        {
            return ValueTask.FromResult(ApplicationResult<ProcurementProductLifecycleResult>.Failure(validation));
        }

        return _support.ExecuteAsync(
            execution.CommandId,
            execution.RequestHash,
            execution.ActorAccountId,
            execution.Command.ProcurementProductId,
            execution.Command.ExpectedRowVersion,
            false,
            "RestoreProcurementProduct",
            "product.procurement_products",
            "product.procurement-product",
            ProcurementProductLifecycleErrorCodes.ProductNotFound,
            ProcurementProductLifecycleErrorCodes.AlreadyDeleted,
            ProcurementProductLifecycleErrorCodes.NotDeleted,
            ProcurementProductLifecycleErrorCodes.StaleRowVersion,
            ProcurementProductLifecycleErrorCodes.IdempotencyKeyReused,
            (id, rowVersion, deleted) => new ProcurementProductLifecycleResult(id, rowVersion, deleted),
            cancellationToken);
    }
}

internal sealed class PostgreSqlSalesProductLifecycleExecutor : ISalesProductLifecycleExecutor
{
    private readonly ProductLifecycleSqlSupport _support;

    public PostgreSqlSalesProductLifecycleExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _support = new ProductLifecycleSqlSupport(dbContext, transactionRunner, timeProvider);
    }

    public ValueTask<ApplicationResult<SalesProductLifecycleResult>> SoftDeleteAsync(
        SoftDeleteSalesProductExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        var validation = SalesProductLifecycleValidation.Validate(execution.Command);
        if (validation is not null)
        {
            return ValueTask.FromResult(ApplicationResult<SalesProductLifecycleResult>.Failure(validation));
        }

        return _support.ExecuteAsync(
            execution.CommandId,
            execution.RequestHash,
            execution.ActorAccountId,
            execution.Command.SalesProductId,
            execution.Command.ExpectedRowVersion,
            true,
            "SoftDeleteSalesProduct",
            "product.sales_products",
            "product.sales-product",
            SalesProductLifecycleErrorCodes.ProductNotFound,
            SalesProductLifecycleErrorCodes.AlreadyDeleted,
            SalesProductLifecycleErrorCodes.NotDeleted,
            SalesProductLifecycleErrorCodes.StaleRowVersion,
            SalesProductLifecycleErrorCodes.IdempotencyKeyReused,
            (id, rowVersion, deleted) => new SalesProductLifecycleResult(id, rowVersion, deleted),
            cancellationToken);
    }

    public ValueTask<ApplicationResult<SalesProductLifecycleResult>> RestoreAsync(
        RestoreSalesProductExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        var validation = SalesProductLifecycleValidation.Validate(execution.Command);
        if (validation is not null)
        {
            return ValueTask.FromResult(ApplicationResult<SalesProductLifecycleResult>.Failure(validation));
        }

        return _support.ExecuteAsync(
            execution.CommandId,
            execution.RequestHash,
            execution.ActorAccountId,
            execution.Command.SalesProductId,
            execution.Command.ExpectedRowVersion,
            false,
            "RestoreSalesProduct",
            "product.sales_products",
            "product.sales-product",
            SalesProductLifecycleErrorCodes.ProductNotFound,
            SalesProductLifecycleErrorCodes.AlreadyDeleted,
            SalesProductLifecycleErrorCodes.NotDeleted,
            SalesProductLifecycleErrorCodes.StaleRowVersion,
            SalesProductLifecycleErrorCodes.IdempotencyKeyReused,
            (id, rowVersion, deleted) => new SalesProductLifecycleResult(id, rowVersion, deleted),
            cancellationToken);
    }
}

internal sealed class ProductLifecycleSqlSupport
{
    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public ProductLifecycleSqlSupport(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public ValueTask<ApplicationResult<TResult>> ExecuteAsync<TResult>(
        CommandId commandId,
        CommandRequestHash requestHash,
        ActorAccountId actorAccountId,
        Guid id,
        long expectedRowVersion,
        bool softDelete,
        string commandType,
        string tableName,
        string subjectKind,
        string notFoundCode,
        string alreadyDeletedCode,
        string notDeletedCode,
        string staleCode,
        string idempotencyCode,
        Func<Guid, long, bool, TResult> resultFactory,
        CancellationToken cancellationToken)
    {
        return _transactionRunner.ExecuteAsync(async ct =>
        {
            var now = _timeProvider.GetUtcNow();
            var acquisition = await AcquireAsync<TResult>(commandId, requestHash, actorAccountId, commandType, now, ct);
            if (acquisition.Kind == AcquisitionKind.Replay)
            {
                return CommandTransactionDecision<ApplicationResult<TResult>>.Rollback(
                    ApplicationResult<TResult>.Success(acquisition.ReplayResult!));
            }
            if (acquisition.Kind == AcquisitionKind.Conflict)
            {
                return Rollback<TResult>(ApplicationErrorKind.Conflict, idempotencyCode);
            }

            var state = await LockAsync(tableName, id, ct);
            if (state is null)
            {
                return Rollback<TResult>(ApplicationErrorKind.NotFound, notFoundCode);
            }
            if (state.RowVersion != expectedRowVersion)
            {
                return Rollback<TResult>(ApplicationErrorKind.Conflict, staleCode);
            }
            if (softDelete && state.DeletedAt is not null)
            {
                return Rollback<TResult>(ApplicationErrorKind.Conflict, alreadyDeletedCode);
            }
            if (!softDelete && state.DeletedAt is null)
            {
                return Rollback<TResult>(ApplicationErrorKind.Conflict, notDeletedCode);
            }

            var nextRowVersion = await MutateAsync(tableName, id, expectedRowVersion, actorAccountId, now, softDelete, ct);
            if (nextRowVersion is null)
            {
                return Rollback<TResult>(ApplicationErrorKind.Conflict, staleCode);
            }

            var result = resultFactory(id, nextRowVersion.Value, softDelete);
            await AppendAuditAsync(commandId, actorAccountId, commandType, subjectKind, id, state.RowVersion, nextRowVersion.Value, softDelete, now, ct);
            await MarkSucceededAsync(commandId, result!, now, ct);
            return CommandTransactionDecision<ApplicationResult<TResult>>.Commit(ApplicationResult<TResult>.Success(result));
        }, cancellationToken);
    }

    private async ValueTask<RowState?> LockAsync(string tableName, Guid id, CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand($"SELECT row_version, deleted_at FROM {tableName} WHERE id = @id FOR UPDATE;");
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new RowState(reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1))
            : null;
    }

    private async ValueTask<long?> MutateAsync(
        string tableName,
        Guid id,
        long expectedRowVersion,
        ActorAccountId actorAccountId,
        DateTimeOffset occurredAt,
        bool softDelete,
        CancellationToken cancellationToken)
    {
        var sql = softDelete
            ? $"""
              UPDATE {tableName}
              SET deleted_at = @occurred_at, deleted_by_account_id = @actor_account_id, row_version = row_version + 1
              WHERE id = @id AND row_version = @expected_row_version AND deleted_at IS NULL
              RETURNING row_version;
              """
            : $"""
              UPDATE {tableName}
              SET deleted_at = NULL, deleted_by_account_id = NULL, row_version = row_version + 1
              WHERE id = @id AND row_version = @expected_row_version AND deleted_at IS NOT NULL
              RETURNING row_version;
              """;

        await using var command = CreateSqlCommand(sql);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("expected_row_version", expectedRowVersion);
        if (softDelete)
        {
            command.Parameters.AddWithValue("occurred_at", occurredAt);
            command.Parameters.AddWithValue("actor_account_id", actorAccountId.Value);
        }
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null ? null : Convert.ToInt64(value);
    }

    private async ValueTask<Acquisition<TResult>> AcquireAsync<TResult>(
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
                (command_id, command_type, request_hash, status, result_payload, actor_account_id, started_at, executed_at)
            VALUES
                (@command_id, @command_type, @request_hash, 'IN_PROGRESS', NULL, @actor_account_id, @started_at, NULL)
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
                return Acquisition<TResult>.Acquired();
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
            throw new InvalidOperationException("Product lifecycle CommandExecution conflict could not be loaded.");
        }
        if (!string.Equals(reader.GetString(0), commandType, StringComparison.Ordinal)
            || reader.GetGuid(3) != actorAccountId.Value
            || !requestHash.Bytes.Span.SequenceEqual(reader.GetFieldValue<byte[]>(1)))
        {
            return Acquisition<TResult>.Conflict();
        }
        if (!string.Equals(reader.GetString(2), "SUCCEEDED", StringComparison.Ordinal) || reader.IsDBNull(4))
        {
            throw new InvalidOperationException($"A committed {commandType} CommandExecution with the same identity is not replayable.");
        }

        var replay = JsonSerializer.Deserialize<TResult>(reader.GetString(4), StoredJsonOptions)
            ?? throw new InvalidOperationException($"Stored {commandType} result could not be deserialized.");
        return Acquisition<TResult>.Replay(replay);
    }

    private async ValueTask AppendAuditAsync(
        CommandId commandId,
        ActorAccountId actorAccountId,
        string commandType,
        string subjectKind,
        Guid id,
        long beforeRowVersion,
        long afterRowVersion,
        bool softDelete,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var auditEventId = Guid.CreateVersion7();
        await using (var audit = CreateSqlCommand(
            """
            INSERT INTO audit.audit_events
                (id, command_id, command_type, event_kind, actor_account_id, occurred_at, reason_text)
            VALUES
                (@id, @command_id, @command_type, 'DATA_LIFECYCLE', @actor_account_id, @occurred_at, NULL);
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
                (audit_event_id, sequence, subject_kind, subject_key, change_kind, before_row_version, after_row_version, change_summary)
            VALUES
                (@audit_event_id, 1, @subject_kind, CAST(@subject_key AS jsonb), @change_kind, @before_row_version, @after_row_version, NULL);
            """);
        subject.Parameters.AddWithValue("audit_event_id", auditEventId);
        subject.Parameters.AddWithValue("subject_kind", subjectKind);
        subject.Parameters.AddWithValue("subject_key", JsonSerializer.Serialize(new { id }, StoredJsonOptions));
        subject.Parameters.AddWithValue("change_kind", softDelete ? "SOFT_DELETE" : "RESTORE");
        subject.Parameters.AddWithValue("before_row_version", beforeRowVersion);
        subject.Parameters.AddWithValue("after_row_version", afterRowVersion);
        await subject.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask MarkSucceededAsync(CommandId commandId, object result, DateTimeOffset executedAt, CancellationToken cancellationToken)
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
            throw new InvalidOperationException("Product lifecycle CommandExecution could not transition to SUCCEEDED.");
        }
    }

    private NpgsqlCommand CreateSqlCommand(string sql)
    {
        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Product lifecycle SQL requires an active command transaction.");
        return new NpgsqlCommand(sql, (NpgsqlConnection)_dbContext.Database.GetDbConnection(), (NpgsqlTransaction)transaction.GetDbTransaction());
    }

    private static CommandTransactionDecision<ApplicationResult<TResult>> Rollback<TResult>(ApplicationErrorKind kind, string code) =>
        CommandTransactionDecision<ApplicationResult<TResult>>.Rollback(
            ApplicationResult<TResult>.Failure(ApplicationError.Create(kind, code)));

    private sealed record RowState(long RowVersion, DateTimeOffset? DeletedAt);

    private enum AcquisitionKind { Acquired, Replay, Conflict }

    private sealed record Acquisition<TResult>(AcquisitionKind Kind, TResult? ReplayResult)
    {
        public static Acquisition<TResult> Acquired() => new(AcquisitionKind.Acquired, default);
        public static Acquisition<TResult> Replay(TResult result) => new(AcquisitionKind.Replay, result);
        public static Acquisition<TResult> Conflict() => new(AcquisitionKind.Conflict, default);
    }
}
