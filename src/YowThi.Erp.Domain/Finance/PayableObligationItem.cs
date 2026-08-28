namespace YowThi.Erp.Domain.Finance;

public sealed class PayableObligationItem
{
    public Guid Id { get; private set; }
    public Guid PayableId { get; private set; }
    public PayableKind PayableKind { get; private set; }
    public PayableObligationKind ObligationKind { get; private set; }
    public long AmountThb { get; private set; }
    public Guid? ProcurementEntryId { get; private set; }
    public Guid? OutsourcedSupplyDetailId { get; private set; }
    public Guid? EmployeeDailyWageId { get; private set; }
    public Guid? ProcurementBatchId { get; private set; }
    public decimal? AggregatedApplicableQuantity { get; private set; }
    public decimal? AppliedRatePerKg { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
}
