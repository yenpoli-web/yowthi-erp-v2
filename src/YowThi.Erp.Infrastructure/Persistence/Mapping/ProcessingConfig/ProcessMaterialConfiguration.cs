using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Infrastructure;
using YowThi.Erp.Domain.ProcessingConfiguration;
using YowThi.Erp.Infrastructure.Persistence.System;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.ProcessingConfig;

internal sealed class ProcessMaterialConfiguration : IEntityTypeConfiguration<ProcessMaterial>
{
    public void Configure(EntityTypeBuilder<ProcessMaterial> builder)
    {
        builder.ToTable("process_materials", "processing_config", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_process_materials_name_present", "NULLIF(btrim(name_zh_tw), '') IS NOT NULL OR NULLIF(btrim(name_th_th), '') IS NOT NULL");
            tableBuilder.HasCheckConstraint("ck_process_materials_container_shape", "(uses_container AND container_id IS NOT NULL AND default_container_count IS NOT NULL) OR (NOT uses_container AND container_id IS NULL AND default_container_count IS NULL)");
            tableBuilder.HasCheckConstraint("ck_process_materials_container_count", "default_container_count IS NULL OR default_container_count >= 0");
            tableBuilder.HasCheckConstraint("ck_process_materials_row_version", "row_version >= 1");
            tableBuilder.HasCheckConstraint("ck_process_materials_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
        });

        builder.HasKey(x => x.Id).HasName("pk_process_materials");
        builder.HasAlternateKey(x => new { x.Id, x.ProcessingRouteVersionId }).HasName("ak_process_materials_id_route_version");

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ProcessingRouteVersionId).HasColumnName("processing_route_version_id").IsRequired();
        builder.Property(x => x.NameZhTw).HasColumnName("name_zh_tw").HasColumnType("text");
        builder.Property(x => x.NameThTh).HasColumnName("name_th_th").HasColumnType("text");
        builder.Property(x => x.UsesContainer).HasColumnName("uses_container").IsRequired();
        builder.Property(x => x.ContainerId).HasColumnName("container_id");
        builder.Property(x => x.DefaultContainerCount).HasColumnName("default_container_count");
        builder.Property(x => x.DefaultStorageLocationId).HasColumnName("default_storage_location_id");
        builder.Property(x => x.Active).HasColumnName("active").IsRequired();
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(1L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.CreatedByAccountId).HasColumnName("created_by_account_id").IsRequired();
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.DeletedByAccountId).HasColumnName("deleted_by_account_id");

        builder.HasOne<ProcessingRouteVersion>().WithMany().HasForeignKey(x => x.ProcessingRouteVersionId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_process_materials_route_version");
        builder.HasOne<Container>().WithMany().HasForeignKey(x => x.ContainerId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_process_materials_container");
        builder.HasOne<StorageLocation>().WithMany().HasForeignKey(x => x.DefaultStorageLocationId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_process_materials_default_storage_location");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.CreatedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_process_materials_created_by_account");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.DeletedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_process_materials_deleted_by_account");
        builder.HasIndex(x => x.ProcessingRouteVersionId).HasDatabaseName("ix_process_materials_route_version_id");
        builder.HasIndex(x => x.ContainerId).HasDatabaseName("ix_process_materials_container_id");
        builder.HasIndex(x => x.DefaultStorageLocationId).HasDatabaseName("ix_process_materials_default_storage_location_id");
        builder.HasIndex(x => x.CreatedByAccountId).HasDatabaseName("ix_process_materials_created_by_account_id");
        builder.HasIndex(x => x.DeletedByAccountId).HasDatabaseName("ix_process_materials_deleted_by_account_id");
    }
}
