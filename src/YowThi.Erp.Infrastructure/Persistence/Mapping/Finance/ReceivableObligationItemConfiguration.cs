using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Finance;
using YowThi.Erp.Domain.Sales;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Finance;

internal sealed class ReceivableObligationItemConfiguration : IEntityTypeConfiguration<ReceivableObligationItem>
{
    public void Configure(EntityTypeBuilder<ReceivableObligationItem> builder)
    {
        builder.ToTable("receivable_obligation_items", "finance", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_receivable_obligation_items_amount", "amount_thb >= 0");
        });

        builder.HasKey(x => x.Id).HasName("pk_receivable_obligation_items");
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ReceivableId).HasColumnName("receivable_id").IsRequired();
        builder.Property(x => x.SalesId).HasColumnName("sales_id").IsRequired();
        builder.Property(x => x.SalesDetailId).HasColumnName("sales_detail_id").IsRequired();
        builder.Property(x => x.AmountThb).HasColumnName("amount_thb").IsRequired();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").HasColumnType("timestamp with time zone").IsRequired();

        builder.HasOne<Receivable>().WithMany().HasForeignKey(x => new { x.ReceivableId, x.SalesId }).HasPrincipalKey(x => new { x.Id, x.SalesId }).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_receivable_obligation_items_receivable_sales");
        builder.HasOne<SalesDetail>().WithMany().HasForeignKey(x => new { x.SalesDetailId, x.SalesId }).HasPrincipalKey(x => new { x.Id, x.SalesId }).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_receivable_obligation_items_detail_sales");

        builder.HasIndex(x => x.SalesDetailId).IsUnique().HasDatabaseName("ux_receivable_obligation_items_sales_detail_id");
        builder.HasIndex(x => new { x.ReceivableId, x.SalesId }).HasDatabaseName("ix_receivable_obligation_items_receivable_sales");
        builder.HasIndex(x => new { x.SalesDetailId, x.SalesId }).HasDatabaseName("ix_receivable_obligation_items_detail_sales");
    }
}
