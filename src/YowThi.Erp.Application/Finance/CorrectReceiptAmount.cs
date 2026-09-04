using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Finance;

public sealed record CorrectReceiptAmountCommand(
    Guid ReceivableId,
    Guid ReceiptId,
    long CorrectedAmountThb,
    long ExpectedOutstandingVersion,
    string? ReasonText);

public sealed record CorrectReceiptAmountExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    CorrectReceiptAmountCommand Command);

public sealed record CorrectReceiptAmountResult(
    Guid ReceiptId,
    Guid ReceivableId,
    long PreviousAmountThb,
    long CorrectedAmountThb,
    long OutstandingThb,
    long OutstandingVersion,
    DateTimeOffset CorrectedAt);

public interface ICorrectReceiptAmountExecutor
{
    ValueTask<ApplicationResult<CorrectReceiptAmountResult>> ExecuteAsync(
        CorrectReceiptAmountExecution execution,
        CancellationToken cancellationToken);
}

public static class CorrectReceiptAmountValidation
{
    public static ApplicationError? Validate(CorrectReceiptAmountCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ReceivableId == Guid.Empty
            || command.ReceiptId == Guid.Empty
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
