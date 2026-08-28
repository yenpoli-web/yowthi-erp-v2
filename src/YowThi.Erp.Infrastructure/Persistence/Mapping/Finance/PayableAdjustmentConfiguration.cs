using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Finance;
using YowThi.Erp.Infrastructure.Persistence.System;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Finance;

internal sealed class PayableAdjustmentConfiguration : IEntityTypeConfiguration<PayableAdjustment>
{
    public void Configure(EntityTypeBuilder<PayableAdjustment> builder)
    {
        builder.ToTable("payable_adjustments", "finance", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_payable_adjustments_type", "adjustment_type = 'SUPPLIER_QUALITY_WEIGHT_DEDUCTION'");
            tableBuilder.HasCheckConstraint("ck_payable_adjustments_amount_delta", "amount_delta_thb < 0");
        });

        builder.HasKey(x => x.Id).HasName("pk_payable_adjustments");
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.PayableId).HasColumnName("payable_id").IsRequired();
        builder.Property(x => x.AdjustmentType).HasColumnName("adjustment_type").HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(x => x.AmountDeltaThb).HasColumnName("amount_delta_thb").IsRequired();
        builder.Property(x => x.ReasonText).HasColumnName("reason_text").HasColumnType("text");
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.RecordedByAccountId).HasColumnName("recorded_by_account_id").IsRequired();

        builder.HasOne<Payable>().WithMany().HasForeignKey(x => x.PayableId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_payable_adjustments_payable");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.RecordedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_payable_adjustments_recorded_by_account");
        builder.HasIndex(x => x.PayableId).HasDatabaseName("ix_payable_adjustments_payable_id");
        builder.HasIndex(x => x.RecordedByAccountId).HasDatabaseName("ix_payable_adjustments_recorded_by_account_id");
    }
}
