using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.ProcessingConfiguration;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.ProcessingConfig;

internal sealed class ProcessingRouteVersionConfiguration : IEntityTypeConfiguration<ProcessingRouteVersion>
{
    public void Configure(EntityTypeBuilder<ProcessingRouteVersion> builder)
    {
        builder.ToTable("processing_route_versions", "processing_config", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_processing_route_versions_version_number", "version_number > 0");
            tableBuilder.HasCheckConstraint("ck_processing_route_versions_status", "status IN ('DRAFT', 'VALIDATED', 'ACTIVE', 'RETIRED')");
        });

        builder.HasKey(x => x.Id).HasName("pk_processing_route_versions");
        builder.HasAlternateKey(x => new { x.Id, x.ProcessingRouteId }).HasName("ak_processing_route_versions_id_route");

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ProcessingRouteId).HasColumnName("processing_route_id").IsRequired();
        builder.Property(x => x.VersionNumber).HasColumnName("version_number").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasColumnType("text").IsRequired();

        builder.HasOne<ProcessingRoute>().WithMany().HasForeignKey(x => x.ProcessingRouteId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_processing_route_versions_route");
        builder.HasIndex(x => new { x.ProcessingRouteId, x.VersionNumber }).IsUnique().HasDatabaseName("ux_processing_route_versions_route_version_number");
        builder.HasIndex(x => x.ProcessingRouteId).IsUnique().HasFilter("status = 'ACTIVE'").HasDatabaseName("ux_processing_route_versions_one_active_per_route");
    }
}
