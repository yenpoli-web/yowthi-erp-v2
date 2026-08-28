using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Finance;
using YowThi.Erp.Domain.Labor;
using YowThi.Erp.Domain.Outsourced;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.Procurement;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Finance;

internal sealed class PayableConfiguration : IEntityTypeConfiguration<Payable>
{
    public void Configure(EntityTypeBuilder<Payable> builder)
    {
        builder.ToTable("payables", "finance", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_payables_kind", "payable_kind IN ('PROCUREMENT_SUPPLIER', 'PROCUREMENT_FARMER', 'COMPANY_PICKUP_TRANSPORT', 'OUTSOURCED_VENDOR', 'EMPLOYEE_DAILY_WAGE')");
            tableBuilder.HasCheckConstraint("ck_payables_typed_source", "(payable_kind = 'PROCUREMENT_SUPPLIER' AND procurement_batch_id IS NOT NULL AND supplier_id IS NOT NULL AND farmer_id IS NULL AND outsourced_supply_detail_id IS NULL AND employee_daily_wage_id IS NULL) OR (payable_kind = 'PROCUREMENT_FARMER' AND procurement_batch_id IS NOT NULL AND supplier_id IS NULL AND farmer_id IS NOT NULL AND outsourced_supply_detail_id IS NULL AND employee_daily_wage_id IS NULL) OR (payable_kind = 'COMPANY_PICKUP_TRANSPORT' AND procurement_batch_id IS NULL AND supplier_id IS NULL AND farmer_id IS NULL AND outsourced_supply_detail_id IS NULL AND employee_daily_wage_id IS NULL) OR (payable_kind = 'OUTSOURCED_VENDOR' AND procurement_batch_id IS NULL AND supplier_id IS NULL AND farmer_id IS NULL AND outsourced_supply_detail_id IS NOT NULL AND employee_daily_wage_id IS NULL) OR (payable_kind = 'EMPLOYEE_DAILY_WAGE' AND procurement_batch_id IS NULL AND supplier_id IS NULL AND farmer_id IS NULL AND outsourced_supply_detail_id IS NULL AND employee_daily_wage_id IS NOT NULL)");
            tableBuilder.HasCheckConstraint("ck_payables_row_version", "row_version >= 1");
        });

        builder.HasKey(x => x.Id).HasName("pk_payables");
        builder.HasAlternateKey(x => new { x.Id, x.PayableKind }).HasName("ak_payables_id_kind");

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.PayableKind).HasColumnName("payable_kind").HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(x => x.ProcurementBatchId).HasColumnName("procurement_batch_id");
        builder.Property(x => x.SupplierId).HasColumnName("supplier_id");
        builder.Property(x => x.FarmerId).HasColumnName("farmer_id");
        builder.Property(x => x.OutsourcedSupplyDetailId).HasColumnName("outsourced_supply_detail_id");
        builder.Property(x => x.EmployeeDailyWageId).HasColumnName("employee_daily_wage_id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(1L).IsConcurrencyToken();

        builder.HasOne<ProcurementBatch>().WithMany().HasForeignKey(x => x.ProcurementBatchId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_payables_procurement_batch");
        builder.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_payables_supplier");
        builder.HasOne<Farmer>().WithMany().HasForeignKey(x => x.FarmerId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_payables_farmer");
        builder.HasOne<OutsourcedSupplyDetail>().WithMany().HasForeignKey(x => x.OutsourcedSupplyDetailId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_payables_outsourced_supply_detail");
        builder.HasOne<EmployeeDailyWage>().WithMany().HasForeignKey(x => x.EmployeeDailyWageId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_payables_employee_daily_wage");

        builder.HasIndex(x => x.SupplierId).HasDatabaseName("ix_payables_supplier_id");
        builder.HasIndex(x => x.FarmerId).HasDatabaseName("ix_payables_farmer_id");
        builder.HasIndex(x => new { x.ProcurementBatchId, x.SupplierId }).IsUnique().HasFilter("payable_kind = 'PROCUREMENT_SUPPLIER'").HasDatabaseName("ux_payables_procurement_supplier");
        builder.HasIndex(x => new { x.ProcurementBatchId, x.FarmerId }).IsUnique().HasFilter("payable_kind = 'PROCUREMENT_FARMER'").HasDatabaseName("ux_payables_procurement_farmer");
        builder.HasIndex(x => x.OutsourcedSupplyDetailId).IsUnique().HasFilter("payable_kind = 'OUTSOURCED_VENDOR'").HasDatabaseName("ux_payables_outsourced_vendor");
        builder.HasIndex(x => x.EmployeeDailyWageId).IsUnique().HasFilter("payable_kind = 'EMPLOYEE_DAILY_WAGE'").HasDatabaseName("ux_payables_employee_daily_wage");
    }
}
