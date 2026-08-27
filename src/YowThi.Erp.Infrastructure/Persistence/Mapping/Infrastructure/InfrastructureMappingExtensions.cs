using Microsoft.EntityFrameworkCore;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Infrastructure;

internal static class InfrastructureMappingExtensions
{
    public static ModelBuilder ApplyInfrastructureMappings(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ContainerConfiguration());
        modelBuilder.ApplyConfiguration(new WarehouseConfiguration());
        modelBuilder.ApplyConfiguration(new StorageLocationConfiguration());
        return modelBuilder;
    }
}
