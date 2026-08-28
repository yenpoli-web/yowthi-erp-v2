namespace YowThi.Erp.Domain.Finance;

public sealed class PayableAdjustment
{
    public Guid Id { get; private set; }
    public Guid PayableId { get; private set; }
    public PayableAdjustmentType AdjustmentType { get; private set; }
    public long AmountDeltaThb { get; private set; }
    public string? ReasonText { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public Guid RecordedByAccountId { get; private set; }
}
