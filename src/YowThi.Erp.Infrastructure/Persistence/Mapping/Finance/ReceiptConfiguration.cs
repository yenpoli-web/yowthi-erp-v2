using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Finance;
using YowThi.Erp.Infrastructure.Persistence.System;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Finance;

internal sealed class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        builder.ToTable("receipts", "finance", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_receipts_amount", "amount_thb > 0");
        });

        builder.HasKey(x => x.Id).HasName("pk_receipts");
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ReceivableId).HasColumnName("receivable_id").IsRequired();
        builder.Property(x => x.AmountThb).HasColumnName("amount_thb").IsRequired();
        builder.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.ConfirmedByAccountId).HasColumnName("confirmed_by_account_id").IsRequired();

        builder.HasOne<Receivable>().WithMany().HasForeignKey(x => x.ReceivableId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_receipts_receivable");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.ConfirmedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_receipts_confirmed_by_account");
        builder.HasIndex(x => x.ReceivableId).HasDatabaseName("ix_receipts_receivable_id");
        builder.HasIndex(x => x.ConfirmedByAccountId).HasDatabaseName("ix_receipts_confirmed_by_account_id");
    }
}
