using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Infrastructure;
using YowThi.Erp.Domain.Product;
using YowThi.Erp.Infrastructure.Persistence.System;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Product;

internal sealed class ProcurementProductConfiguration : IEntityTypeConfiguration<ProcurementProduct>
{
    public void Configure(EntityTypeBuilder<ProcurementProduct> builder)
    {
        builder.ToTable("procurement_products", "product", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_procurement_products_name_present", "NULLIF(btrim(name_zh_tw), '') IS NOT NULL OR NULLIF(btrim(name_th_th), '') IS NOT NULL");
            tableBuilder.HasCheckConstraint("ck_procurement_products_unit_code", "NULLIF(btrim(unit_code), '') IS NOT NULL");
            tableBuilder.HasCheckConstraint("ck_procurement_products_row_version", "row_version >= 1");
            tableBuilder.HasCheckConstraint("ck_procurement_products_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
        });

        builder.HasKey(x => x.Id).HasName("pk_procurement_products");
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.NameZhTw).HasColumnName("name_zh_tw").HasColumnType("text");
        builder.Property(x => x.NameThTh).HasColumnName("name_th_th").HasColumnType("text");
        builder.Property(x => x.UnitCode).HasColumnName("unit_code").HasColumnType("text").IsRequired();
        builder.Property(x => x.DefaultStorageLocationId).HasColumnName("default_storage_location_id");
        builder.Property(x => x.Active).HasColumnName("active").IsRequired();
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(1L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.CreatedByAccountId).HasColumnName("created_by_account_id").IsRequired();
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.DeletedByAccountId).HasColumnName("deleted_by_account_id");

        builder.HasOne<StorageLocation>().WithMany().HasForeignKey(x => x.DefaultStorageLocationId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_procurement_products_default_storage_location");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.CreatedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_procurement_products_created_by_account");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.DeletedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_procurement_products_deleted_by_account");
        builder.HasIndex(x => x.DefaultStorageLocationId).HasDatabaseName("ix_procurement_products_default_storage_location_id");
        builder.HasIndex(x => x.CreatedByAccountId).HasDatabaseName("ix_procurement_products_created_by_account_id");
        builder.HasIndex(x => x.DeletedByAccountId).HasDatabaseName("ix_procurement_products_deleted_by_account_id");
    }
}
