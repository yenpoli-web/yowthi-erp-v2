using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Finance;

public sealed record CorrectPayableAdjustmentCommand(
    Guid PayableId,
    Guid PayableAdjustmentId,
    long CorrectedAmountDeltaThb,
    string? CorrectedReasonText,
    long ExpectedOutstandingVersion,
    string? CorrectionReasonText);

public sealed record CorrectPayableAdjustmentExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    CorrectPayableAdjustmentCommand Command);

public sealed record CorrectPayableAdjustmentResult(
    Guid PayableAdjustmentId,
    Guid PayableId,
    long PreviousAmountDeltaThb,
    long CorrectedAmountDeltaThb,
    string? PreviousReasonText,
    string? CorrectedReasonText,
    long OutstandingThb,
    long OutstandingVersion,
    DateTimeOffset CorrectedAt);

public interface ICorrectPayableAdjustmentExecutor
{
    ValueTask<ApplicationResult<CorrectPayableAdjustmentResult>> ExecuteAsync(
        CorrectPayableAdjustmentExecution execution,
        CancellationToken cancellationToken);
}

public static class CorrectPayableAdjustmentValidation
{
    public static ApplicationError? Validate(CorrectPayableAdjustmentCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.PayableId == Guid.Empty
            || command.PayableAdjustmentId == Guid.Empty
            || command.CorrectedAmountDeltaThb >= 0
            || command.ExpectedOutstandingVersion < 1)
        {
            return ApplicationError.Create(
                ApplicationErrorKind.Validation,
                FinanceApplicationErrorCodes.InvalidInput);
        }

        return null;
    }
}
