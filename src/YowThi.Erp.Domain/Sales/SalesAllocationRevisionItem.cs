using YowThi.Erp.Domain.Inventory;

namespace YowThi.Erp.Domain.Sales;

public sealed class SalesAllocationRevisionItem
{
    public Guid Id { get; private set; }
    public Guid SalesAllocationRevisionId { get; private set; }
    public Guid SalesId { get; private set; }
    public Guid SalesDetailId { get; private set; }
    public int Sequence { get; private set; }
    public InventoryOrigin Origin { get; private set; }
    public Guid? ProcurementBatchId { get; private set; }
    public Guid? OutsourcedSupplyBatchId { get; private set; }
    public decimal AllocatedQuantity { get; private set; }
    public bool ManualOverride { get; private set; }
}
