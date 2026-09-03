using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Finance;

public sealed record PayPayableCommand(
    Guid PayableId,
    long? AmountThb,
    long ExpectedOutstandingVersion);

public sealed record PayPayableExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    PayPayableCommand Command);

public sealed record PayPayableResult(
    Guid PaymentId,
    Guid PayableId,
    long AmountThb,
    long OutstandingThb,
    long OutstandingVersion,
    DateTimeOffset ConfirmedAt);

public interface IPayPayableExecutor
{
    ValueTask<ApplicationResult<PayPayableResult>> ExecuteAsync(
        PayPayableExecution execution,
        CancellationToken cancellationToken);
}

public static class PayPayableValidation
{
    public static ApplicationError? Validate(PayPayableCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.PayableId == Guid.Empty
            || command.ExpectedOutstandingVersion < 1
            || command.AmountThb is <= 0)
        {
            return ApplicationError.Create(
                ApplicationErrorKind.Validation,
                FinanceApplicationErrorCodes.InvalidInput);
        }

        return null;
    }
}
