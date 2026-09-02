using YowThi.Erp.Domain.Common;

namespace YowThi.Erp.Domain.Finance;

public sealed class ReceivableOutstandingPosition : IHasRowVersion
{
    public Guid ReceivableId { get; private set; }
    public long OriginalObligationThb { get; private set; }
    public long AdjustmentTotalThb { get; private set; }
    public long SettlementTotalThb { get; private set; }
    public long OutstandingThb { get; private set; }
    public long RowVersion { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ReceivableOutstandingPosition Create(
        Guid receivableId,
        long originalObligationThb,
        DateTimeOffset updatedAt)
    {
        if (receivableId == Guid.Empty)
        {
            throw new ArgumentException("Receivable ID cannot be empty.", nameof(receivableId));
        }

        if (originalObligationThb < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(originalObligationThb), "Original receivable obligation cannot be negative.");
        }

        return new ReceivableOutstandingPosition
        {
            ReceivableId = receivableId,
            OriginalObligationThb = originalObligationThb,
            AdjustmentTotalThb = 0,
            SettlementTotalThb = 0,
            OutstandingThb = originalObligationThb,
            RowVersion = 1,
            UpdatedAt = updatedAt,
        };
    }
}