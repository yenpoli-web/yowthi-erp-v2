using Microsoft.EntityFrameworkCore;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Product;

internal static class ProductMappingExtensions
{
    public static ModelBuilder ApplyProductMappings(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ProcurementProductConfiguration());
        modelBuilder.ApplyConfiguration(new SalesProductGroupConfiguration());
        modelBuilder.ApplyConfiguration(new SalesProductConfiguration());
        return modelBuilder;
    }
}
