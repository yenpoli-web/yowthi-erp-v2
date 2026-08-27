using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.Sales;
using YowThi.Erp.Domain.SalesHandling;
using YowThi.Erp.Infrastructure.Persistence.System;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.SalesHandling;

internal sealed class SalesPackagingWorkRecordConfiguration : IEntityTypeConfiguration<SalesPackagingWorkRecord>
{
    public void Configure(EntityTypeBuilder<SalesPackagingWorkRecord> builder)
    {
        builder.ToTable("sales_packaging_work_records", "sales_handling", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_sales_packaging_work_records_confirmed_wage", "confirmed_wage_thb >= 0");
            tableBuilder.HasCheckConstraint("ck_sales_packaging_work_records_row_version", "row_version >= 1");
            tableBuilder.HasCheckConstraint("ck_sales_packaging_work_records_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
        });

        builder.HasKey(x => x.Id).HasName("pk_sales_packaging_work_records");
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.SalesId).HasColumnName("sales_id").IsRequired();
        builder.Property(x => x.WorkDate).HasColumnName("work_date").HasColumnType("date").IsRequired();
        builder.Property(x => x.EmployeeId).HasColumnName("employee_id").IsRequired();
        builder.Property(x => x.SalesPackagingItemId).HasColumnName("sales_packaging_item_id").IsRequired();
        builder.Property(x => x.ConfirmedWageThb).HasColumnName("confirmed_wage_thb").IsRequired();
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.RecordedByAccountId).HasColumnName("recorded_by_account_id").IsRequired();
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(1L).IsConcurrencyToken();
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.DeletedByAccountId).HasColumnName("deleted_by_account_id");

        builder.HasOne<Sale>().WithMany().HasForeignKey(x => x.SalesId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sales_packaging_work_records_sales");
        builder.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sales_packaging_work_records_employee");
        builder.HasOne<SalesPackagingItem>().WithMany().HasForeignKey(x => x.SalesPackagingItemId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sales_packaging_work_records_item");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.RecordedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sales_packaging_work_records_recorded_by_account");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.DeletedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sales_packaging_work_records_deleted_by_account");

        builder.HasIndex(x => x.SalesId).HasDatabaseName("ix_sales_packaging_work_records_sales_id");
        builder.HasIndex(x => x.EmployeeId).HasDatabaseName("ix_sales_packaging_work_records_employee_id");
        builder.HasIndex(x => x.SalesPackagingItemId).HasDatabaseName("ix_sales_packaging_work_records_item_id");
        builder.HasIndex(x => x.RecordedByAccountId).HasDatabaseName("ix_sales_packaging_work_records_recorded_by_account_id");
        builder.HasIndex(x => x.DeletedByAccountId).HasDatabaseName("ix_sales_packaging_work_records_deleted_by_account_id");
    }
}
