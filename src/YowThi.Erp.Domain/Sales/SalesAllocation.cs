using YowThi.Erp.Domain.Common;

namespace YowThi.Erp.Domain.Sales;

public sealed class SalesAllocation : IHasRowVersion
{
    public Guid SalesDetailId { get; private set; }
    public int Sequence { get; private set; }
    public Guid SalesAllocationRevisionItemId { get; private set; }
    public long RowVersion { get; private set; }

    public static SalesAllocation Create(
        Guid salesDetailId,
        int sequence,
        Guid salesAllocationRevisionItemId)
    {
        if (salesDetailId == Guid.Empty || salesAllocationRevisionItemId == Guid.Empty)
        {
            throw new ArgumentException("Sales Allocation reference IDs cannot be empty.");
        }

        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Sales allocation sequence must be positive.");
        }

        return new SalesAllocation
        {
            SalesDetailId = salesDetailId,
            Sequence = sequence,
            SalesAllocationRevisionItemId = salesAllocationRevisionItemId,
            RowVersion = 1,
        };
    }
}