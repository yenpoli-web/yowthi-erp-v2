using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Finance;

public sealed record ReceiveReceivableCommand(
    Guid ReceivableId,
    long? AmountThb,
    long ExpectedOutstandingVersion);

public sealed record ReceiveReceivableExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    ReceiveReceivableCommand Command);

public sealed record ReceiveReceivableResult(
    Guid ReceiptId,
    Guid ReceivableId,
    long AmountThb,
    long OutstandingThb,
    long OutstandingVersion,
    DateTimeOffset ConfirmedAt);

public interface IReceiveReceivableExecutor
{
    ValueTask<ApplicationResult<ReceiveReceivableResult>> ExecuteAsync(
        ReceiveReceivableExecution execution,
        CancellationToken cancellationToken);
}

public static class ReceiveReceivableValidation
{
    public static ApplicationError? Validate(ReceiveReceivableCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ReceivableId == Guid.Empty
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
