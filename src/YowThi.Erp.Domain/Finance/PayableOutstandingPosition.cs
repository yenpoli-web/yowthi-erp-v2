using YowThi.Erp.Domain.Common;

namespace YowThi.Erp.Domain.Finance;

public sealed class PayableOutstandingPosition : IHasRowVersion
{
    public Guid PayableId { get; private set; }
    public long OriginalObligationThb { get; private set; }
    public long AdjustmentTotalThb { get; private set; }
    public long SettlementTotalThb { get; private set; }
    public long OutstandingThb { get; private set; }
    public long RowVersion { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static PayableOutstandingPosition Create(
        Guid payableId,
        long originalObligationThb,
        DateTimeOffset updatedAt)
    {
        if (payableId == Guid.Empty || originalObligationThb < 0)
        {
            throw new ArgumentException("Payable Outstanding Position is invalid.");
        }

        return new PayableOutstandingPosition
        {
            PayableId = payableId,
            OriginalObligationThb = originalObligationThb,
            AdjustmentTotalThb = 0,
            SettlementTotalThb = 0,
            OutstandingThb = originalObligationThb,
            RowVersion = 1,
            UpdatedAt = updatedAt,
        };
    }
}
