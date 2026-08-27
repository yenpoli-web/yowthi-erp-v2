using Microsoft.EntityFrameworkCore;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Party;

internal static class PartyMappingExtensions
{
    public static void ApplyPartyMappings(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new SupplierConfiguration());
        modelBuilder.ApplyConfiguration(new FarmerConfiguration());
        modelBuilder.ApplyConfiguration(new EmployeeConfiguration());
        modelBuilder.ApplyConfiguration(new CustomerConfiguration());
        modelBuilder.ApplyConfiguration(new OutsourcedVendorConfiguration());
    }
}
