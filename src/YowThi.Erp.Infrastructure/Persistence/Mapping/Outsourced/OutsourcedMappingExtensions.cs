using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Domain.Outsourced;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Outsourced;

internal static class OutsourcedMappingExtensions
{
    public static void ApplyOutsourcedMappings(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutsourcedSupplyBatchConfiguration());
        modelBuilder.ApplyConfiguration(new OutsourcedSupplyDetailConfiguration());
    }
}
