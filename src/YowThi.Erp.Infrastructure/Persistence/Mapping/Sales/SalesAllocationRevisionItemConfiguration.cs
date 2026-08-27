using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Outsourced;
using YowThi.Erp.Domain.Procurement;
using YowThi.Erp.Domain.Sales;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Sales;

internal sealed class SalesAllocationRevisionItemConfiguration : IEntityTypeConfiguration<SalesAllocationRevisionItem>
{
    public void Configure(EntityTypeBuilder<SalesAllocationRevisionItem> builder)
    {
        builder.ToTable("sales_allocation_revision_items", "sales", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_sales_allocation_revision_items_sequence", "sequence > 0");
            tableBuilder.HasCheckConstraint("ck_sales_allocation_revision_items_origin", "origin IN ('IN_HOUSE', 'OUTSOURCED')");
            tableBuilder.HasCheckConstraint("ck_sales_allocation_revision_items_typed_source_batch", "(origin = 'IN_HOUSE' AND procurement_batch_id IS NOT NULL AND outsourced_supply_batch_id IS NULL) OR (origin = 'OUTSOURCED' AND procurement_batch_id IS NULL AND outsourced_supply_batch_id IS NOT NULL)");
            tableBuilder.HasCheckConstraint("ck_sales_allocation_revision_items_allocated_quantity", "allocated_quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND allocated_quantity >= 0");
        });

        builder.HasKey(x => x.Id).HasName("pk_sales_allocation_revision_items");
        builder.HasAlternateKey(x => new { x.Id, x.SalesDetailId }).HasName("ak_sales_allocation_revision_items_id_sales_detail");

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.SalesAllocationRevisionId).HasColumnName("sales_allocation_revision_id").IsRequired();
        builder.Property(x => x.SalesId).HasColumnName("sales_id").IsRequired();
        builder.Property(x => x.SalesDetailId).HasColumnName("sales_detail_id").IsRequired();
        builder.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(x => x.Origin).HasColumnName("origin").HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(x => x.ProcurementBatchId).HasColumnName("procurement_batch_id");
        builder.Property(x => x.OutsourcedSupplyBatchId).HasColumnName("outsourced_supply_batch_id");
        builder.Property(x => x.AllocatedQuantity).HasColumnName("allocated_quantity").HasColumnType("numeric").IsRequired();
        builder.Property(x => x.ManualOverride).HasColumnName("manual_override").IsRequired();

        builder.HasOne<Sale>().WithMany().HasForeignKey(x => x.SalesId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sales_allocation_revision_items_sales");
        builder.HasOne<SalesAllocationRevision>().WithMany().HasForeignKey(x => new { x.SalesAllocationRevisionId, x.SalesId }).HasPrincipalKey(x => new { x.Id, x.SalesId }).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sales_allocation_revision_items_revision_sales");
        builder.HasOne<SalesDetail>().WithMany().HasForeignKey(x => new { x.SalesDetailId, x.SalesId }).HasPrincipalKey(x => new { x.Id, x.SalesId }).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sales_allocation_revision_items_detail_sales");
        builder.HasOne<ProcurementBatch>().WithMany().HasForeignKey(x => x.ProcurementBatchId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sales_allocation_revision_items_procurement_batch");
        builder.HasOne<OutsourcedSupplyBatch>().WithMany().HasForeignKey(x => x.OutsourcedSupplyBatchId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sales_allocation_revision_items_outsourced_batch");

        builder.HasIndex(x => new { x.SalesAllocationRevisionId, x.SalesDetailId, x.Sequence }).IsUnique().HasDatabaseName("ux_sales_allocation_revision_items_revision_detail_sequence");
        builder.HasIndex(x => x.SalesId).HasDatabaseName("ix_sales_allocation_revision_items_sales_id");
        builder.HasIndex(x => new { x.SalesAllocationRevisionId, x.SalesId }).HasDatabaseName("ix_sales_allocation_revision_items_revision_sales");
        builder.HasIndex(x => new { x.SalesDetailId, x.SalesId }).HasDatabaseName("ix_sales_allocation_revision_items_detail_sales");
        builder.HasIndex(x => x.ProcurementBatchId).HasDatabaseName("ix_sales_allocation_revision_items_procurement_batch_id");
        builder.HasIndex(x => x.OutsourcedSupplyBatchId).HasDatabaseName("ix_sales_allocation_revision_items_outsourced_batch_id");
    }
}
