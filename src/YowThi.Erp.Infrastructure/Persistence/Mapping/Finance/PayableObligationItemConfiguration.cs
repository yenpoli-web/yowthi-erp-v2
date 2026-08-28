using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Finance;
using YowThi.Erp.Domain.Labor;
using YowThi.Erp.Domain.Outsourced;
using YowThi.Erp.Domain.Procurement;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Finance;

internal sealed class PayableObligationItemConfiguration : IEntityTypeConfiguration<PayableObligationItem>
{
    public void Configure(EntityTypeBuilder<PayableObligationItem> builder)
    {
        builder.ToTable("payable_obligation_items", "finance", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_payable_obligation_items_kind", "obligation_kind IN ('PROCUREMENT_ENTRY', 'OUTSOURCED_SUPPLY_DETAIL', 'EMPLOYEE_DAILY_WAGE', 'COMPANY_PICKUP_TRANSPORT')");
            tableBuilder.HasCheckConstraint("ck_payable_obligation_items_amount", "amount_thb >= 0");
            tableBuilder.HasCheckConstraint("ck_payable_obligation_items_typed_source", "(obligation_kind = 'PROCUREMENT_ENTRY' AND payable_kind IN ('PROCUREMENT_SUPPLIER', 'PROCUREMENT_FARMER') AND procurement_entry_id IS NOT NULL AND outsourced_supply_detail_id IS NULL AND employee_daily_wage_id IS NULL AND procurement_batch_id IS NULL AND aggregated_applicable_quantity IS NULL AND applied_rate_per_kg IS NULL) OR (obligation_kind = 'OUTSOURCED_SUPPLY_DETAIL' AND payable_kind = 'OUTSOURCED_VENDOR' AND procurement_entry_id IS NULL AND outsourced_supply_detail_id IS NOT NULL AND employee_daily_wage_id IS NULL AND procurement_batch_id IS NULL AND aggregated_applicable_quantity IS NULL AND applied_rate_per_kg IS NULL) OR (obligation_kind = 'EMPLOYEE_DAILY_WAGE' AND payable_kind = 'EMPLOYEE_DAILY_WAGE' AND procurement_entry_id IS NULL AND outsourced_supply_detail_id IS NULL AND employee_daily_wage_id IS NOT NULL AND procurement_batch_id IS NULL AND aggregated_applicable_quantity IS NULL AND applied_rate_per_kg IS NULL) OR (obligation_kind = 'COMPANY_PICKUP_TRANSPORT' AND payable_kind = 'COMPANY_PICKUP_TRANSPORT' AND procurement_entry_id IS NULL AND outsourced_supply_detail_id IS NULL AND employee_daily_wage_id IS NULL AND procurement_batch_id IS NOT NULL AND aggregated_applicable_quantity IS NOT NULL AND applied_rate_per_kg IS NOT NULL)");
            tableBuilder.HasCheckConstraint("ck_payable_obligation_items_transport_quantity", "aggregated_applicable_quantity IS NULL OR (aggregated_applicable_quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND aggregated_applicable_quantity >= 0)");
            tableBuilder.HasCheckConstraint("ck_payable_obligation_items_transport_rate", "applied_rate_per_kg IS NULL OR (applied_rate_per_kg NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND applied_rate_per_kg >= 0)");
        });

        builder.HasKey(x => x.Id).HasName("pk_payable_obligation_items");

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.PayableId).HasColumnName("payable_id").IsRequired();
        builder.Property(x => x.PayableKind).HasColumnName("payable_kind").HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(x => x.ObligationKind).HasColumnName("obligation_kind").HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(x => x.AmountThb).HasColumnName("amount_thb").IsRequired();
        builder.Property(x => x.ProcurementEntryId).HasColumnName("procurement_entry_id");
        builder.Property(x => x.OutsourcedSupplyDetailId).HasColumnName("outsourced_supply_detail_id");
        builder.Property(x => x.EmployeeDailyWageId).HasColumnName("employee_daily_wage_id");
        builder.Property(x => x.ProcurementBatchId).HasColumnName("procurement_batch_id");
        builder.Property(x => x.AggregatedApplicableQuantity).HasColumnName("aggregated_applicable_quantity").HasColumnType("numeric");
        builder.Property(x => x.AppliedRatePerKg).HasColumnName("applied_rate_per_kg").HasColumnType("numeric");
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").HasColumnType("timestamp with time zone").IsRequired();

        builder.HasOne<Payable>().WithMany().HasForeignKey(x => new { x.PayableId, x.PayableKind }).HasPrincipalKey(x => new { x.Id, x.PayableKind }).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_payable_obligation_items_payable_kind");
        builder.HasOne<ProcurementEntry>().WithMany().HasForeignKey(x => x.ProcurementEntryId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_payable_obligation_items_procurement_entry");
        builder.HasOne<OutsourcedSupplyDetail>().WithMany().HasForeignKey(x => x.OutsourcedSupplyDetailId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_payable_obligation_items_outsourced_supply_detail");
        builder.HasOne<EmployeeDailyWage>().WithMany().HasForeignKey(x => x.EmployeeDailyWageId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_payable_obligation_items_employee_daily_wage");
        builder.HasOne<ProcurementBatch>().WithMany().HasForeignKey(x => x.ProcurementBatchId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_payable_obligation_items_procurement_batch");

        builder.HasIndex(x => x.ProcurementEntryId).IsUnique().HasFilter("obligation_kind = 'PROCUREMENT_ENTRY'").HasDatabaseName("ux_payable_obligation_items_procurement_entry");
        builder.HasIndex(x => x.OutsourcedSupplyDetailId).IsUnique().HasFilter("obligation_kind = 'OUTSOURCED_SUPPLY_DETAIL'").HasDatabaseName("ux_payable_obligation_items_outsourced_supply_detail");
        builder.HasIndex(x => x.EmployeeDailyWageId).IsUnique().HasFilter("obligation_kind = 'EMPLOYEE_DAILY_WAGE'").HasDatabaseName("ux_payable_obligation_items_employee_daily_wage");
        builder.HasIndex(x => new { x.PayableId, x.PayableKind }).HasDatabaseName("ix_payable_obligation_items_payable_kind");
        builder.HasIndex(x => x.ProcurementBatchId).HasDatabaseName("ix_payable_obligation_items_procurement_batch_id");
    }
}
