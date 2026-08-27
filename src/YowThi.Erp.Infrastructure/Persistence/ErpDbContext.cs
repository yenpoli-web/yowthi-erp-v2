using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Infrastructure.Persistence.Mapping.Infrastructure;
using YowThi.Erp.Infrastructure.Persistence.Mapping.Inventory;
using YowThi.Erp.Infrastructure.Persistence.Mapping.Labor;
using YowThi.Erp.Infrastructure.Persistence.Mapping.Outsourced;
using YowThi.Erp.Infrastructure.Persistence.Mapping.Party;
using YowThi.Erp.Infrastructure.Persistence.Mapping.Processing;
using YowThi.Erp.Infrastructure.Persistence.Mapping.ProcessingConfig;
using YowThi.Erp.Infrastructure.Persistence.Mapping.Procurement;
using YowThi.Erp.Infrastructure.Persistence.Mapping.Product;
using YowThi.Erp.Infrastructure.Persistence.Mapping.Sales;
using YowThi.Erp.Infrastructure.Persistence.Mapping.SalesHandling;
using YowThi.Erp.Infrastructure.Persistence.Mapping.System;

namespace YowThi.Erp.Infrastructure.Persistence;

public sealed class ErpDbContext(DbContextOptions<ErpDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplySystemMappings();
        modelBuilder.ApplyPartyMappings();
        modelBuilder.ApplyInfrastructureMappings();
        modelBuilder.ApplyProductMappings();
        modelBuilder.ApplyProcessingConfigMappings();
        modelBuilder.ApplyProcurementMappings();
        modelBuilder.ApplyProcessingMappings();
        modelBuilder.ApplyOutsourcedMappings();
        modelBuilder.ApplySalesMappings();
        modelBuilder.ApplyInventoryMappings();
        modelBuilder.ApplySalesHandlingMappings();
        modelBuilder.ApplyLaborMappings();
    }
}
