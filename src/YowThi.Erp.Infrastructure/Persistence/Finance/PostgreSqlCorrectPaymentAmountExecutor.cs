using System.Text.Json;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Finance;

namespace YowThi.Erp.Infrastructure.Persistence.Finance;

internal sealed class PostgreSqlCorrectPaymentAmountExecutor : ICorrectPaymentAmountExecutor
{
    private const string CommandType = "CorrectPaymentAmount";
    private const string OutboxMessageType = "finance.payment-corrected";
    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlCorrectPaymentAmountExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public async ValueTask<ApplicationResult<CorrectPaymentAmountResult>> ExecuteAsync(
        CorrectPaymentAmountExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);

        var validationError = CorrectPaymentAmountValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ApplicationResult<CorrectPaymentAmountResult>.Failure(validationError);
        }

        return await _transactionRunner.ExecuteAsync(
            async ct =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await FinanceCommandPersistence.AcquireAsync<CorrectPaymentAmountResult>(
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
                    return CommandTransactionDecision<ApplicationResult<CorrectPaymentAmountResult>>.Rollback(
                        ApplicationResult<CorrectPaymentAmountResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == FinanceCommandAcquisitionKind.Conflict)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        FinanceApplicationErrorCodes.IdempotencyKeyReused);
                }

                var command = execution.Command;
                var payment = await LockPaymentAsync(command.PaymentId, ct);
                if (payment is null || payment.PayableId != command.PayableId)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.NotFound,
                        FinanceApplicationErrorCodes.PaymentNotFound);
                }

                var snapshot = await LockOutstandingAsync(command.PayableId, ct);
                if (snapshot.RowVersion != command.ExpectedOutstandingVersion)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        FinanceApplicationErrorCodes.OutstandingChanged);
                }

                if (payment.AmountThb == command.CorrectedAmountThb)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Validation,
                        FinanceApplicationErrorCodes.PaymentCorrectionNoChange);
                }

                var delta = command.CorrectedAmountThb - payment.AmountThb;
                long newOutstandingThb;
                long newSettlementTotalThb;
                try
                {
                    newOutstandingThb = checked(snapshot.OutstandingThb - delta);
                    newSettlementTotalThb = checked(snapshot.SettlementTotalThb + delta);
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
                        FinanceApplicationErrorCodes.PaymentCorrectionExceedsOutstanding);
                }

                if (newSettlementTotalThb < 0)
                {
                    throw new InvalidOperationException("Payment correction would make settlement total negative.");
                }

                await using (var updateOutstanding = FinanceCommandPersistence.CreateSqlCommand(
                    _dbContext,
                    """
                    UPDATE finance.payable_outstanding_positions
                    SET settlement_total_thb = @settlement_total_thb,
                        outstanding_thb = @outstanding_thb,
                        row_version = row_version + 1,
                        updated_at = @updated_at
                    WHERE payable_id = @payable_id
                      AND row_version = @expected_row_version;
                    """))
                {
                    updateOutstanding.Parameters.AddWithValue("settlement_total_thb", newSettlementTotalThb);
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

                await using (var updatePayment = FinanceCommandPersistence.CreateSqlCommand(
                    _dbContext,
                    """
                    UPDATE finance.payments
                    SET amount_thb = @corrected_amount_thb
                    WHERE id = @payment_id
                      AND payable_id = @payable_id
                      AND amount_thb = @previous_amount_thb;
                    """))
                {
                    updatePayment.Parameters.AddWithValue("corrected_amount_thb", command.CorrectedAmountThb);
                    updatePayment.Parameters.AddWithValue("payment_id", command.PaymentId);
                    updatePayment.Parameters.AddWithValue("payable_id", command.PayableId);
                    updatePayment.Parameters.AddWithValue("previous_amount_thb", payment.AmountThb);

                    if (await updatePayment.ExecuteNonQueryAsync(ct) != 1)
                    {
                        throw new InvalidOperationException("Locked Payment changed unexpectedly during correction.");
                    }
                }

                var result = new CorrectPaymentAmountResult(
                    command.PaymentId,
                    command.PayableId,
                    payment.AmountThb,
                    command.CorrectedAmountThb,
                    newOutstandingThb,
                    checked(command.ExpectedOutstandingVersion + 1),
                    now);
                var resultJson = JsonSerializer.Serialize(result, StoredJsonOptions);

                await AppendCorrectionAuditAsync(execution, payment.AmountThb, now, ct);
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

                return CommandTransactionDecision<ApplicationResult<CorrectPaymentAmountResult>>.Commit(
                    ApplicationResult<CorrectPaymentAmountResult>.Success(result));
            },
            cancellationToken);
    }

    private async ValueTask<PaymentSnapshot?> LockPaymentAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        await using var command = FinanceCommandPersistence.CreateSqlCommand(
            _dbContext,
            """
            SELECT payable_id, amount_thb
            FROM finance.payments
            WHERE id = @payment_id
            FOR UPDATE;
            """);
        command.Parameters.AddWithValue("payment_id", paymentId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PaymentSnapshot(reader.GetGuid(0), reader.GetInt64(1));
    }

    private async ValueTask<OutstandingSnapshot> LockOutstandingAsync(
        Guid payableId,
        CancellationToken cancellationToken)
    {
        await using var command = FinanceCommandPersistence.CreateSqlCommand(
            _dbContext,
            """
            SELECT outstanding_thb, settlement_total_thb, row_version
            FROM finance.payable_outstanding_positions
            WHERE payable_id = @payable_id
            FOR UPDATE;
            """);
        command.Parameters.AddWithValue("payable_id", payableId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Payment Payable is missing its Outstanding Position.");
        }

        return new OutstandingSnapshot(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    private async ValueTask AppendCorrectionAuditAsync(
        CorrectPaymentAmountExecution execution,
        long previousAmountThb,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var correctedAuditEventId = await FindLatestPaymentAuditEventAsync(
            execution.Command.PaymentId,
            cancellationToken)
            ?? throw new InvalidOperationException("Payment has no audit event to correct.");
        var correctionAuditEventId = Guid.CreateVersion7();
        var subjectKeyJson = JsonSerializer.Serialize(
            new { id = execution.Command.PaymentId },
            StoredJsonOptions);
        var changeSummaryJson = JsonSerializer.Serialize(
            new
            {
                amountThb = new
                {
                    before = previousAmountThb,
                    after = execution.Command.CorrectedAmountThb,
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
                (object?)execution.Command.ReasonText ?? DBNull.Value);
            await auditEvent.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var subject = FinanceCommandPersistence.CreateSqlCommand(
            _dbContext,
            """
            INSERT INTO audit.audit_event_subjects
                (audit_event_id, sequence, subject_kind, subject_key, change_kind,
                 before_row_version, after_row_version, change_summary)
            VALUES
                (@audit_event_id, 1, 'finance.payment', CAST(@subject_key AS jsonb), 'UPDATE',
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

    private async ValueTask<Guid?> FindLatestPaymentAuditEventAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        await using var command = FinanceCommandPersistence.CreateSqlCommand(
            _dbContext,
            """
            SELECT e.id
            FROM audit.audit_events e
            JOIN audit.audit_event_subjects s ON s.audit_event_id = e.id
            WHERE e.command_type IN ('PayPayable', 'CorrectPaymentAmount')
              AND s.subject_kind = 'finance.payment'
              AND s.subject_key ->> 'id' = @payment_id
            ORDER BY e.occurred_at DESC, e.id DESC
            LIMIT 1;
            """);
        command.Parameters.AddWithValue("payment_id", paymentId.ToString());
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null ? null : (Guid)result;
    }

    private static CommandTransactionDecision<ApplicationResult<CorrectPaymentAmountResult>> RollbackFailure(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<CorrectPaymentAmountResult>>.Rollback(
            ApplicationResult<CorrectPaymentAmountResult>.Failure(ApplicationError.Create(kind, code)));

    private sealed record PaymentSnapshot(Guid PayableId, long AmountThb);
    private sealed record OutstandingSnapshot(long OutstandingThb, long SettlementTotalThb, long RowVersion);
}
