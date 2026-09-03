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

    public static PayableAdjustment Create(
        Guid id,
        Guid payableId,
        PayableAdjustmentType adjustmentType,
        long amountDeltaThb,
        string? reasonText,
        DateTimeOffset recordedAt,
        Guid recordedByAccountId)
    {
        if (id == Guid.Empty
            || payableId == Guid.Empty
            || recordedByAccountId == Guid.Empty
            || adjustmentType != PayableAdjustmentType.SUPPLIER_QUALITY_WEIGHT_DEDUCTION
            || amountDeltaThb >= 0)
        {
            throw new ArgumentException("Payable Adjustment is invalid.");
        }

        return new PayableAdjustment
        {
            Id = id,
            PayableId = payableId,
            AdjustmentType = adjustmentType,
            AmountDeltaThb = amountDeltaThb,
            ReasonText = reasonText,
            RecordedAt = recordedAt,
            RecordedByAccountId = recordedByAccountId,
        };
    }
}
