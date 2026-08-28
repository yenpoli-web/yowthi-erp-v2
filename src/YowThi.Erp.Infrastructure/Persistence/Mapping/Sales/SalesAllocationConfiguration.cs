using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Sales;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Sales;

internal sealed class SalesAllocationConfiguration : IEntityTypeConfiguration<SalesAllocation>
{
    public void Configure(EntityTypeBuilder<SalesAllocation> builder)
    {
        builder.ToTable("sales_allocations", "sales", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_sales_allocations_sequence", "sequence > 0");
            tableBuilder.HasCheckConstraint("ck_sales_allocations_row_version", "row_version >= 1");
        });

        builder.HasKey(x => new { x.SalesDetailId, x.Sequence }).HasName("pk_sales_allocations");

        builder.Property(x => x.SalesDetailId).HasColumnName("sales_detail_id").IsRequired();
        builder.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(x => x.SalesAllocationRevisionItemId).HasColumnName("sales_allocation_revision_item_id").IsRequired();
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(1L).IsConcurrencyToken();

        builder.HasOne<SalesDetail>().WithMany().HasForeignKey(x => x.SalesDetailId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sales_allocations_sales_detail");
        builder.HasOne<SalesAllocationRevisionItem>().WithMany().HasForeignKey(x => new { x.SalesAllocationRevisionItemId, x.SalesDetailId }).HasPrincipalKey(x => new { x.Id, x.SalesDetailId }).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sales_allocations_revision_item_detail");

        builder.HasIndex(x => new { x.SalesAllocationRevisionItemId, x.SalesDetailId }).HasDatabaseName("ix_sales_allocations_revision_item_detail");
        builder.HasIndex(x => x.SalesAllocationRevisionItemId).IsUnique().HasDatabaseName("ux_sales_allocations_revision_item_id");
    }
}
