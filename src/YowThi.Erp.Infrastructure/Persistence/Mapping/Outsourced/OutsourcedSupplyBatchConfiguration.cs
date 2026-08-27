using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Outsourced;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Infrastructure.Persistence.System;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Outsourced;

internal sealed class OutsourcedSupplyBatchConfiguration : IEntityTypeConfiguration<OutsourcedSupplyBatch>
{
    public void Configure(EntityTypeBuilder<OutsourcedSupplyBatch> builder)
    {
        builder.ToTable("outsourced_supply_batches", "outsourced", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_outsourced_supply_batches_lifecycle_status", "lifecycle_status IN ('ACTIVE', 'CLOSED')");
            tableBuilder.HasCheckConstraint("ck_outsourced_supply_batches_closing_state", "(lifecycle_status = 'ACTIVE' AND closed_at IS NULL AND closed_by_account_id IS NULL) OR (lifecycle_status = 'CLOSED' AND closed_at IS NOT NULL AND closed_by_account_id IS NOT NULL)");
            tableBuilder.HasCheckConstraint("ck_outsourced_supply_batches_row_version", "row_version >= 1");
            tableBuilder.HasCheckConstraint("ck_outsourced_supply_batches_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
        });

        builder.HasKey(x => x.Id).HasName("pk_outsourced_supply_batches");

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.SupplyDate).HasColumnName("supply_date").HasColumnType("date").IsRequired();
        builder.Property(x => x.OutsourcedVendorId).HasColumnName("outsourced_vendor_id").IsRequired();
        builder.Property(x => x.LifecycleStatus).HasColumnName("lifecycle_status").HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(x => x.ClosedAt).HasColumnName("closed_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.ClosedByAccountId).HasColumnName("closed_by_account_id");
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(1L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.CreatedByAccountId).HasColumnName("created_by_account_id").IsRequired();
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.DeletedByAccountId).HasColumnName("deleted_by_account_id");

        builder.HasOne<OutsourcedVendor>().WithMany().HasForeignKey(x => x.OutsourcedVendorId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_outsourced_supply_batches_vendor");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.ClosedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_outsourced_supply_batches_closed_by_account");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.CreatedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_outsourced_supply_batches_created_by_account");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.DeletedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_outsourced_supply_batches_deleted_by_account");

        builder.HasIndex(x => new { x.SupplyDate, x.OutsourcedVendorId }).IsUnique().HasDatabaseName("ux_outsourced_supply_batches_date_vendor");
        builder.HasIndex(x => x.OutsourcedVendorId).HasDatabaseName("ix_outsourced_supply_batches_vendor_id");
        builder.HasIndex(x => x.ClosedByAccountId).HasDatabaseName("ix_outsourced_supply_batches_closed_by_account_id");
        builder.HasIndex(x => x.CreatedByAccountId).HasDatabaseName("ix_outsourced_supply_batches_created_by_account_id");
        builder.HasIndex(x => x.DeletedByAccountId).HasDatabaseName("ix_outsourced_supply_batches_deleted_by_account_id");
    }
}
