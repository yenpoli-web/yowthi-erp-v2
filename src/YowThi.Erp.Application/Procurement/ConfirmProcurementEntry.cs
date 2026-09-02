using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Domain.Procurement;

namespace YowThi.Erp.Application.Procurement;

public sealed record ConfirmProcurementEntryCommand(
    DateOnly ProcurementDate,
    Guid ProcurementProductId,
    ProcurementSourceType SourceType,
    Guid? SupplierId,
    Guid? FarmerId,
    decimal NetQuantity,
    decimal UnitPrice,
    bool CompanyPickup,
    Guid? ReceiptStorageLocationId);

public sealed record ConfirmProcurementEntryExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    ConfirmProcurementEntryCommand Command);

public sealed record ConfirmProcurementEntryResult(
    Guid ProcurementEntryId,
    Guid ProcurementBatchId,
    Guid InventoryOperationId,
    Guid PayableId,
    Guid? CompanyPickupTransportBasisId,
    Guid ReceiptStorageLocationId,
    long AmountThb,
    long ProcurementEntryRowVersion);

public interface IConfirmProcurementEntryExecutor
{
    ValueTask<ApplicationResult<ConfirmProcurementEntryResult>> ExecuteAsync(
        ConfirmProcurementEntryExecution execution,
        CancellationToken cancellationToken);
}

public static class ProcurementApplicationErrorCodes
{
    public const string InvalidSource = "procurement.source-invalid";
    public const string InvalidNetQuantity = "procurement.net-quantity-invalid";
    public const string ZeroNetQuantityUnverified = "procurement.net-quantity-zero-unverified";
    public const string InvalidUnitPrice = "procurement.unit-price-invalid";
    public const string AmountOutOfRange = "procurement.amount-out-of-range";
    public const string ProductNotFound = "procurement.product-not-found";
    public const string ProductInactive = "procurement.product-inactive";
    public const string SourceNotFound = "procurement.source-not-found";
    public const string SourceInactive = "procurement.source-inactive";
    public const string ReceiptLocationNotFound = "procurement.receipt-location-not-found";
    public const string ReceiptLocationInactive = "procurement.receipt-location-inactive";
    public const string ReceiptLocationRequired = "procurement.receipt-location-required";
    public const string BatchCompleted = "procurement.batch-completed";
    public const string BatchUnavailable = "procurement.batch-unavailable";
    public const string ConcurrentChange = "procurement.concurrent-change";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class ConfirmProcurementEntryValidation
{
    public static ApplicationError? Validate(ConfirmProcurementEntryCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ProcurementProductId == Guid.Empty)
        {
            return Validation(ProcurementApplicationErrorCodes.ProductNotFound);
        }

        var sourceIsValid = command.SourceType switch
        {
            ProcurementSourceType.SUPPLIER => command.SupplierId is { } supplierId
                && supplierId != Guid.Empty
                && command.FarmerId is null,
            ProcurementSourceType.FARMER => command.FarmerId is { } farmerId
                && farmerId != Guid.Empty
                && command.SupplierId is null,
            _ => false,
        };

        if (!sourceIsValid)
        {
            return Validation(ProcurementApplicationErrorCodes.InvalidSource);
        }

        if (command.NetQuantity < 0)
        {
            return Validation(ProcurementApplicationErrorCodes.InvalidNetQuantity);
        }

        // PROC-003 / TO VERIFY: the relational Procurement Entry permits zero quantity,
        // while the required PURCHASE_RECEIPT movement structurally requires a positive delta.
        // Block the unsupported edge without promoting this control to a permanent Business Rule.
        if (command.NetQuantity == 0)
        {
            return Validation(ProcurementApplicationErrorCodes.ZeroNetQuantityUnverified);
        }

        if (command.UnitPrice < 0)
        {
            return Validation(ProcurementApplicationErrorCodes.InvalidUnitPrice);
        }

        if (command.ReceiptStorageLocationId == Guid.Empty)
        {
            return Validation(ProcurementApplicationErrorCodes.ReceiptLocationNotFound);
        }

        return null;
    }

    private static ApplicationError Validation(string code) =>
        ApplicationError.Create(ApplicationErrorKind.Validation, code);
}
