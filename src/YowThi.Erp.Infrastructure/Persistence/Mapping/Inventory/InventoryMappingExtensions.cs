using Microsoft.EntityFrameworkCore;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Inventory;

internal static class InventoryMappingExtensions
{
    public static void ApplyInventoryMappings(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new InventoryOperationConfiguration());
        modelBuilder.ApplyConfiguration(new InventoryMovementConfiguration());
        modelBuilder.ApplyConfiguration(new InventoryPositionConfiguration());
    }
}
