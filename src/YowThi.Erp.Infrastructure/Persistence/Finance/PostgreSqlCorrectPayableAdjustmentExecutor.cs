using System.Text.Json;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Finance;
using YowThi.Erp.Domain.Finance;

namespace YowThi.Erp.Infrastructure.Persistence.Finance;

internal sealed class PostgreSqlCorrectPayableAdjustmentExecutor : ICorrectPayableAdjustmentExecutor
{
    private const string CommandType = "CorrectPayableAdjustment";
    private const string OutboxMessageType = "finance.payable-adjustment-corrected";
    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlCorrectPayableAdjustmentExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public async ValueTask<ApplicationResult<CorrectPayableAdjustmentResult>> ExecuteAsync(
        CorrectPayableAdjustmentExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);

        var validationError = CorrectPayableAdjustmentValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ApplicationResult<CorrectPayableAdjustmentResult>.Failure(validationError);
        }

        return await _transactionRunner.ExecuteAsync(
            async ct =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await FinanceCommandPersistence.AcquireAsync<CorrectPayableAdjustmentResult>(
                    _dbContext,
                    CommandType,
                    execution.CommandId,
                    execution.RequestHash,
                    execution.ActorAccountId,
                    now,
                    StoredJsonOptions,
                    ct);

                if (acquisition.Kind == FinanceCommandAcquisitionKind.Replay)
                {
                    return CommandTransactionDecision<ApplicationResult<CorrectPayableAdjustmentResult>>.Rollback(
                        ApplicationResult<CorrectPayableAdjustmentResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == FinanceCommandAcquisitionKind.Conflict)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        FinanceApplicationErrorCodes.IdempotencyKeyReused);
                }

                var command = execution.Command;
                var adjustment = await LockAdjustmentAsync(command.PayableAdjustmentId, ct);
                if (adjustment is null || adjustment.PayableId != command.PayableId)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.NotFound,
                        FinanceApplicationErrorCodes.PayableAdjustmentNotFound);
                }

                if (adjustment.AdjustmentType != PayableAdjustmentType.SUPPLIER_QUALITY_WEIGHT_DEDUCTION)
                {
                    throw new InvalidOperationException("Stored Payable Adjustment type is outside the v0.1 supported vocabulary.");
                }

                var snapshot = await LockOutstandingAsync(command.PayableId, ct);
                if (snapshot.RowVersion != command.ExpectedOutstandingVersion)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        FinanceApplicationErrorCodes.OutstandingChanged);
                }

                if (adjustment.AmountDeltaThb == command.CorrectedAmountDeltaThb
                    && string.Equals(adjustment.ReasonText, command.CorrectedReasonText, StringComparison.Ordinal))
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Validation,
                        FinanceApplicationErrorCodes.AdjustmentCorrectionNoChange);
                }

                var difference = command.CorrectedAmountDeltaThb - adjustment.AmountDeltaThb;
                long newOutstandingThb;
                long newAdjustmentTotalThb;
                try
                {
                    newOutstandingThb = checked(snapshot.OutstandingThb + difference);
                    newAdjustmentTotalThb = checked(snapshot.AdjustmentTotalThb + difference);
                }
                catch (OverflowException)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Validation,
                        FinanceApplicationErrorCodes.InvalidInput);
                }

                if (newOutstandingThb < 0)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        FinanceApplicationErrorCodes.AdjustmentCorrectionCausesNegativeOutstanding);
                }

                if (newAdjustmentTotalThb > 0)
                {
                    throw new InvalidOperationException("Payable Adjustment total became positive despite deduction-only adjustment vocabulary.");
                }

                await using (var updateOutstanding = FinanceCommandPersistence.CreateSqlCommand(
                    _dbContext,
                    """
                    UPDATE finance.payable_outstanding_positions
                    SET adjustment_total_thb = @adjustment_total_thb,
                        outstanding_thb = @outstanding_thb,
                        row_version = row_version + 1,
                        updated_at = @updated_at
                    WHERE payable_id = @payable_id
                      AND row_version = @expected_row_version;
                    """))
                {
                    updateOutstanding.Parameters.AddWithValue("adjustment_total_thb", newAdjustmentTotalThb);
                    updateOutstanding.Parameters.AddWithValue("outstanding_thb", newOutstandingThb);
                    updateOutstanding.Parameters.AddWithValue("updated_at", now);
                    updateOutstanding.Parameters.AddWithValue("payable_id", command.PayableId);
                    updateOutstanding.Parameters.AddWithValue("expected_row_version", command.ExpectedOutstandingVersion);

                    if (await updateOutstanding.ExecuteNonQueryAsync(ct) != 1)
                    {
                        return RollbackFailure(
                            ApplicationErrorKind.Conflict,
                            FinanceApplicationErrorCodes.OutstandingChanged);
                    }
                }

                await using (var updateAdjustment = FinanceCommandPersistence.CreateSqlCommand(
                    _dbContext,
                    """
                    UPDATE finance.payable_adjustments
                    SET amount_delta_thb = @corrected_amount_delta_thb,
                        reason_text = @corrected_reason_text
                    WHERE id = @adjustment_id
                      AND payable_id = @payable_id
                      AND amount_delta_thb = @previous_amount_delta_thb
                      AND reason_text IS NOT DISTINCT FROM @previous_reason_text;
                    """))
                {
                    updateAdjustment.Parameters.AddWithValue("corrected_amount_delta_thb", command.CorrectedAmountDeltaThb);
                    updateAdjustment.Parameters.AddWithValue(
                        "corrected_reason_text",
                        (object?)command.CorrectedReasonText ?? DBNull.Value);
                    updateAdjustment.Parameters.AddWithValue("adjustment_id", command.PayableAdjustmentId);
                    updateAdjustment.Parameters.AddWithValue("payable_id", command.PayableId);
                    updateAdjustment.Parameters.AddWithValue("previous_amount_delta_thb", adjustment.AmountDeltaThb);
                    updateAdjustment.Parameters.AddWithValue(
                        "previous_reason_text",
                        (object?)adjustment.ReasonText ?? DBNull.Value);

                    if (await updateAdjustment.ExecuteNonQueryAsync(ct) != 1)
                    {
                        throw new InvalidOperationException("Locked Payable Adjustment changed unexpectedly during correction.");
                    }
                }

                var result = new CorrectPayableAdjustmentResult(
                    command.PayableAdjustmentId,
                    command.PayableId,
                    adjustment.AmountDeltaThb,
                    command.CorrectedAmountDeltaThb,
                    adjustment.ReasonText,
                    command.CorrectedReasonText,
                    newOutstandingThb,
                    checked(command.ExpectedOutstandingVersion + 1),
                    now);
                var resultJson = JsonSerializer.Serialize(result, StoredJsonOptions);

                await AppendCorrectionAuditAsync(execution, adjustment, now, ct);
                await FinanceCommandPersistence.EnqueueOutboxAsync(
                    _dbContext,
                    OutboxMessageType,
                    execution.CommandId,
                    resultJson,
                    now,
                    ct);
                await FinanceCommandPersistence.MarkSucceededAsync(
                    _dbContext,
                    execution.CommandId,
                    resultJson,
                    now,
                    ct);

                return CommandTransactionDecision<ApplicationResult<CorrectPayableAdjustmentResult>>.Commit(
                    ApplicationResult<CorrectPayableAdjustmentResult>.Success(result));
            },
            cancellationToken);
    }

    private async ValueTask<AdjustmentSnapshot?> LockAdjustmentAsync(
        Guid adjustmentId,
        CancellationToken cancellationToken)
    {
        await using var command = FinanceCommandPersistence.CreateSqlCommand(
            _dbContext,
            """
            SELECT payable_id, adjustment_type, amount_delta_thb, reason_text
            FROM finance.payable_adjustments
            WHERE id = @adjustment_id
            FOR UPDATE;
            """);
        command.Parameters.AddWithValue("adjustment_id", adjustmentId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AdjustmentSnapshot(
            reader.GetGuid(0),
            Enum.Parse<PayableAdjustmentType>(reader.GetString(1), ignoreCase: false),
            reader.GetInt64(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private async ValueTask<OutstandingSnapshot> LockOutstandingAsync(
        Guid payableId,
        CancellationToken cancellationToken)
    {
        await using var command = FinanceCommandPersistence.CreateSqlCommand(
            _dbContext,
            """
            SELECT outstanding_thb, adjustment_total_thb, row_version
            FROM finance.payable_outstanding_positions
            WHERE payable_id = @payable_id
            FOR UPDATE;
            """);
        command.Parameters.AddWithValue("payable_id", payableId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Payable Adjustment Payable is missing its Outstanding Position.");
        }

        return new OutstandingSnapshot(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    private async ValueTask AppendCorrectionAuditAsync(
        CorrectPayableAdjustmentExecution execution,
        AdjustmentSnapshot previous,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var correctedAuditEventId = await FindLatestAdjustmentAuditEventAsync(
            execution.Command.PayableAdjustmentId,
            cancellationToken)
            ?? throw new InvalidOperationException("Payable Adjustment has no audit event to correct.");
        var correctionAuditEventId = Guid.CreateVersion7();
        var subjectKeyJson = JsonSerializer.Serialize(
            new { id = execution.Command.PayableAdjustmentId },
            StoredJsonOptions);
        var changeSummaryJson = JsonSerializer.Serialize(
            new
            {
                amountDeltaThb = new
                {
                    before = previous.AmountDeltaThb,
                    after = execution.Command.CorrectedAmountDeltaThb,
                },
                reasonText = new
                {
                    before = previous.ReasonText,
                    after = execution.Command.CorrectedReasonText,
                },
            },
            StoredJsonOptions);

        await using (var auditEvent = FinanceCommandPersistence.CreateSqlCommand(
            _dbContext,
            """
            INSERT INTO audit.audit_events
                (id, command_id, command_type, event_kind, actor_account_id, occurred_at, reason_text)
            VALUES
                (@id, @command_id, @command_type, 'CORRECTION', @actor_account_id, @occurred_at, @reason_text);
            """))
        {
            auditEvent.Parameters.AddWithValue("id", correctionAuditEventId);
            auditEvent.Parameters.AddWithValue("command_id", execution.CommandId.Value);
            auditEvent.Parameters.AddWithValue("command_type", CommandType);
            auditEvent.Parameters.AddWithValue("actor_account_id", execution.ActorAccountId.Value);
            auditEvent.Parameters.AddWithValue("occurred_at", occurredAt);
            auditEvent.Parameters.AddWithValue(
                "reason_text",
                (object?)execution.Command.CorrectionReasonText ?? DBNull.Value);
            await auditEvent.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var subject = FinanceCommandPersistence.CreateSqlCommand(
            _dbContext,
            """
            INSERT INTO audit.audit_event_subjects
                (audit_event_id, sequence, subject_kind, subject_key, change_kind,
                 before_row_version, after_row_version, change_summary)
            VALUES
                (@audit_event_id, 1, 'finance.payable-adjustment', CAST(@subject_key AS jsonb), 'UPDATE',
                 NULL, NULL, CAST(@change_summary AS jsonb));
            """))
        {
            subject.Parameters.AddWithValue("audit_event_id", correctionAuditEventId);
            subject.Parameters.AddWithValue("subject_key", subjectKeyJson);
            subject.Parameters.AddWithValue("change_summary", changeSummaryJson);
            await subject.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var link = FinanceCommandPersistence.CreateSqlCommand(
            _dbContext,
            """
            INSERT INTO audit.correction_links
                (correction_audit_event_id, corrected_audit_event_id, correction_mode)
            VALUES
                (@correction_id, @corrected_id, 'DIRECT_AMENDMENT');
            """);
        link.Parameters.AddWithValue("correction_id", correctionAuditEventId);
        link.Parameters.AddWithValue("corrected_id", correctedAuditEventId);
        await link.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask<Guid?> FindLatestAdjustmentAuditEventAsync(
        Guid adjustmentId,
        CancellationToken cancellationToken)
    {
        await using var command = FinanceCommandPersistence.CreateSqlCommand(
            _dbContext,
            """
            SELECT e.id
            FROM audit.audit_events e
            JOIN audit.audit_event_subjects s ON s.audit_event_id = e.id
            WHERE e.command_type IN ('AddPayableAdjustment', 'CorrectPayableAdjustment')
              AND s.subject_kind = 'finance.payable-adjustment'
              AND s.subject_key ->> 'id' = @adjustment_id
            ORDER BY e.occurred_at DESC, e.id DESC
            LIMIT 1;
            """);
        command.Parameters.AddWithValue("adjustment_id", adjustmentId.ToString());
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null ? null : (Guid)result;
    }

    private static CommandTransactionDecision<ApplicationResult<CorrectPayableAdjustmentResult>> RollbackFailure(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<CorrectPayableAdjustmentResult>>.Rollback(
            ApplicationResult<CorrectPayableAdjustmentResult>.Failure(ApplicationError.Create(kind, code)));

    private sealed record AdjustmentSnapshot(
        Guid PayableId,
        PayableAdjustmentType AdjustmentType,
        long AmountDeltaThb,
        string? ReasonText);

    private sealed record OutstandingSnapshot(
        long OutstandingThb,
        long AdjustmentTotalThb,
        long RowVersion);
}
