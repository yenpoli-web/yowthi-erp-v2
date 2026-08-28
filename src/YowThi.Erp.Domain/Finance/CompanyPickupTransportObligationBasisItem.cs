namespace YowThi.Erp.Domain.Finance;

public sealed class CompanyPickupTransportObligationBasisItem
{
    public Guid TransportObligationItemId { get; private set; }
    public Guid TransportBasisId { get; private set; }
    public decimal AppliedQuantity { get; private set; }
}
