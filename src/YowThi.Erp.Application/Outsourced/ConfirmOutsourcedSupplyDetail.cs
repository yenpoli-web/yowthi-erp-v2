using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Outsourced;

public sealed record ConfirmOutsourcedSupplyDetailCommand(
    DateOnly SupplyDate,
    Guid OutsourcedVendorId,
    Guid SalesProductId,
    decimal Quantity,
    decimal UnitPrice,
    Guid? ReceiptStorageLocationId);

public sealed record ConfirmOutsourcedSupplyDetailExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    ConfirmOutsourcedSupplyDetailCommand Command);

public sealed record ConfirmOutsourcedSupplyDetailResult(
    Guid OutsourcedSupplyDetailId,
    Guid OutsourcedSupplyBatchId,
    Guid InventoryOperationId,
    Guid PayableId,
    Guid ReceiptStorageLocationId,
    long AmountThb,
    long OutsourcedSupplyDetailRowVersion);

public interface IConfirmOutsourcedSupplyDetailExecutor
{
    ValueTask<ApplicationResult<ConfirmOutsourcedSupplyDetailResult>> ExecuteAsync(
        ConfirmOutsourcedSupplyDetailExecution execution,
        CancellationToken cancellationToken);
}

public static class OutsourcedApplicationErrorCodes
{
    public const string InvalidQuantity = "outsourced.quantity-invalid";
    public const string ZeroQuantityUnverified = "outsourced.quantity-zero-unverified";
    public const string InvalidUnitPrice = "outsourced.unit-price-invalid";
    public const string AmountOutOfRange = "outsourced.amount-out-of-range";
    public const string VendorNotFound = "outsourced.vendor-not-found";
    public const string VendorInactive = "outsourced.vendor-inactive";
    public const string ProductNotFound = "outsourced.product-not-found";
    public const string ProductInactive = "outsourced.product-inactive";
    public const string ReceiptLocationNotFound = "outsourced.receipt-location-not-found";
    public const string ReceiptLocationInactive = "outsourced.receipt-location-inactive";
    public const string ReceiptLocationRequired = "outsourced.receipt-location-required";
    public const string BatchUnavailable = "outsourced.batch-unavailable";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class ConfirmOutsourcedSupplyDetailValidation
{
    public static ApplicationError? Validate(ConfirmOutsourcedSupplyDetailCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.OutsourcedVendorId == Guid.Empty)
        {
            return Validation(OutsourcedApplicationErrorCodes.VendorNotFound);
        }

        if (command.SalesProductId == Guid.Empty)
        {
            return Validation(OutsourcedApplicationErrorCodes.ProductNotFound);
        }

        if (command.Quantity < 0)
        {
            return Validation(OutsourcedApplicationErrorCodes.InvalidQuantity);
        }

        // OUT-002 / TO VERIFY: the relational Outsourced Supply Detail permits zero quantity,
        // while the required OUTSOURCED_RECEIPT movement structurally requires a positive delta.
        // Block the unsupported edge without promoting this control to a permanent Business Rule.
        if (command.Quantity == 0)
        {
            return Validation(OutsourcedApplicationErrorCodes.ZeroQuantityUnverified);
        }

        if (command.UnitPrice < 0)
        {
            return Validation(OutsourcedApplicationErrorCodes.InvalidUnitPrice);
        }

        if (command.ReceiptStorageLocationId == Guid.Empty)
        {
            return Validation(OutsourcedApplicationErrorCodes.ReceiptLocationNotFound);
        }

        return null;
    }

    private static ApplicationError Validation(string code) =>
        ApplicationError.Create(ApplicationErrorKind.Validation, code);
}
