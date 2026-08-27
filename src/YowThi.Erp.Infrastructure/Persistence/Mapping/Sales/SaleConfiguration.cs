using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.Sales;
using YowThi.Erp.Infrastructure.Persistence.System;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Sales;

internal sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.ToTable("sales", "sales", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_sales_status", "status IN ('DRAFT', 'CONFIRMED')");
            tableBuilder.HasCheckConstraint("ck_sales_confirmation_state", "(status = 'DRAFT' AND confirmed_at IS NULL AND confirmed_by_account_id IS NULL) OR (status = 'CONFIRMED' AND confirmed_at IS NOT NULL AND confirmed_by_account_id IS NOT NULL)");
            tableBuilder.HasCheckConstraint("ck_sales_row_version", "row_version >= 1");
            tableBuilder.HasCheckConstraint("ck_sales_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
        });

        builder.HasKey(x => x.Id).HasName("pk_sales");

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.SalesDate).HasColumnName("sales_date").HasColumnType("date").IsRequired();
        builder.Property(x => x.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.ConfirmedByAccountId).HasColumnName("confirmed_by_account_id");
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(1L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.CreatedByAccountId).HasColumnName("created_by_account_id").IsRequired();
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.DeletedByAccountId).HasColumnName("deleted_by_account_id");

        builder.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sales_customer");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.ConfirmedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sales_confirmed_by_account");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.CreatedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sales_created_by_account");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.DeletedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_sales_deleted_by_account");

        builder.HasIndex(x => x.CustomerId).HasDatabaseName("ix_sales_customer_id");
        builder.HasIndex(x => x.ConfirmedByAccountId).HasDatabaseName("ix_sales_confirmed_by_account_id");
        builder.HasIndex(x => x.CreatedByAccountId).HasDatabaseName("ix_sales_created_by_account_id");
        builder.HasIndex(x => x.DeletedByAccountId).HasDatabaseName("ix_sales_deleted_by_account_id");
    }
}
