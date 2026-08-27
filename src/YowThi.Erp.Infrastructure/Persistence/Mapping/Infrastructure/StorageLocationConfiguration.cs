using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Infrastructure;
using YowThi.Erp.Infrastructure.Persistence.System;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Infrastructure;

internal sealed class StorageLocationConfiguration : IEntityTypeConfiguration<StorageLocation>
{
    public void Configure(EntityTypeBuilder<StorageLocation> builder)
    {
        builder.ToTable("storage_locations", "infrastructure", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_storage_locations_name_present", "NULLIF(btrim(name_zh_tw), '') IS NOT NULL OR NULLIF(btrim(name_th_th), '') IS NOT NULL");
            tableBuilder.HasCheckConstraint("ck_storage_locations_row_version", "row_version >= 1");
            tableBuilder.HasCheckConstraint("ck_storage_locations_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
        });

        builder.HasKey(x => x.Id).HasName("pk_storage_locations");
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.WarehouseId).HasColumnName("warehouse_id").IsRequired();
        builder.Property(x => x.Code).HasColumnName("code").HasColumnType("text");
        builder.Property(x => x.NameZhTw).HasColumnName("name_zh_tw").HasColumnType("text");
        builder.Property(x => x.NameThTh).HasColumnName("name_th_th").HasColumnType("text");
        builder.Property(x => x.Active).HasColumnName("active").IsRequired();
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(1L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.CreatedByAccountId).HasColumnName("created_by_account_id").IsRequired();
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.DeletedByAccountId).HasColumnName("deleted_by_account_id");

        builder.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_storage_locations_warehouse");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.CreatedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_storage_locations_created_by_account");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.DeletedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_storage_locations_deleted_by_account");
        builder.HasIndex(x => x.WarehouseId).HasDatabaseName("ix_storage_locations_warehouse_id");
        builder.HasIndex(x => x.CreatedByAccountId).HasDatabaseName("ix_storage_locations_created_by_account_id");
        builder.HasIndex(x => x.DeletedByAccountId).HasDatabaseName("ix_storage_locations_deleted_by_account_id");
    }
}
