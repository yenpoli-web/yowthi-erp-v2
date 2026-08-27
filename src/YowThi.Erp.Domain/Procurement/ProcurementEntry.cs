using YowThi.Erp.Domain.Common;

namespace YowThi.Erp.Domain.Procurement;

public sealed class ProcurementEntry : IHasRowVersion
{
    public Guid Id { get; private set; }
    public Guid ProcurementBatchId { get; private set; }
    public ProcurementSourceType SourceType { get; private set; }
    public Guid? SupplierId { get; private set; }
    public Guid? FarmerId { get; private set; }
    public decimal NetQuantity { get; private set; }
    public string UnitCodeSnapshot { get; private set; } = null!;
    public decimal UnitPrice { get; private set; }
    public long AmountThb { get; private set; }
    public bool CompanyPickup { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public Guid RecordedByAccountId { get; private set; }
    public long RowVersion { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedByAccountId { get; private set; }
}
