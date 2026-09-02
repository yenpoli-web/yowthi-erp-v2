namespace YowThi.Erp.Domain.Finance;

public sealed class CompanyPickupTransportBasis
{
    public Guid Id { get; private set; }
    public Guid ProcurementEntryId { get; private set; }
    public decimal ApplicableQuantity { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    public static CompanyPickupTransportBasis Create(
        Guid id,
        Guid procurementEntryId,
        decimal applicableQuantity,
        DateTimeOffset recordedAt)
    {
        if (id == Guid.Empty || procurementEntryId == Guid.Empty)
        {
            throw new ArgumentException("Company Pickup transport basis IDs cannot be empty.");
        }

        return new CompanyPickupTransportBasis
        {
            Id = id,
            ProcurementEntryId = procurementEntryId,
            ApplicableQuantity = applicableQuantity,
            RecordedAt = recordedAt,
        };
    }
}
