using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Labor;
using YowThi.Erp.Domain.Finance;
using YowThi.Erp.Domain.Labor;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.Processing;
using YowThi.Erp.Domain.SalesHandling;

namespace YowThi.Erp.Infrastructure.Persistence.Labor;

internal sealed class PostgreSqlConfirmEmployeeDailyWageExecutor : IConfirmEmployeeDailyWageExecutor
{
    private const string CommandType = "ConfirmEmployeeDailyWage";
    private const string OutboxMessageType = "labor.employee-daily-wage.confirmed";

    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlConfirmEmployeeDailyWageExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public async ValueTask<ApplicationResult<ConfirmEmployeeDailyWageResult>> ExecuteAsync(
        ConfirmEmployeeDailyWageExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);

        var validationError = ConfirmEmployeeDailyWageValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ApplicationResult<ConfirmEmployeeDailyWageResult>.Failure(validationError);
        }

        return await _transactionRunner.ExecuteAsync(
            async operationCancellationToken =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await AcquireCommandAsync(execution, now, operationCancellationToken);

                if (acquisition.Kind == CommandAcquisitionKind.Replay)
                {
                    return CommandTransactionDecision<ApplicationResult<ConfirmEmployeeDailyWageResult>>.Rollback(
                        ApplicationResult<ConfirmEmployeeDailyWageResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == CommandAcquisitionKind.Conflict)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, LaborApplicationErrorCodes.IdempotencyKeyReused);
                }

                var command = execution.Command;
                var employeeExists = await _dbContext.Set<Employee>()
                    .AsNoTracking()
                    .AnyAsync(x => x.Id == command.EmployeeId, operationCancellationToken);
                if (!employeeExists)
                {
                    return RollbackFailure(ApplicationErrorKind.NotFound, LaborApplicationErrorCodes.EmployeeNotFound);
                }

                if (await _dbContext.Set<EmployeeDailyWage>()
                    .AsNoTracking()
                    .AnyAsync(
                        x => x.WorkDate == command.WorkDate && x.EmployeeId == command.EmployeeId,
                        operationCancellationToken))
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, LaborApplicationErrorCodes.DailyWageAlreadyConfirmed);
                }

                var processingSources = await ReadProcessingSourcesAsync(
                    command.WorkDate,
                    command.EmployeeId,
                    operationCancellationToken);
                var processingGroups = BuildProcessingGroups(processingSources);
                if (processingGroups.Error is not null)
                {
                    return CommandTransactionDecision<ApplicationResult<ConfirmEmployeeDailyWageResult>>.Rollback(
                        ApplicationResult<ConfirmEmployeeDailyWageResult>.Failure(processingGroups.Error));
                }

                var overrideByTarget = command.ProcessingWageRateOverrides.ToDictionary(
                    item => new WageTarget(item.ProcessingModuleOutputId, item.ConfiguredWageRateSnapshot));
                if (overrideByTarget.Keys.Any(target => !processingGroups.Groups!.Any(group => group.Target == target)))
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Validation,
                        LaborApplicationErrorCodes.ProcessingRateOverrideTargetNotFound);
                }

                var packagingRecords = await _dbContext.Set<SalesPackagingWorkRecord>()
                    .AsNoTracking()
                    .Where(x => x.WorkDate == command.WorkDate
                        && x.EmployeeId == command.EmployeeId
                        && x.DeletedAt == null)
                    .Where(x => !_dbContext.Set<SalesPackagingWageComponent>()
                        .Any(component => component.SalesPackagingWorkRecordId == x.Id))
                    .OrderBy(x => x.Id)
                    .ToListAsync(operationCancellationToken);

                var dailyWageId = Guid.CreateVersion7();
                long processingTotalThb = 0;
                var processingComponents = new List<ProcessingComponentPlan>();

                foreach (var group in processingGroups.Groups!)
                {
                    overrideByTarget.TryGetValue(group.Target, out var rateOverride);
                    var appliedRate = rateOverride?.AppliedWageRate ?? group.Target.ConfiguredWageRateSnapshot;
                    var overridden = rateOverride is not null && appliedRate != group.Target.ConfiguredWageRateSnapshot;
                    var componentId = Guid.CreateVersion7();
                    ProcessingWageComponent component;

                    try
                    {
                        component = ProcessingWageComponent.Create(
                            componentId,
                            dailyWageId,
                            group.Target.ProcessingModuleOutputId,
                            group.Target.ConfiguredWageRateSnapshot,
                            appliedRate,
                            overridden,
                            group.AggregatedQuantity);
                        processingTotalThb = checked(processingTotalThb + component.AmountThb);
                    }
                    catch (ArgumentException)
                    {
                        return RollbackFailure(ApplicationErrorKind.Validation, LaborApplicationErrorCodes.WageAmountInvalid);
                    }
                    catch (OverflowException)
                    {
                        return RollbackFailure(ApplicationErrorKind.Validation, LaborApplicationErrorCodes.WageAmountInvalid);
                    }

                    processingComponents.Add(new ProcessingComponentPlan(component, group.Sources));
                }

                long packagingTotalThb = 0;
                try
                {
                    foreach (var record in packagingRecords)
                    {
                        packagingTotalThb = checked(packagingTotalThb + record.ConfirmedWageThb);
                    }
                }
                catch (OverflowException)
                {
                    return RollbackFailure(ApplicationErrorKind.Validation, LaborApplicationErrorCodes.WageAmountInvalid);
                }

                EmployeeDailyWage dailyWage;
                Payable payable;
                PayableObligationItem obligation;
                PayableOutstandingPosition outstanding;
                var payableId = Guid.CreateVersion7();

                try
                {
                    dailyWage = EmployeeDailyWage.Create(
                        dailyWageId,
                        command.WorkDate,
                        command.EmployeeId,
                        processingTotalThb,
                        packagingTotalThb,
                        now,
                        execution.ActorAccountId.Value);
                    payable = Payable.CreateEmployeeDailyWage(payableId, dailyWageId, now);
                    obligation = PayableObligationItem.CreateEmployeeDailyWage(
                        Guid.CreateVersion7(),
                        payableId,
                        dailyWageId,
                        dailyWage.TotalWageThb,
                        now);
                    outstanding = PayableOutstandingPosition.Create(payableId, dailyWage.TotalWageThb, now);
                }
                catch (ArgumentException)
                {
                    return RollbackFailure(ApplicationErrorKind.Validation, LaborApplicationErrorCodes.WageAmountInvalid);
                }
                catch (OverflowException)
                {
                    return RollbackFailure(ApplicationErrorKind.Validation, LaborApplicationErrorCodes.WageAmountInvalid);
                }

                _dbContext.AddRange(dailyWage, payable, obligation, outstanding);

                foreach (var plan in processingComponents)
                {
                    _dbContext.Add(plan.Component);
                    foreach (var source in plan.Sources)
                    {
                        _dbContext.Add(ProcessingWageComponentSource.Create(
                            plan.Component.Id,
                            source.ProcessingExecutionOutputId,
                            source.Quantity));
                    }
                }

                foreach (var record in packagingRecords)
                {
                    _dbContext.Add(SalesPackagingWageComponent.Create(
                        Guid.CreateVersion7(),
                        dailyWageId,
                        record.Id,
                        record.ConfirmedWageThb));
                }

                try
                {
                    await _dbContext.SaveChangesAsync(operationCancellationToken);
                }
                catch (DbUpdateException exception)
                    when (exception.InnerException is PostgresException postgres
                          && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
                {
                    var code = string.Equals(
                            postgres.ConstraintName,
                            "ux_employee_daily_wages_work_date_employee",
                            StringComparison.Ordinal)
                        ? LaborApplicationErrorCodes.DailyWageAlreadyConfirmed
                        : LaborApplicationErrorCodes.ConcurrentChange;
                    return RollbackFailure(ApplicationErrorKind.Conflict, code);
                }

                var result = new ConfirmEmployeeDailyWageResult(
                    dailyWage.Id,
                    dailyWage.RowVersion,
                    payable.Id,
                    dailyWage.ProcessingWageTotalThb,
                    dailyWage.SalesPackagingWageTotalThb,
                    dailyWage.TotalWageThb);
                var storedResultJson = JsonSerializer.Serialize(result, StoredJsonOptions);

                await AppendAuditAsync(execution, dailyWage.Id, dailyWage.RowVersion, now, operationCancellationToken);
                await EnqueueOutboxAsync(execution, storedResultJson, now, operationCancellationToken);
                await MarkCommandSucceededAsync(
                    execution.CommandId.Value,
                    storedResultJson,
                    now,
                    operationCancellationToken);

                return CommandTransactionDecision<ApplicationResult<ConfirmEmployeeDailyWageResult>>.Commit(
                    ApplicationResult<ConfirmEmployeeDailyWageResult>.Success(result));
            },
            cancellationToken);
    }

    private async ValueTask<IReadOnlyList<ProcessingSource>> ReadProcessingSourcesAsync(
        DateOnly workDate,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        return await (
            from output in _dbContext.Set<ProcessingExecutionOutput>().AsNoTracking()
            join processingExecution in _dbContext.Set<ProcessingExecution>().AsNoTracking()
                on output.ProcessingExecutionId equals processingExecution.Id
            where processingExecution.WorkDate == workDate
                && processingExecution.EmployeeId == employeeId
                && processingExecution.DeletedAt == null
                && !_dbContext.Set<ProcessingWageComponentSource>()
                    .Any(source => source.ProcessingExecutionOutputId == output.Id)
            orderby output.ProcessingModuleOutputId, output.Id
            select new ProcessingSource(
                output.Id,
                output.ProcessingModuleOutputId,
                output.ConfiguredWageRateSnapshot,
                output.DerivedNetQuantity ?? output.CompletedQuantity ?? 0m))
            .ToListAsync(cancellationToken);
    }

    private static ProcessingGroupResult BuildProcessingGroups(IReadOnlyList<ProcessingSource> sources)
    {
        try
        {
            var groups = sources
                .GroupBy(source => new WageTarget(
                    source.ProcessingModuleOutputId,
                    source.ConfiguredWageRateSnapshot))
                .Select(group => new ProcessingGroup(
                    group.Key,
                    group.Sum(source => source.Quantity),
                    group.ToArray()))
                .OrderBy(group => group.Target.ProcessingModuleOutputId)
                .ThenBy(group => group.Target.ConfiguredWageRateSnapshot)
                .ToArray();
            return ProcessingGroupResult.Resolved(groups);
        }
        catch (OverflowException)
        {
            return ProcessingGroupResult.Failed(
                ApplicationError.Create(ApplicationErrorKind.Validation, LaborApplicationErrorCodes.WageAmountInvalid));
        }
    }

    private async ValueTask<CommandAcquisition> AcquireCommandAsync(
        ConfirmEmployeeDailyWageExecution execution,
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
            throw new InvalidOperationException("CommandExecution conflict was observed but the stored command could not be loaded.");
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
                "A committed ConfirmEmployeeDailyWage CommandExecution with the same identity is not replayable.");
        }

        var replay = JsonSerializer.Deserialize<ConfirmEmployeeDailyWageResult>(reader.GetString(4), StoredJsonOptions)
            ?? throw new InvalidOperationException("Stored ConfirmEmployeeDailyWage result payload could not be deserialized.");
        return CommandAcquisition.Replay(replay);
    }

    private async ValueTask AppendAuditAsync(
        ConfirmEmployeeDailyWageExecution execution,
        Guid employeeDailyWageId,
        long rowVersion,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var auditEventId = Guid.CreateVersion7();
        var subjectKeyJson = JsonSerializer.Serialize(new { id = employeeDailyWageId }, StoredJsonOptions);

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
                (@audit_event_id, 1, 'labor.employee-daily-wage', CAST(@subject_key AS jsonb), 'CREATE',
                 NULL, @after_row_version, NULL);
            """);
        subject.Parameters.AddWithValue("audit_event_id", auditEventId);
        subject.Parameters.AddWithValue("subject_key", subjectKeyJson);
        subject.Parameters.AddWithValue("after_row_version", rowVersion);
        await subject.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask EnqueueOutboxAsync(
        ConfirmEmployeeDailyWageExecution execution,
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
            throw new InvalidOperationException("CommandExecution could not be transitioned from IN_PROGRESS to SUCCEEDED.");
        }
    }

    private NpgsqlCommand CreateSqlCommand(string sql)
    {
        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("ConfirmEmployeeDailyWage SQL requires the active command transaction.");

        return new NpgsqlCommand(
            sql,
            (NpgsqlConnection)_dbContext.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction());
    }

    private static CommandTransactionDecision<ApplicationResult<ConfirmEmployeeDailyWageResult>> RollbackFailure(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<ConfirmEmployeeDailyWageResult>>.Rollback(
            ApplicationResult<ConfirmEmployeeDailyWageResult>.Failure(
                ApplicationError.Create(kind, code)));

    private sealed record ProcessingSource(
        Guid ProcessingExecutionOutputId,
        Guid ProcessingModuleOutputId,
        decimal ConfiguredWageRateSnapshot,
        decimal Quantity);

    private sealed record WageTarget(
        Guid ProcessingModuleOutputId,
        decimal ConfiguredWageRateSnapshot);

    private sealed record ProcessingGroup(
        WageTarget Target,
        decimal AggregatedQuantity,
        IReadOnlyList<ProcessingSource> Sources);

    private sealed record ProcessingComponentPlan(
        ProcessingWageComponent Component,
        IReadOnlyList<ProcessingSource> Sources);

    private sealed record ProcessingGroupResult(
        IReadOnlyList<ProcessingGroup>? Groups,
        ApplicationError? Error)
    {
        public static ProcessingGroupResult Resolved(IReadOnlyList<ProcessingGroup> groups) => new(groups, null);
        public static ProcessingGroupResult Failed(ApplicationError error) => new(null, error);
    }

    private enum CommandAcquisitionKind
    {
        Acquired,
        Replay,
        Conflict,
    }

    private sealed record CommandAcquisition(
        CommandAcquisitionKind Kind,
        ConfirmEmployeeDailyWageResult? ReplayResult)
    {
        public static CommandAcquisition Acquired() => new(CommandAcquisitionKind.Acquired, null);
        public static CommandAcquisition Replay(ConfirmEmployeeDailyWageResult result) => new(CommandAcquisitionKind.Replay, result);
        public static CommandAcquisition Conflict() => new(CommandAcquisitionKind.Conflict, null);
    }
}
