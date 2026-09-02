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

    public static SalesAllocationRevisionItem Create(
        Guid id,
        Guid salesAllocationRevisionId,
        Guid salesId,
        Guid salesDetailId,
        int sequence,
        InventoryOrigin origin,
        Guid? procurementBatchId,
        Guid? outsourcedSupplyBatchId,
        decimal allocatedQuantity,
        bool manualOverride)
    {
        if (id == Guid.Empty
            || salesAllocationRevisionId == Guid.Empty
            || salesId == Guid.Empty
            || salesDetailId == Guid.Empty)
        {
            throw new ArgumentException("Sales Allocation Revision Item technical and reference IDs cannot be empty.");
        }

        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Sales allocation sequence must be positive.");
        }

        if (allocatedQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(allocatedQuantity), "Allocated quantity cannot be negative.");
        }

        var sourceShapeIsValid = origin switch
        {
            InventoryOrigin.IN_HOUSE => procurementBatchId is { } procurementId
                && procurementId != Guid.Empty
                && outsourcedSupplyBatchId is null,
            InventoryOrigin.OUTSOURCED => outsourcedSupplyBatchId is { } outsourcedId
                && outsourcedId != Guid.Empty
                && procurementBatchId is null,
            _ => false,
        };

        if (!sourceShapeIsValid)
        {
            throw new ArgumentException("Sales allocation source batch shape is invalid.");
        }

        return new SalesAllocationRevisionItem
        {
            Id = id,
            SalesAllocationRevisionId = salesAllocationRevisionId,
            SalesId = salesId,
            SalesDetailId = salesDetailId,
            Sequence = sequence,
            Origin = origin,
            ProcurementBatchId = procurementBatchId,
            OutsourcedSupplyBatchId = outsourcedSupplyBatchId,
            AllocatedQuantity = allocatedQuantity,
            ManualOverride = manualOverride,
        };
    }
}