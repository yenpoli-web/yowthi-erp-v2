using Microsoft.EntityFrameworkCore;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.System;

internal static class SystemMappingExtensions
{
    public static void ApplySystemMappings(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new SystemAccountRecordConfiguration());
        modelBuilder.ApplyConfiguration(new SystemAccountCapabilityGrantRecordConfiguration());
        modelBuilder.ApplyConfiguration(new CommandExecutionRecordConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageRecordConfiguration());
    }
}
