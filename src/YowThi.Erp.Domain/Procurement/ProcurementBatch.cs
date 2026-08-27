using YowThi.Erp.Domain.Common;

namespace YowThi.Erp.Domain.Procurement;

public sealed class ProcurementBatch : IHasRowVersion
{
    public Guid Id { get; private set; }
    public DateOnly ProcurementDate { get; private set; }
    public Guid ProcurementProductId { get; private set; }
    public ProcurementStatus ProcurementStatus { get; private set; }
    public ProcurementBatchLifecycleStatus LifecycleStatus { get; private set; }
    public Guid? ProcessingRouteId { get; private set; }
    public Guid? ProcessingRouteVersionId { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public Guid? CompletedByAccountId { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public Guid? ClosedByAccountId { get; private set; }
    public long RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedByAccountId { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedByAccountId { get; private set; }
}
