using YowThi.Erp.Domain.Common;

namespace YowThi.Erp.Domain.Sales;

public sealed class SalesAllocation : IHasRowVersion
{
    public Guid SalesDetailId { get; private set; }
    public int Sequence { get; private set; }
    public Guid SalesAllocationRevisionItemId { get; private set; }
    public long RowVersion { get; private set; }
}
