using YowThi.Erp.Domain.Inventory;

namespace YowThi.Erp.Application.Inventory;

public sealed record InventoryPositionIdentity(
    InventoryOrigin Origin,
    Guid? ProcurementBatchId,
    Guid? OutsourcedSupplyBatchId,
    InventoryObjectKind InventoryObjectKind,
    Guid? ProcurementProductId,
    Guid? ProcessMaterialId,
    Guid? SalesProductId,
    InventoryRawSourceKind? RawSourceKind,
    Guid? SupplierId);

public static class InventoryPositionIdentityValidation
{
    public static bool IsValid(InventoryPositionIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var sourceBatchIsValid = identity.Origin switch
        {
            InventoryOrigin.IN_HOUSE => IsPresent(identity.ProcurementBatchId)
                && identity.OutsourcedSupplyBatchId is null,
            InventoryOrigin.OUTSOURCED => IsPresent(identity.OutsourcedSupplyBatchId)
                && identity.ProcurementBatchId is null,
            _ => false,
        };

        var inventoryObjectIsValid = identity.InventoryObjectKind switch
        {
            InventoryObjectKind.PROCUREMENT_PRODUCT => IsPresent(identity.ProcurementProductId)
                && identity.ProcessMaterialId is null
                && identity.SalesProductId is null,
            InventoryObjectKind.PROCESS_MATERIAL => identity.ProcurementProductId is null
                && IsPresent(identity.ProcessMaterialId)
                && identity.SalesProductId is null,
            InventoryObjectKind.SALES_PRODUCT => identity.ProcurementProductId is null
                && identity.ProcessMaterialId is null
                && IsPresent(identity.SalesProductId),
            _ => false,
        };

        var rawSourceIsValid = identity.RawSourceKind switch
        {
            null => identity.SupplierId is null,
            InventoryRawSourceKind.SUPPLIER => identity.Origin == InventoryOrigin.IN_HOUSE
                && identity.InventoryObjectKind == InventoryObjectKind.PROCUREMENT_PRODUCT
                && IsPresent(identity.SupplierId),
            InventoryRawSourceKind.FARMERS_COMBINED => identity.Origin == InventoryOrigin.IN_HOUSE
                && identity.InventoryObjectKind == InventoryObjectKind.PROCUREMENT_PRODUCT
                && identity.SupplierId is null,
            _ => false,
        };

        return sourceBatchIsValid && inventoryObjectIsValid && rawSourceIsValid;
    }

    private static bool IsPresent(Guid? value) => value is { } id && id != Guid.Empty;
}
