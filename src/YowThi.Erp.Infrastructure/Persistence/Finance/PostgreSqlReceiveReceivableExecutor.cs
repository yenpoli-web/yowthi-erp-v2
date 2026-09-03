using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Finance;
using YowThi.Erp.Domain.Finance;

namespace YowThi.Erp.Infrastructure.Persistence.Finance;

internal sealed class PostgreSqlReceiveReceivableExecutor : IReceiveReceivableExecutor
{
    private const string CommandType = "ReceiveReceivable";
    private const string OutboxMessageType = "finance.receipt-recorded";
    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlReceiveReceivableExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public async ValueTask<ApplicationResult<ReceiveReceivableResult>> ExecuteAsync(
        ReceiveReceivableExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);

        var validationError = ReceiveReceivableValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ApplicationResult<ReceiveReceivableResult>.Failure(validationError);
        }

        return await _transactionRunner.ExecuteAsync(
            async ct =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await FinanceCommandPersistence.AcquireAsync<ReceiveReceivableResult>(
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
                    return CommandTransactionDecision<ApplicationResult<ReceiveReceivableResult>>.Rollback(
                        ApplicationResult<ReceiveReceivableResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == FinanceCommandAcquisitionKind.Conflict)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        FinanceApplicationErrorCodes.IdempotencyKeyReused);
                }

                var command = execution.Command;
                var receivable = await _dbContext.Set<Receivable>()
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == command.ReceivableId, ct);
                if (receivable is null)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.NotFound,
                        FinanceApplicationErrorCodes.ReceivableNotFound);
                }

                var snapshot = await ReadOutstandingAsync(command.ReceivableId, ct);
                if (snapshot.RowVersion != command.ExpectedOutstandingVersion)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        FinanceApplicationErrorCodes.OutstandingChanged);
                }

                var amountThb = command.AmountThb ?? snapshot.OutstandingThb;
                if (amountThb <= 0)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Validation,
                        FinanceApplicationErrorCodes.InvalidInput);
                }

                if (amountThb > snapshot.OutstandingThb)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        FinanceApplicationErrorCodes.ReceiptExceedsOutstanding);
                }

                await using (var update = FinanceCommandPersistence.CreateSqlCommand(
                    _dbContext,
                    """
                    UPDATE finance.receivable_outstanding_positions
                    SET settlement_total_thb = settlement_total_thb + @amount_thb,
                        outstanding_thb = outstanding_thb - @amount_thb,
                        row_version = row_version + 1,
                        updated_at = @updated_at
                    WHERE receivable_id = @receivable_id
                      AND row_version = @expected_row_version
                      AND outstanding_thb >= @amount_thb;
                    """))
                {
                    update.Parameters.AddWithValue("amount_thb", amountThb);
                    update.Parameters.AddWithValue("updated_at", now);
                    update.Parameters.AddWithValue("receivable_id", command.ReceivableId);
                    update.Parameters.AddWithValue("expected_row_version", command.ExpectedOutstandingVersion);

                    if (await update.ExecuteNonQueryAsync(ct) != 1)
                    {
                        var current = await ReadOutstandingAsync(command.ReceivableId, ct);
                        if (current.RowVersion != command.ExpectedOutstandingVersion)
                        {
                            return RollbackFailure(
                                ApplicationErrorKind.Conflict,
                                FinanceApplicationErrorCodes.OutstandingChanged);
                        }

                        if (amountThb > current.OutstandingThb)
                        {
                            return RollbackFailure(
                                ApplicationErrorKind.Conflict,
                                FinanceApplicationErrorCodes.ReceiptExceedsOutstanding);
                        }

                        throw new InvalidOperationException("Receivable Outstanding CAS failed without an explained state change.");
                    }
                }

                var receipt = Receipt.Create(
                    Guid.CreateVersion7(),
                    command.ReceivableId,
                    amountThb,
                    now,
                    execution.ActorAccountId.Value);
                _dbContext.Add(receipt);
                await _dbContext.SaveChangesAsync(ct);

                var result = new ReceiveReceivableResult(
                    receipt.Id,
                    command.ReceivableId,
                    amountThb,
                    checked(snapshot.OutstandingThb - amountThb),
                    checked(command.ExpectedOutstandingVersion + 1),
                    now);
                var resultJson = JsonSerializer.Serialize(result, StoredJsonOptions);

                await FinanceCommandPersistence.AppendAuditAsync(
                    _dbContext,
                    execution.CommandId,
                    CommandType,
                    execution.ActorAccountId,
                    "finance.receipt",
                    receipt.Id,
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

                return CommandTransactionDecision<ApplicationResult<ReceiveReceivableResult>>.Commit(
                    ApplicationResult<ReceiveReceivableResult>.Success(result));
            },
            cancellationToken);
    }

    private async ValueTask<OutstandingSnapshot> ReadOutstandingAsync(
        Guid receivableId,
        CancellationToken cancellationToken)
    {
        await using var command = FinanceCommandPersistence.CreateSqlCommand(
            _dbContext,
            """
            SELECT outstanding_thb, row_version
            FROM finance.receivable_outstanding_positions
            WHERE receivable_id = @receivable_id;
            """);
        command.Parameters.AddWithValue("receivable_id", receivableId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Existing Receivable is missing its Outstanding Position.");
        }

        return new OutstandingSnapshot(reader.GetInt64(0), reader.GetInt64(1));
    }

    private static CommandTransactionDecision<ApplicationResult<ReceiveReceivableResult>> RollbackFailure(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<ReceiveReceivableResult>>.Rollback(
            ApplicationResult<ReceiveReceivableResult>.Failure(ApplicationError.Create(kind, code)));

    private readonly record struct OutstandingSnapshot(long OutstandingThb, long RowVersion);
}
