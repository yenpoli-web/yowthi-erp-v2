using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Infrastructure;
using YowThi.Erp.Domain.ProcessingConfiguration;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.ProcessingConfig;

internal sealed class RouteInputConfigConfiguration : IEntityTypeConfiguration<RouteInputConfig>
{
    public void Configure(EntityTypeBuilder<RouteInputConfig> builder)
    {
        builder.ToTable("route_input_configs", "processing_config", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_route_input_configs_container_shape", "(uses_container AND container_id IS NOT NULL AND default_container_count IS NOT NULL) OR (NOT uses_container AND container_id IS NULL AND default_container_count IS NULL)");
            tableBuilder.HasCheckConstraint("ck_route_input_configs_container_count", "default_container_count IS NULL OR default_container_count >= 0");
        });

        builder.HasKey(x => x.ProcessingRouteVersionId).HasName("pk_route_input_configs");
        builder.Property(x => x.ProcessingRouteVersionId).HasColumnName("processing_route_version_id").ValueGeneratedNever();
        builder.Property(x => x.UsesContainer).HasColumnName("uses_container").IsRequired();
        builder.Property(x => x.ContainerId).HasColumnName("container_id");
        builder.Property(x => x.DefaultContainerCount).HasColumnName("default_container_count");
        builder.Property(x => x.DefaultStorageLocationId).HasColumnName("default_storage_location_id");

        builder.HasOne<ProcessingRouteVersion>().WithMany().HasForeignKey(x => x.ProcessingRouteVersionId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_route_input_configs_route_version");
        builder.HasOne<Container>().WithMany().HasForeignKey(x => x.ContainerId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_route_input_configs_container");
        builder.HasOne<StorageLocation>().WithMany().HasForeignKey(x => x.DefaultStorageLocationId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_route_input_configs_default_storage_location");
        builder.HasIndex(x => x.ContainerId).HasDatabaseName("ix_route_input_configs_container_id");
        builder.HasIndex(x => x.DefaultStorageLocationId).HasDatabaseName("ix_route_input_configs_default_storage_location_id");
    }
}
