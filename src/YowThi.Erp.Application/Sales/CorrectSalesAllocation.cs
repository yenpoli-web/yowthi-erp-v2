using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Domain.Inventory;

namespace YowThi.Erp.Application.Sales;

public enum SalesAllocationCorrectionMode
{
    COMPLETE_REPLACEMENT,
    OVERRIDE_AND_REALLOCATE,
}

public sealed record CorrectSalesAllocationCommand(
    Guid SalesId,
    long ExpectedRowVersion,
    SalesAllocationCorrectionMode Mode,
    IReadOnlyList<SalesAllocationCorrectionInput> Allocations);

public sealed record SalesAllocationCorrectionInput(
    Guid SalesDetailId,
    InventoryOrigin Origin,
    Guid? ProcurementBatchId,
    Guid? OutsourcedSupplyBatchId,
    decimal AllocatedQuantity);

public sealed record CorrectSalesAllocationExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    CorrectSalesAllocationCommand Command);

public sealed record CorrectSalesAllocationResult(
    Guid SalesId,
    long SalesRowVersion,
    Guid AllocationRevisionId,
    int RevisionNumber,
    Guid InventoryOperationId);

public interface ICorrectSalesAllocationExecutor
{
    ValueTask<ApplicationResult<CorrectSalesAllocationResult>> ExecuteAsync(
        CorrectSalesAllocationExecution execution,
        CancellationToken cancellationToken);
}

public static class SalesAllocationCorrectionErrorCodes
{
    public const string InvalidInput = "sales.allocation-correction-invalid";
    public const string InvalidState = "sales.allocation-correction-invalid-state";
    public const string CurrentAllocationMissing = "sales.allocation-current-missing";
    public const string LifecycleBlocked = "sales.allocation-correction-lifecycle-blocked";
}

public static class CorrectSalesAllocationValidation
{
    public static ApplicationError? Validate(CorrectSalesAllocationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.SalesId == Guid.Empty
            || command.ExpectedRowVersion < 1
            || command.Allocations is null
            || !Enum.IsDefined(command.Mode))
        {
            return Validation();
        }

        foreach (var allocation in command.Allocations)
        {
            if (allocation is null
                || allocation.SalesDetailId == Guid.Empty
                || allocation.AllocatedQuantity < 0)
            {
                return Validation();
            }

            var sourceShapeIsValid = allocation.Origin switch
            {
                InventoryOrigin.IN_HOUSE => allocation.ProcurementBatchId is { } procurementBatchId
                    && procurementBatchId != Guid.Empty
                    && allocation.OutsourcedSupplyBatchId is null,
                InventoryOrigin.OUTSOURCED => allocation.OutsourcedSupplyBatchId is { } outsourcedBatchId
                    && outsourcedBatchId != Guid.Empty
                    && allocation.ProcurementBatchId is null,
                _ => false,
            };

            if (!sourceShapeIsValid)
            {
                return Validation();
            }
        }

        return null;
    }

    private static ApplicationError Validation() =>
        ApplicationError.Create(
            ApplicationErrorKind.Validation,
            SalesAllocationCorrectionErrorCodes.InvalidInput);
}