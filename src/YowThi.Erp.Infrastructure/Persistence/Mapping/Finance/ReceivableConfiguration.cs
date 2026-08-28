using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Finance;
using YowThi.Erp.Domain.Sales;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Finance;

internal sealed class ReceivableConfiguration : IEntityTypeConfiguration<Receivable>
{
    public void Configure(EntityTypeBuilder<Receivable> builder)
    {
        builder.ToTable("receivables", "finance", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_receivables_row_version", "row_version >= 1");
        });

        builder.HasKey(x => x.Id).HasName("pk_receivables");
        builder.HasAlternateKey(x => new { x.Id, x.SalesId }).HasName("ak_receivables_id_sales");
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.SalesId).HasColumnName("sales_id").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(1L).IsConcurrencyToken();

        builder.HasOne<Sale>().WithMany().HasForeignKey(x => x.SalesId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_receivables_sales");
        builder.HasIndex(x => x.SalesId).IsUnique().HasDatabaseName("ux_receivables_sales_id");
    }
}
