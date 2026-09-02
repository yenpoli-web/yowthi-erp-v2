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

    public static ProcurementEntry Create(
        Guid id,
        Guid procurementBatchId,
        ProcurementSourceType sourceType,
        Guid? supplierId,
        Guid? farmerId,
        decimal netQuantity,
        string unitCodeSnapshot,
        decimal unitPrice,
        long amountThb,
        bool companyPickup,
        DateTimeOffset recordedAt,
        Guid recordedByAccountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unitCodeSnapshot);

        if (id == Guid.Empty || procurementBatchId == Guid.Empty || recordedByAccountId == Guid.Empty)
        {
            throw new ArgumentException("Procurement Entry technical and reference IDs cannot be empty.");
        }

        return new ProcurementEntry
        {
            Id = id,
            ProcurementBatchId = procurementBatchId,
            SourceType = sourceType,
            SupplierId = supplierId,
            FarmerId = farmerId,
            NetQuantity = netQuantity,
            UnitCodeSnapshot = unitCodeSnapshot,
            UnitPrice = unitPrice,
            AmountThb = amountThb,
            CompanyPickup = companyPickup,
            RecordedAt = recordedAt,
            RecordedByAccountId = recordedByAccountId,
            RowVersion = 1,
        };
    }
}
