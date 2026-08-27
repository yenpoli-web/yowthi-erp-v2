using YowThi.Erp.Domain.Common;

namespace YowThi.Erp.Domain.Inventory;

public sealed class InventoryPosition : IHasRowVersion
{
    public Guid Id { get; private set; }
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
    public decimal BalanceQuantity { get; private set; }
    public long RowVersion { get; private set; }
}
