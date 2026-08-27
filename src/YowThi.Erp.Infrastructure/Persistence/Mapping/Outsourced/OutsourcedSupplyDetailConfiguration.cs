using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Outsourced;
using YowThi.Erp.Domain.Product;
using YowThi.Erp.Infrastructure.Persistence.System;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Outsourced;

internal sealed class OutsourcedSupplyDetailConfiguration : IEntityTypeConfiguration<OutsourcedSupplyDetail>
{
    public void Configure(EntityTypeBuilder<OutsourcedSupplyDetail> builder)
    {
        builder.ToTable("outsourced_supply_details", "outsourced", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_outsourced_supply_details_quantity", "quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND quantity >= 0");
            tableBuilder.HasCheckConstraint("ck_outsourced_supply_details_pricing_basis", "pricing_basis_snapshot IN ('WEIGHT_BASED_UNIT', 'UNIT_BASED')");
            tableBuilder.HasCheckConstraint("ck_outsourced_supply_details_pricing_shape", "(pricing_basis_snapshot = 'WEIGHT_BASED_UNIT' AND sales_weight_snapshot IS NOT NULL) OR (pricing_basis_snapshot = 'UNIT_BASED' AND sales_weight_snapshot IS NULL)");
            tableBuilder.HasCheckConstraint("ck_outsourced_supply_details_sales_weight", "sales_weight_snapshot IS NULL OR (sales_weight_snapshot NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND sales_weight_snapshot >= 0)");
            tableBuilder.HasCheckConstraint("ck_outsourced_supply_details_unit_price", "unit_price NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND unit_price >= 0");
            tableBuilder.HasCheckConstraint("ck_outsourced_supply_details_amount", "(pricing_basis_snapshot = 'WEIGHT_BASED_UNIT' AND amount_thb = floor(quantity * sales_weight_snapshot * unit_price)) OR (pricing_basis_snapshot = 'UNIT_BASED' AND amount_thb = floor(quantity * unit_price))");
            tableBuilder.HasCheckConstraint("ck_outsourced_supply_details_row_version", "row_version >= 1");
            tableBuilder.HasCheckConstraint("ck_outsourced_supply_details_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
        });

        builder.HasKey(x => x.Id).HasName("pk_outsourced_supply_details");

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.OutsourcedSupplyBatchId).HasColumnName("outsourced_supply_batch_id").IsRequired();
        builder.Property(x => x.SalesProductId).HasColumnName("sales_product_id").IsRequired();
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("numeric").IsRequired();
        builder.Property(x => x.PricingBasisSnapshot).HasColumnName("pricing_basis_snapshot").HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(x => x.SalesWeightSnapshot).HasColumnName("sales_weight_snapshot").HasColumnType("numeric");
        builder.Property(x => x.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric").IsRequired();
        builder.Property(x => x.AmountThb).HasColumnName("amount_thb").IsRequired();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.RecordedByAccountId).HasColumnName("recorded_by_account_id").IsRequired();
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(1L).IsConcurrencyToken();
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.DeletedByAccountId).HasColumnName("deleted_by_account_id");

        builder.HasOne<OutsourcedSupplyBatch>().WithMany().HasForeignKey(x => x.OutsourcedSupplyBatchId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_outsourced_supply_details_batch");
        builder.HasOne<SalesProduct>().WithMany().HasForeignKey(x => x.SalesProductId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_outsourced_supply_details_sales_product");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.RecordedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_outsourced_supply_details_recorded_by_account");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.DeletedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_outsourced_supply_details_deleted_by_account");

        builder.HasIndex(x => x.OutsourcedSupplyBatchId).HasDatabaseName("ix_outsourced_supply_details_batch_id");
        builder.HasIndex(x => x.SalesProductId).HasDatabaseName("ix_outsourced_supply_details_sales_product_id");
        builder.HasIndex(x => x.RecordedByAccountId).HasDatabaseName("ix_outsourced_supply_details_recorded_by_account_id");
        builder.HasIndex(x => x.DeletedByAccountId).HasDatabaseName("ix_outsourced_supply_details_deleted_by_account_id");
    }
}
