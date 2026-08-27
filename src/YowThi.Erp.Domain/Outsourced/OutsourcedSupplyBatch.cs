using YowThi.Erp.Domain.Common;

namespace YowThi.Erp.Domain.Outsourced;

public sealed class OutsourcedSupplyBatch : IHasRowVersion
{
    public Guid Id { get; private set; }
    public DateOnly SupplyDate { get; private set; }
    public Guid OutsourcedVendorId { get; private set; }
    public OutsourcedSupplyBatchLifecycleStatus LifecycleStatus { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public Guid? ClosedByAccountId { get; private set; }
    public long RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedByAccountId { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedByAccountId { get; private set; }
}
