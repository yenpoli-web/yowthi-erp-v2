using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Infrastructure;
using YowThi.Erp.Domain.Product;
using YowThi.Erp.Infrastructure.Persistence.System;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Product;

internal sealed class SalesProductConfiguration : IEntityTypeConfiguration<SalesProduct>
{
    public void Configure(EntityTypeBuilder<SalesProduct> builder)
    {
        builder.ToTable("sales_products", "product", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_sales_products_name_present", "NULLIF(btrim(name_zh_tw), '') IS NOT NULL OR NULLIF(btrim(name_th_th), '') IS NOT NULL");
            tableBuilder.HasCheckConstraint("ck_sales_products_pricing_basis", "pricing_basis IN ('WEIGHT_BASED_UNIT', 'UNIT_BASED')");
            tableBuilder.HasCheckConstraint("ck_sales_products_pricing_shape", "(pricing_basis = 'WEIGHT_BASED_UNIT' AND sales_weight IS NOT NULL) OR (pricing_basis = 'UNIT_BASED' AND sales_weight IS NULL)");
            tableBuilder.HasCheckConstraint("ck_sales_products_packaging_weight", "packaging_weight IS NULL OR (packaging_weight NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND packaging_weight > 0)");
            tableBuilder.HasCheckConstraint("ck_sales_products_sales_weight", "sales_weight IS NULL OR (sales_weight NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND sales_weight >= 0)");
            tableBuilder.HasCheckConstraint("ck_sales_products_row_version", "row_version >= 1");
            tableBuilder.HasCheckConstraint("ck_sales_products_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
        });

        builder.HasKey(x => x.Id).HasName("pk_sales_products");
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.SalesProductGroupId).HasColumnName("sales_product_group_id").IsRequired();
        builder.Property(x => x.NameZhTw).HasColumnName("name_zh_tw").HasColumnType("text");
        builder.Property(x => x.NameThTh).HasColumnName("name_th_th").HasColumnType("text");
        builder.Property(x => x.PricingBasis).HasColumnName("pricing_basis").HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(x => x.PackagingWeight).HasColumnName("packaging_weight").HasColumnType("numeric");
        builder.Property(x => x.SalesWeight).HasColumnName("sales_weight").HasColumnType("numeric");
        builder.Property(x => x.DefaultStorageLocationId).HasColumnName("default_storage_location_id");
        builder.Property(x => x.Active).HasColumnName("active").IsRequired();
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(1L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.CreatedByAccountId).HasColumnName("created_by_account_id").IsRequired();
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.DeletedByAccountId).HasColumnName("deleted_by_account_id");

        builder.HasOne<SalesProductGroup>().WithMany().HasForeignKey(x => x.SalesProductGroupId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sales_products_group");
        builder.HasOne<StorageLocation>().WithMany().HasForeignKey(x => x.DefaultStorageLocationId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sales_products_default_storage_location");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.CreatedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sales_products_created_by_account");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.DeletedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sales_products_deleted_by_account");
        builder.HasIndex(x => x.SalesProductGroupId).HasDatabaseName("ix_sales_products_group_id");
        builder.HasIndex(x => x.DefaultStorageLocationId).HasDatabaseName("ix_sales_products_default_storage_location_id");
        builder.HasIndex(x => x.CreatedByAccountId).HasDatabaseName("ix_sales_products_created_by_account_id");
        builder.HasIndex(x => x.DeletedByAccountId).HasDatabaseName("ix_sales_products_deleted_by_account_id");
    }
}
