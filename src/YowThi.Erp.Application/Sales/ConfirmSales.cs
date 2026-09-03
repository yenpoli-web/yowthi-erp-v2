using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Domain.Inventory;

namespace YowThi.Erp.Application.Sales;

public sealed record ConfirmSalesCommand(
    Guid SalesId,
    long ExpectedRowVersion,
    IReadOnlyList<SalesManualAllocationOverride> ManualAllocationOverrides);

public sealed record SalesManualAllocationOverride(
    Guid SalesDetailId,
    InventoryOrigin Origin,
    Guid? ProcurementBatchId,
    Guid? OutsourcedSupplyBatchId,
    decimal AllocatedQuantity);

public sealed record ConfirmSalesExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    ConfirmSalesCommand Command);

public sealed record ConfirmSalesResult(
    Guid SalesId,
    long SalesRowVersion,
    Guid ReceivableId,
    Guid AllocationRevisionId,
    Guid InventoryOperationId);

public interface IConfirmSalesExecutor
{
    ValueTask<ApplicationResult<ConfirmSalesResult>> ExecuteAsync(
        ConfirmSalesExecution execution,
        CancellationToken cancellationToken);
}

public static class SalesApplicationErrorCodes
{
    public const string SalesNotFound = "sales.not-found";
    public const string InvalidState = "sales.invalid-state";
    public const string InvalidExpectedRowVersion = "sales.expected-row-version-invalid";
    public const string StaleRowVersion = "concurrency.stale-row-version";
    public const string DetailInvalid = "sales.detail-invalid";
    public const string PricingInvalid = "sales.pricing-invalid";
    public const string AllocationInvalid = "sales.allocation-invalid";
    public const string IssueLocationRequired = "sales.issue-location-required";
    public const string InsufficientStock = "inventory.insufficient-stock";
    public const string ConcurrentInventoryChange = "inventory.concurrent-change";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class ConfirmSalesValidation
{
    public static ApplicationError? Validate(ConfirmSalesCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.SalesId == Guid.Empty)
        {
            return Validation(SalesApplicationErrorCodes.SalesNotFound);
        }

        if (command.ExpectedRowVersion < 1)
        {
            return Validation(SalesApplicationErrorCodes.InvalidExpectedRowVersion);
        }

        if (command.ManualAllocationOverrides is null)
        {
            return Validation(SalesApplicationErrorCodes.AllocationInvalid);
        }

        foreach (var allocation in command.ManualAllocationOverrides)
        {
            if (allocation is null
                || allocation.SalesDetailId == Guid.Empty
                || allocation.AllocatedQuantity < 0)
            {
                return Validation(SalesApplicationErrorCodes.AllocationInvalid);
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
                return Validation(SalesApplicationErrorCodes.AllocationInvalid);
            }
        }

        // SALES-001 remains a Class A gap. This command deliberately does not invent a
        // storage-location override vocabulary; the execution layer may auto-resolve only
        // an unambiguous location and must otherwise block with IssueLocationRequired.
        return null;
    }

    private static ApplicationError Validation(string code) =>
        ApplicationError.Create(ApplicationErrorKind.Validation, code);
}