using Microsoft.EntityFrameworkCore;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Finance;

internal static class FinanceMappingExtensions
{
    public static ModelBuilder ApplyFinanceMappings(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PayableConfiguration());
        modelBuilder.ApplyConfiguration(new PayableObligationItemConfiguration());
        modelBuilder.ApplyConfiguration(new CompanyPickupTransportBasisConfiguration());
        modelBuilder.ApplyConfiguration(new CompanyPickupTransportObligationBasisItemConfiguration());
        modelBuilder.ApplyConfiguration(new PayableAdjustmentConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentConfiguration());
        modelBuilder.ApplyConfiguration(new ReceivableConfiguration());
        modelBuilder.ApplyConfiguration(new ReceivableObligationItemConfiguration());
        modelBuilder.ApplyConfiguration(new ReceiptConfiguration());
        modelBuilder.ApplyConfiguration(new PayableOutstandingPositionConfiguration());
        modelBuilder.ApplyConfiguration(new ReceivableOutstandingPositionConfiguration());
        return modelBuilder;
    }
}
