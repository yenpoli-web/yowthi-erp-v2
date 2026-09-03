using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Domain.Finance;

namespace YowThi.Erp.Application.Finance;

public sealed record AddPayableAdjustmentCommand(
    Guid PayableId,
    long ExpectedOutstandingVersion,
    PayableAdjustmentType AdjustmentType,
    long AmountDeltaThb,
    string? ReasonText);

public sealed record AddPayableAdjustmentExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    AddPayableAdjustmentCommand Command);

public sealed record AddPayableAdjustmentResult(
    Guid PayableAdjustmentId,
    Guid PayableId,
    long AmountDeltaThb,
    long OutstandingThb,
    long OutstandingVersion,
    DateTimeOffset RecordedAt);

public interface IAddPayableAdjustmentExecutor
{
    ValueTask<ApplicationResult<AddPayableAdjustmentResult>> ExecuteAsync(
        AddPayableAdjustmentExecution execution,
        CancellationToken cancellationToken);
}

public static class AddPayableAdjustmentValidation
{
    public static ApplicationError? Validate(AddPayableAdjustmentCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.PayableId == Guid.Empty
            || command.ExpectedOutstandingVersion < 1
            || command.AdjustmentType != PayableAdjustmentType.SUPPLIER_QUALITY_WEIGHT_DEDUCTION
            || command.AmountDeltaThb >= 0)
        {
            return ApplicationError.Create(
                ApplicationErrorKind.Validation,
                FinanceApplicationErrorCodes.InvalidInput);
        }

        return null;
    }
}
