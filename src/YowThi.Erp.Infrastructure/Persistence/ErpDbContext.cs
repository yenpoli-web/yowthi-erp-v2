using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Infrastructure.Persistence.Mapping.Party;
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
    }
}
