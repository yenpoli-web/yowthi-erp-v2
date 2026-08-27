using Microsoft.EntityFrameworkCore;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Labor;

internal static class LaborMappingExtensions
{
    public static ModelBuilder ApplyLaborMappings(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new EmployeeDailyWageConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessingWageComponentConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessingWageComponentSourceConfiguration());
        modelBuilder.ApplyConfiguration(new SalesPackagingWageComponentConfiguration());
        return modelBuilder;
    }
}
