namespace YowThi.Erp.Domain.Finance;

public sealed class CompanyPickupTransportBasis
{
    public Guid Id { get; private set; }
    public Guid ProcurementEntryId { get; private set; }
    public decimal ApplicableQuantity { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
}
