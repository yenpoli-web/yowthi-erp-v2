using Microsoft.EntityFrameworkCore;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Audit;

internal static class AuditMappingExtensions
{
    public static ModelBuilder ApplyAuditMappings(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AuditEventConfiguration());
        modelBuilder.ApplyConfiguration(new AuditEventSubjectConfiguration());
        modelBuilder.ApplyConfiguration(new CorrectionLinkConfiguration());
        return modelBuilder;
    }
}
