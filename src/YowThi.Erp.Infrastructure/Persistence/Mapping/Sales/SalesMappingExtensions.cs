using Microsoft.EntityFrameworkCore;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Sales;

internal static class SalesMappingExtensions
{
    public static void ApplySalesMappings(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SaleConfiguration());
        modelBuilder.ApplyConfiguration(new SalesDetailConfiguration());
        modelBuilder.ApplyConfiguration(new SalesAllocationRevisionConfiguration());
        modelBuilder.ApplyConfiguration(new SalesAllocationRevisionItemConfiguration());
        modelBuilder.ApplyConfiguration(new SalesAllocationConfiguration());
    }
}
