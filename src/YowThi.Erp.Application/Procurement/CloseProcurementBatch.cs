using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Procurement;

public sealed record CloseProcurementBatchCommand(
    Guid ProcurementBatchId,
    long ExpectedRowVersion);

public sealed record CloseProcurementBatchExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    CloseProcurementBatchCommand Command);

public sealed record CloseProcurementBatchResult(
    Guid ProcurementBatchId,
    Guid? InventoryOperationId,
    int ReconciledPositionCount,
    long ClosedRowVersion);

public interface ICloseProcurementBatchExecutor
{
    ValueTask<ApplicationResult<CloseProcurementBatchResult>> ExecuteAsync(
        CloseProcurementBatchExecution execution,
        CancellationToken cancellationToken);
}

public static class ProcurementBatchCloseErrorCodes
{
    public const string InvalidInput = "procurement.batch-close-invalid";
    public const string BatchNotFound = "procurement.batch-not-found";
    public const string BatchUnavailable = "procurement.batch-unavailable";
    public const string AlreadyClosed = "procurement.batch-already-closed";
    public const string SellableInventoryRemaining = "procurement.batch-sellable-inventory-remaining";
    public const string ConcurrentChange = "procurement.concurrent-change";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class CloseProcurementBatchValidation
{
    public static ApplicationError? Validate(CloseProcurementBatchCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ProcurementBatchId == Guid.Empty || command.ExpectedRowVersion <= 0)
        {
            return ApplicationError.Create(
                ApplicationErrorKind.Validation,
                ProcurementBatchCloseErrorCodes.InvalidInput);
        }

        return null;
    }
}
