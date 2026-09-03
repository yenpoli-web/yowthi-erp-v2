using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Outsourced;

public sealed record CloseOutsourcedSupplyBatchCommand(
    Guid OutsourcedSupplyBatchId,
    long ExpectedRowVersion);

public sealed record CloseOutsourcedSupplyBatchExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    CloseOutsourcedSupplyBatchCommand Command);

public sealed record CloseOutsourcedSupplyBatchResult(
    Guid OutsourcedSupplyBatchId,
    long ClosedRowVersion);

public interface ICloseOutsourcedSupplyBatchExecutor
{
    ValueTask<ApplicationResult<CloseOutsourcedSupplyBatchResult>> ExecuteAsync(
        CloseOutsourcedSupplyBatchExecution execution,
        CancellationToken cancellationToken);
}

public static class OutsourcedBatchCloseErrorCodes
{
    public const string InvalidInput = "outsourced.batch-close-invalid";
    public const string BatchNotFound = "outsourced.batch-not-found";
    public const string BatchUnavailable = "outsourced.batch-unavailable";
    public const string AlreadyClosed = "outsourced.batch-already-closed";
    public const string SellableInventoryRemaining = "outsourced.batch-sellable-inventory-remaining";
    public const string ConcurrentChange = "outsourced.concurrent-change";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class CloseOutsourcedSupplyBatchValidation
{
    public static ApplicationError? Validate(CloseOutsourcedSupplyBatchCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.OutsourcedSupplyBatchId == Guid.Empty || command.ExpectedRowVersion <= 0)
        {
            return ApplicationError.Create(
                ApplicationErrorKind.Validation,
                OutsourcedBatchCloseErrorCodes.InvalidInput);
        }

        return null;
    }
}
