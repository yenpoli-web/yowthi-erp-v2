using Microsoft.EntityFrameworkCore;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Procurement;

internal static class ProcurementMappingExtensions
{
    public static ModelBuilder ApplyProcurementMappings(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ProcurementBatchConfiguration());
        modelBuilder.ApplyConfiguration(new ProcurementEntryConfiguration());
        return modelBuilder;
    }
}
