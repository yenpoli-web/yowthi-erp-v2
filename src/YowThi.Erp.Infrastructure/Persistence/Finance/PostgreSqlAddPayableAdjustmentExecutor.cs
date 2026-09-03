using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Finance;
using YowThi.Erp.Domain.Finance;

namespace YowThi.Erp.Infrastructure.Persistence.Finance;

internal sealed class PostgreSqlAddPayableAdjustmentExecutor : IAddPayableAdjustmentExecutor
{
    private const string CommandType = "AddPayableAdjustment";
    private const string OutboxMessageType = "finance.payable-adjusted";
    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlAddPayableAdjustmentExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public async ValueTask<ApplicationResult<AddPayableAdjustmentResult>> ExecuteAsync(
        AddPayableAdjustmentExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);

        var validationError = AddPayableAdjustmentValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ApplicationResult<AddPayableAdjustmentResult>.Failure(validationError);
        }

        return await _transactionRunner.ExecuteAsync(
            async ct =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await FinanceCommandPersistence.AcquireAsync<AddPayableAdjustmentResult>(
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
                    return CommandTransactionDecision<ApplicationResult<AddPayableAdjustmentResult>>.Rollback(
                        ApplicationResult<AddPayableAdjustmentResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == FinanceCommandAcquisitionKind.Conflict)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        FinanceApplicationErrorCodes.IdempotencyKeyReused);
                }

                var command = execution.Command;
                var payable = await _dbContext.Set<Payable>()
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == command.PayableId, ct);
                if (payable is null)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.NotFound,
                        FinanceApplicationErrorCodes.PayableNotFound);
                }

                if (payable.PayableKind != PayableKind.PROCUREMENT_SUPPLIER)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        FinanceApplicationErrorCodes.AdjustmentNotAllowed);
                }

                var snapshot = await ReadOutstandingAsync(command.PayableId, ct);
                if (snapshot.RowVersion != command.ExpectedOutstandingVersion)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        FinanceApplicationErrorCodes.OutstandingChanged);
                }

                if ((decimal)snapshot.OutstandingThb + command.AmountDeltaThb < 0m)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        FinanceApplicationErrorCodes.AdjustmentCausesNegativeOutstanding);
                }

                await using (var update = FinanceCommandPersistence.CreateSqlCommand(
                    _dbContext,
                    """
                    UPDATE finance.payable_outstanding_positions
                    SET adjustment_total_thb = adjustment_total_thb + @amount_delta_thb,
                        outstanding_thb = outstanding_thb + @amount_delta_thb,
                        row_version = row_version + 1,
                        updated_at = @updated_at
                    WHERE payable_id = @payable_id
                      AND row_version = @expected_row_version
                      AND outstanding_thb + @amount_delta_thb >= 0;
                    """))
                {
                    update.Parameters.AddWithValue("amount_delta_thb", command.AmountDeltaThb);
                    update.Parameters.AddWithValue("updated_at", now);
                    update.Parameters.AddWithValue("payable_id", command.PayableId);
                    update.Parameters.AddWithValue("expected_row_version", command.ExpectedOutstandingVersion);

                    if (await update.ExecuteNonQueryAsync(ct) != 1)
                    {
                        var current = await ReadOutstandingAsync(command.PayableId, ct);
                        if (current.RowVersion != command.ExpectedOutstandingVersion)
                        {
                            return RollbackFailure(
                                ApplicationErrorKind.Conflict,
                                FinanceApplicationErrorCodes.OutstandingChanged);
                        }

                        if ((decimal)current.OutstandingThb + command.AmountDeltaThb < 0m)
                        {
                            return RollbackFailure(
                                ApplicationErrorKind.Conflict,
                                FinanceApplicationErrorCodes.AdjustmentCausesNegativeOutstanding);
                        }

                        throw new InvalidOperationException("Payable Outstanding CAS failed without an explained state change.");
                    }
                }

                var adjustment = PayableAdjustment.Create(
                    Guid.CreateVersion7(),
                    command.PayableId,
                    command.AdjustmentType,
                    command.AmountDeltaThb,
                    command.ReasonText,
                    now,
                    execution.ActorAccountId.Value);
                _dbContext.Add(adjustment);
                await _dbContext.SaveChangesAsync(ct);

                var result = new AddPayableAdjustmentResult(
                    adjustment.Id,
                    command.PayableId,
                    command.AmountDeltaThb,
                    checked(snapshot.OutstandingThb + command.AmountDeltaThb),
                    checked(command.ExpectedOutstandingVersion + 1),
                    now);
                var resultJson = JsonSerializer.Serialize(result, StoredJsonOptions);

                await FinanceCommandPersistence.AppendAuditAsync(
                    _dbContext,
                    execution.CommandId,
                    CommandType,
                    execution.ActorAccountId,
                    "finance.payable-adjustment",
                    adjustment.Id,
                    now,
                    StoredJsonOptions,
                    ct);
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

                return CommandTransactionDecision<ApplicationResult<AddPayableAdjustmentResult>>.Commit(
                    ApplicationResult<AddPayableAdjustmentResult>.Success(result));
            },
            cancellationToken);
    }

    private async ValueTask<OutstandingSnapshot> ReadOutstandingAsync(
        Guid payableId,
        CancellationToken cancellationToken)
    {
        await using var command = FinanceCommandPersistence.CreateSqlCommand(
            _dbContext,
            """
            SELECT outstanding_thb, row_version
            FROM finance.payable_outstanding_positions
            WHERE payable_id = @payable_id;
            """);
        command.Parameters.AddWithValue("payable_id", payableId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Existing Payable is missing its Outstanding Position.");
        }

        return new OutstandingSnapshot(reader.GetInt64(0), reader.GetInt64(1));
    }

    private static CommandTransactionDecision<ApplicationResult<AddPayableAdjustmentResult>> RollbackFailure(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<AddPayableAdjustmentResult>>.Rollback(
            ApplicationResult<AddPayableAdjustmentResult>.Failure(ApplicationError.Create(kind, code)));

    private readonly record struct OutstandingSnapshot(long OutstandingThb, long RowVersion);
}
