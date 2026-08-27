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
}
