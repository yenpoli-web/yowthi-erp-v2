using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Finance;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Finance;

internal sealed class PayableOutstandingPositionConfiguration : IEntityTypeConfiguration<PayableOutstandingPosition>
{
    public void Configure(EntityTypeBuilder<PayableOutstandingPosition> builder)
    {
        builder.ToTable("payable_outstanding_positions", "finance", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_payable_outstanding_positions_original", "original_obligation_thb >= 0");
            tableBuilder.HasCheckConstraint("ck_payable_outstanding_positions_settlement", "settlement_total_thb >= 0");
            tableBuilder.HasCheckConstraint("ck_payable_outstanding_positions_formula", "outstanding_thb = original_obligation_thb + adjustment_total_thb - settlement_total_thb");
            tableBuilder.HasCheckConstraint("ck_payable_outstanding_positions_row_version", "row_version >= 1");
        });

        builder.HasKey(x => x.PayableId).HasName("pk_payable_outstanding_positions");
        builder.Property(x => x.PayableId).HasColumnName("payable_id").ValueGeneratedNever();
        builder.Property(x => x.OriginalObligationThb).HasColumnName("original_obligation_thb").IsRequired();
        builder.Property(x => x.AdjustmentTotalThb).HasColumnName("adjustment_total_thb").IsRequired();
        builder.Property(x => x.SettlementTotalThb).HasColumnName("settlement_total_thb").IsRequired();
        builder.Property(x => x.OutstandingThb).HasColumnName("outstanding_thb").IsRequired();
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(1L).IsConcurrencyToken();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired();

        builder.HasOne<Payable>().WithOne().HasForeignKey<PayableOutstandingPosition>(x => x.PayableId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_payable_outstanding_positions_payable");
    }
}
