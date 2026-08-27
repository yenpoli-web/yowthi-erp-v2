using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Labor;
using YowThi.Erp.Domain.SalesHandling;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Labor;

internal sealed class SalesPackagingWageComponentConfiguration : IEntityTypeConfiguration<SalesPackagingWageComponent>
{
    public void Configure(EntityTypeBuilder<SalesPackagingWageComponent> builder)
    {
        builder.ToTable("sales_packaging_wage_components", "labor", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_sales_packaging_wage_components_amount", "wage_amount_thb >= 0");
        });

        builder.HasKey(x => x.Id).HasName("pk_sales_packaging_wage_components");
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.EmployeeDailyWageId).HasColumnName("employee_daily_wage_id").IsRequired();
        builder.Property(x => x.SalesPackagingWorkRecordId).HasColumnName("sales_packaging_work_record_id").IsRequired();
        builder.Property(x => x.WageAmountThb).HasColumnName("wage_amount_thb").IsRequired();

        builder.HasOne<EmployeeDailyWage>().WithMany().HasForeignKey(x => x.EmployeeDailyWageId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sales_packaging_wage_components_daily_wage");
        builder.HasOne<SalesPackagingWorkRecord>().WithMany().HasForeignKey(x => x.SalesPackagingWorkRecordId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sales_packaging_wage_components_work_record");

        builder.HasIndex(x => x.EmployeeDailyWageId).HasDatabaseName("ix_sales_packaging_wage_components_daily_wage_id");
        builder.HasIndex(x => x.SalesPackagingWorkRecordId).IsUnique().HasDatabaseName("ux_sales_packaging_wage_components_work_record");
    }
}
