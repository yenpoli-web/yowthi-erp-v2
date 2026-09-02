namespace YowThi.Erp.Domain.Inventory;

public sealed class InventoryMovement
{
    public Guid Id { get; private set; }
    public Guid InventoryOperationId { get; private set; }
    public int Sequence { get; private set; }
    public InventoryMovementType MovementType { get; private set; }
    public InventoryOrigin Origin { get; private set; }
    public Guid? ProcurementBatchId { get; private set; }
    public Guid? OutsourcedSupplyBatchId { get; private set; }
    public InventoryObjectKind InventoryObjectKind { get; private set; }
    public Guid? ProcurementProductId { get; private set; }
    public Guid? ProcessMaterialId { get; private set; }
    public Guid? SalesProductId { get; private set; }
    public Guid StorageLocationId { get; private set; }
    public InventoryRawSourceKind? RawSourceKind { get; private set; }
    public Guid? SupplierId { get; private set; }
    public decimal QuantityDelta { get; private set; }
    public Guid? SalesAllocationRevisionItemId { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    public static InventoryMovement CreateProcurementReceipt(
        Guid id,
        Guid inventoryOperationId,
        Guid procurementBatchId,
        Guid procurementProductId,
        Guid storageLocationId,
        InventoryRawSourceKind rawSourceKind,
        Guid? supplierId,
        decimal quantity,
        DateTimeOffset recordedAt)
    {
        if (id == Guid.Empty
            || inventoryOperationId == Guid.Empty
            || procurementBatchId == Guid.Empty
            || procurementProductId == Guid.Empty
            || storageLocationId == Guid.Empty)
        {
            throw new ArgumentException("Inventory movement technical and reference IDs cannot be empty.");
        }

        return new InventoryMovement
        {
            Id = id,
            InventoryOperationId = inventoryOperationId,
            Sequence = 1,
            MovementType = InventoryMovementType.PURCHASE_RECEIPT,
            Origin = InventoryOrigin.IN_HOUSE,
            ProcurementBatchId = procurementBatchId,
            InventoryObjectKind = InventoryObjectKind.PROCUREMENT_PRODUCT,
            ProcurementProductId = procurementProductId,
            StorageLocationId = storageLocationId,
            RawSourceKind = rawSourceKind,
            SupplierId = supplierId,
            QuantityDelta = quantity,
            RecordedAt = recordedAt,
        };
    }
}
