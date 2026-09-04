using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Finance;

public sealed record CorrectPaymentAmountCommand(
    Guid PayableId,
    Guid PaymentId,
    long CorrectedAmountThb,
    long ExpectedOutstandingVersion,
    string? ReasonText);

public sealed record CorrectPaymentAmountExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    CorrectPaymentAmountCommand Command);

public sealed record CorrectPaymentAmountResult(
    Guid PaymentId,
    Guid PayableId,
    long PreviousAmountThb,
    long CorrectedAmountThb,
    long OutstandingThb,
    long OutstandingVersion,
    DateTimeOffset CorrectedAt);

public interface ICorrectPaymentAmountExecutor
{
    ValueTask<ApplicationResult<CorrectPaymentAmountResult>> ExecuteAsync(
        CorrectPaymentAmountExecution execution,
        CancellationToken cancellationToken);
}

public static class CorrectPaymentAmountValidation
{
    public static ApplicationError? Validate(CorrectPaymentAmountCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.PayableId == Guid.Empty
            || command.PaymentId == Guid.Empty
            || command.CorrectedAmountThb <= 0
            || command.ExpectedOutstandingVersion < 1)
        {
            return ApplicationError.Create(
                ApplicationErrorKind.Validation,
                FinanceApplicationErrorCodes.InvalidInput);
        }

        return null;
    }
}
