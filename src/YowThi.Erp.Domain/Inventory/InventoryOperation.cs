namespace YowThi.Erp.Domain.Inventory;

public sealed class InventoryOperation
{
    public Guid Id { get; private set; }
    public InventoryOperationType OperationType { get; private set; }
    public Guid? ProcurementEntryId { get; private set; }
    public Guid? ProcessingExecutionId { get; private set; }
    public Guid? OutsourcedSupplyDetailId { get; private set; }
    public Guid? SalesId { get; private set; }
    public Guid? SalesAllocationRevisionId { get; private set; }
    public Guid? ProcurementBatchId { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public Guid RecordedByAccountId { get; private set; }
}
