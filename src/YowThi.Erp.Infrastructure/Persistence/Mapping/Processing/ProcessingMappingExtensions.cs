using Microsoft.EntityFrameworkCore;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Processing;

internal static class ProcessingMappingExtensions
{
    public static ModelBuilder ApplyProcessingMappings(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ProcessingExecutionConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessingExecutionInputConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessingExecutionOutputConfiguration());
        return modelBuilder;
    }
}
