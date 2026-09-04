using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.SalesHandling;
using YowThi.Erp.Domain.Labor;
using YowThi.Erp.Domain.Sales;
using YowThi.Erp.Domain.SalesHandling;
using YowThi.Erp.Infrastructure.Persistence.Labor;

namespace YowThi.Erp.Infrastructure.Persistence.SalesHandling;

internal sealed class PostgreSqlRecordSalesPackagingWorkExecutor : IRecordSalesPackagingWorkExecutor
{
    private const string CommandType = "RecordSalesPackagingWork";
    private const string OutboxMessageType = "sales-handling.work-record.recorded";

    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlRecordSalesPackagingWorkExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public async ValueTask<ApplicationResult<RecordSalesPackagingWorkResult>> ExecuteAsync(
        RecordSalesPackagingWorkExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);

        var validationError = RecordSalesPackagingWorkValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ApplicationResult<RecordSalesPackagingWorkResult>.Failure(validationError);
        }

        return await _transactionRunner.ExecuteAsync(
            async operationCancellationToken =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await AcquireCommandAsync(execution, now, operationCancellationToken);

                if (acquisition.Kind == CommandAcquisitionKind.Replay)
                {
                    return CommandTransactionDecision<ApplicationResult<RecordSalesPackagingWorkResult>>.Rollback(
                        ApplicationResult<RecordSalesPackagingWorkResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == CommandAcquisitionKind.Conflict)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        SalesHandlingApplicationErrorCodes.IdempotencyKeyReused);
                }

                var command = execution.Command;
                var sale = await _dbContext.Set<Sale>()
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        x => x.Id == command.SalesId && x.DeletedAt == null,
                        operationCancellationToken);
                if (sale is null)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.NotFound,
                        SalesHandlingApplicationErrorCodes.SalesNotFound);
                }

                // HANDLING-001 confirmed 2026-09-03: both current Sales lifecycle states are valid.
                if (sale.Status is not SalesStatus.DRAFT and not SalesStatus.CONFIRMED)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        SalesHandlingApplicationErrorCodes.SalesStateNotAllowed);
                }

                var employeeState = await EmployeeLaborCommandLock.AcquireAsync(
                    _dbContext,
                    command.EmployeeId,
                    operationCancellationToken);
                if (!employeeState.Exists)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.NotFound,
                        SalesHandlingApplicationErrorCodes.EmployeeNotFound);
                }

                if (!employeeState.Active || employeeState.Deleted)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        SalesHandlingApplicationErrorCodes.EmployeeInactive);
                }

                // LABOR-001 safe v0.1 control: normal late work is blocked once the day wage exists.
                if (await _dbContext.Set<EmployeeDailyWage>()
                    .AsNoTracking()
                    .AnyAsync(
                        x => x.WorkDate == command.WorkDate && x.EmployeeId == command.EmployeeId,
                        operationCancellationToken))
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        SalesHandlingApplicationErrorCodes.DailyWageAlreadyConfirmed);
                }

                var item = await _dbContext.Set<SalesPackagingItem>()
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        x => x.Id == command.SalesPackagingItemId,
                        operationCancellationToken);
                if (item is null)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.NotFound,
                        SalesHandlingApplicationErrorCodes.ItemNotFound);
                }

                if (!item.Active || item.DeletedAt is not null)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        SalesHandlingApplicationErrorCodes.ItemInactive);
                }

                var workRecord = SalesPackagingWorkRecord.Create(
                    Guid.CreateVersion7(),
                    command.SalesId,
                    command.WorkDate,
                    command.EmployeeId,
                    command.SalesPackagingItemId,
                    command.ConfirmedWageThb,
                    now,
                    execution.ActorAccountId.Value);

                _dbContext.Add(workRecord);
                await _dbContext.SaveChangesAsync(operationCancellationToken);

                var result = new RecordSalesPackagingWorkResult(workRecord.Id, workRecord.RowVersion);
                var storedResultJson = JsonSerializer.Serialize(result, StoredJsonOptions);

                await AppendAuditAsync(
                    execution,
                    workRecord.Id,
                    workRecord.RowVersion,
                    now,
                    operationCancellationToken);
                await EnqueueOutboxAsync(
                    execution,
                    storedResultJson,
                    now,
                    operationCancellationToken);
                await MarkCommandSucceededAsync(
                    execution.CommandId.Value,
                    storedResultJson,
                    now,
                    operationCancellationToken);

                return CommandTransactionDecision<ApplicationResult<RecordSalesPackagingWorkResult>>.Commit(
                    ApplicationResult<RecordSalesPackagingWorkResult>.Success(result));
            },
            cancellationToken);
    }

    private async ValueTask<CommandAcquisition> AcquireCommandAsync(
        RecordSalesPackagingWorkExecution execution,
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
            throw new InvalidOperationException(
                "CommandExecution conflict was observed but the stored command could not be loaded.");
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
                "A committed RecordSalesPackagingWork CommandExecution with the same identity is not replayable.");
        }

        var replay = JsonSerializer.Deserialize<RecordSalesPackagingWorkResult>(
                reader.GetString(4),
                StoredJsonOptions)
            ?? throw new InvalidOperationException(
                "Stored RecordSalesPackagingWork result payload could not be deserialized.");
        return CommandAcquisition.Replay(replay);
    }

    private async ValueTask AppendAuditAsync(
        RecordSalesPackagingWorkExecution execution,
        Guid workRecordId,
        long rowVersion,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var auditEventId = Guid.CreateVersion7();
        var subjectKeyJson = JsonSerializer.Serialize(new { id = workRecordId }, StoredJsonOptions);

        await using (var auditEvent = CreateSqlCommand(
            """
            INSERT INTO audit.audit_events
                (id, command_id, command_type, event_kind, actor_account_id, occurred_at, reason_text)
            VALUES
                (@id, @command_id, @command_type, 'BUSINESS_COMMAND', @actor_account_id, @occurred_at, NULL);
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
                (@audit_event_id, 1, 'sales-handling.work-record', CAST(@subject_key AS jsonb), 'CREATE',
                 NULL, @after_row_version, NULL);
            """);
        subject.Parameters.AddWithValue("audit_event_id", auditEventId);
        subject.Parameters.AddWithValue("subject_key", subjectKeyJson);
        subject.Parameters.AddWithValue("after_row_version", rowVersion);
        await subject.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask EnqueueOutboxAsync(
        RecordSalesPackagingWorkExecution execution,
        string resultJson,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
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
        command.Parameters.AddWithValue("message_type", OutboxMessageType);
        command.Parameters.AddWithValue("payload", resultJson);
        command.Parameters.AddWithValue("command_id", execution.CommandId.Value);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
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
            throw new InvalidOperationException(
                "CommandExecution could not be transitioned from IN_PROGRESS to SUCCEEDED.");
        }
    }

    private NpgsqlCommand CreateSqlCommand(string sql)
    {
        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "RecordSalesPackagingWork SQL requires the active command transaction.");

        return new NpgsqlCommand(
            sql,
            (NpgsqlConnection)_dbContext.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction());
    }

    private static CommandTransactionDecision<ApplicationResult<RecordSalesPackagingWorkResult>> RollbackFailure(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<RecordSalesPackagingWorkResult>>.Rollback(
            ApplicationResult<RecordSalesPackagingWorkResult>.Failure(
                ApplicationError.Create(kind, code)));

    private enum CommandAcquisitionKind
    {
        Acquired,
        Replay,
        Conflict,
    }

    private sealed record CommandAcquisition(
        CommandAcquisitionKind Kind,
        RecordSalesPackagingWorkResult? ReplayResult)
    {
        public static CommandAcquisition Acquired() => new(CommandAcquisitionKind.Acquired, null);
        public static CommandAcquisition Replay(RecordSalesPackagingWorkResult result) =>
            new(CommandAcquisitionKind.Replay, result);
        public static CommandAcquisition Conflict() => new(CommandAcquisitionKind.Conflict, null);
    }
}
