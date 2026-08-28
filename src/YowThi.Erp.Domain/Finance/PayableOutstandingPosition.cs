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
}
