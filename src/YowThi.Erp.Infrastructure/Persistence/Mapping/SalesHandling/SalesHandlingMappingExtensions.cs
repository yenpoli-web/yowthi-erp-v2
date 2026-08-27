using Microsoft.EntityFrameworkCore;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.SalesHandling;

internal static class SalesHandlingMappingExtensions
{
    public static ModelBuilder ApplySalesHandlingMappings(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SalesPackagingItemConfiguration());
        modelBuilder.ApplyConfiguration(new SalesPackagingWorkRecordConfiguration());
        return modelBuilder;
    }
}
