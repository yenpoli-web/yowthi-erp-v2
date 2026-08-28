using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Finance;
using YowThi.Erp.Infrastructure.Persistence.System;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Finance;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", "finance", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_payments_amount", "amount_thb > 0");
        });

        builder.HasKey(x => x.Id).HasName("pk_payments");
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.PayableId).HasColumnName("payable_id").IsRequired();
        builder.Property(x => x.AmountThb).HasColumnName("amount_thb").IsRequired();
        builder.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.ConfirmedByAccountId).HasColumnName("confirmed_by_account_id").IsRequired();

        builder.HasOne<Payable>().WithMany().HasForeignKey(x => x.PayableId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_payments_payable");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.ConfirmedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_payments_confirmed_by_account");
        builder.HasIndex(x => x.PayableId).HasDatabaseName("ix_payments_payable_id");
        builder.HasIndex(x => x.ConfirmedByAccountId).HasDatabaseName("ix_payments_confirmed_by_account_id");
    }
}
