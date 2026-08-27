using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Labor;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Infrastructure.Persistence.System;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Labor;

internal sealed class EmployeeDailyWageConfiguration : IEntityTypeConfiguration<EmployeeDailyWage>
{
    public void Configure(EntityTypeBuilder<EmployeeDailyWage> builder)
    {
        builder.ToTable("employee_daily_wages", "labor", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_employee_daily_wages_processing_total", "processing_wage_total_thb >= 0");
            tableBuilder.HasCheckConstraint("ck_employee_daily_wages_packaging_total", "sales_packaging_wage_total_thb >= 0");
            tableBuilder.HasCheckConstraint("ck_employee_daily_wages_total", "total_wage_thb >= 0");
            tableBuilder.HasCheckConstraint("ck_employee_daily_wages_total_formula", "total_wage_thb = processing_wage_total_thb + sales_packaging_wage_total_thb");
            tableBuilder.HasCheckConstraint("ck_employee_daily_wages_row_version", "row_version >= 1");
        });

        builder.HasKey(x => x.Id).HasName("pk_employee_daily_wages");
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.WorkDate).HasColumnName("work_date").HasColumnType("date").IsRequired();
        builder.Property(x => x.EmployeeId).HasColumnName("employee_id").IsRequired();
        builder.Property(x => x.ProcessingWageTotalThb).HasColumnName("processing_wage_total_thb").IsRequired();
        builder.Property(x => x.SalesPackagingWageTotalThb).HasColumnName("sales_packaging_wage_total_thb").IsRequired();
        builder.Property(x => x.TotalWageThb).HasColumnName("total_wage_thb").IsRequired();
        builder.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.ConfirmedByAccountId).HasColumnName("confirmed_by_account_id").IsRequired();
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(1L).IsConcurrencyToken();

        builder.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_employee_daily_wages_employee");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.ConfirmedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_employee_daily_wages_confirmed_by_account");

        builder.HasIndex(x => new { x.WorkDate, x.EmployeeId }).IsUnique().HasDatabaseName("ux_employee_daily_wages_work_date_employee");
        builder.HasIndex(x => x.EmployeeId).HasDatabaseName("ix_employee_daily_wages_employee_id");
        builder.HasIndex(x => x.ConfirmedByAccountId).HasDatabaseName("ix_employee_daily_wages_confirmed_by_account_id");
    }
}
