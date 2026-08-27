using Microsoft.EntityFrameworkCore;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.ProcessingConfig;

internal static class ProcessingConfigMappingExtensions
{
    public static ModelBuilder ApplyProcessingConfigMappings(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ProcessingRouteConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessingRouteVersionConfiguration());
        modelBuilder.ApplyConfiguration(new RouteInputConfigConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessMaterialConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessingModuleConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessingModuleOutputConfiguration());
        return modelBuilder;
    }
}
