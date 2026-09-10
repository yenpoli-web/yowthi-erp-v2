using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Infrastructure;
using YowThi.Erp.Domain.ProcessingConfiguration;
using YowThi.Erp.Domain.Procurement;
using YowThi.Erp.Domain.Product;
using YowThi.Erp.Infrastructure.Persistence.System;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Procurement;

internal sealed class ProcurementBatchConfiguration : IEntityTypeConfiguration<ProcurementBatch>
{
    public void Configure(EntityTypeBuilder<ProcurementBatch> builder)
    {
        builder.ToTable("procurement_batches", "procurement", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_procurement_batches_status", "procurement_status IN ('OPEN', 'COMPLETED')");
            tableBuilder.HasCheckConstraint("ck_procurement_batches_lifecycle_status", "lifecycle_status IN ('ACTIVE', 'CLOSED')");
            tableBuilder.HasCheckConstraint("ck_procurement_batches_route_binding", "(processing_route_id IS NULL AND processing_route_version_id IS NULL) OR (processing_route_id IS NOT NULL AND processing_route_version_id IS NOT NULL)");
            tableBuilder.HasCheckConstraint("ck_procurement_batches_completion_state", "(procurement_status = 'OPEN' AND completed_at IS NULL AND completed_by_account_id IS NULL) OR (procurement_status = 'COMPLETED' AND completed_at IS NOT NULL AND completed_by_account_id IS NOT NULL)");
            tableBuilder.HasCheckConstraint("ck_procurement_batches_closing_state", "(lifecycle_status = 'ACTIVE' AND closed_at IS NULL AND closed_by_account_id IS NULL) OR (lifecycle_status = 'CLOSED' AND closed_at IS NOT NULL AND closed_by_account_id IS NOT NULL)");
            tableBuilder.HasCheckConstraint("ck_procurement_batches_row_version", "row_version >= 1");
            tableBuilder.HasCheckConstraint("ck_procurement_batches_deleted_pair", "(deleted_at IS NULL AND deleted_by_account_id IS NULL) OR (deleted_at IS NOT NULL AND deleted_by_account_id IS NOT NULL)");
        });

        builder.HasKey(x => x.Id).HasName("pk_procurement_batches");

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ProcurementDate).HasColumnName("procurement_date").HasColumnType("date").IsRequired();
        builder.Property(x => x.ProcurementProductId).HasColumnName("procurement_product_id").IsRequired();
        builder.Property(x => x.ReceiptStorageLocationId).HasColumnName("receipt_storage_location_id");
        builder.Property(x => x.ProcurementStatus).HasColumnName("procurement_status").HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(x => x.LifecycleStatus).HasColumnName("lifecycle_status").HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(x => x.ProcessingRouteId).HasColumnName("processing_route_id");
        builder.Property(x => x.ProcessingRouteVersionId).HasColumnName("processing_route_version_id");
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.CompletedByAccountId).HasColumnName("completed_by_account_id");
        builder.Property(x => x.ClosedAt).HasColumnName("closed_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.ClosedByAccountId).HasColumnName("closed_by_account_id");
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(1L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.CreatedByAccountId).HasColumnName("created_by_account_id").IsRequired();
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp with time zone");
        builder.Property(x => x.DeletedByAccountId).HasColumnName("deleted_by_account_id");

        builder.HasOne<ProcurementProduct>().WithMany().HasForeignKey(x => x.ProcurementProductId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_procurement_batches_product");
        builder.HasOne<StorageLocation>().WithMany().HasForeignKey(x => x.ReceiptStorageLocationId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_procurement_batches_receipt_storage_location");
        builder.HasOne<ProcessingRoute>().WithMany().HasForeignKey(x => x.ProcessingRouteId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_procurement_batches_route");
        builder.HasOne<ProcessingRouteVersion>().WithMany().HasForeignKey(x => new { x.ProcessingRouteVersionId, x.ProcessingRouteId }).HasPrincipalKey(x => new { x.Id, x.ProcessingRouteId }).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_procurement_batches_route_version_route");

        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.CreatedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_procurement_batches_created_by_account");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.CompletedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_procurement_batches_completed_by_account");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.ClosedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_procurement_batches_closed_by_account");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.DeletedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_procurement_batches_deleted_by_account");

        builder.HasIndex(x => new { x.ProcurementDate, x.ProcurementProductId }).IsUnique().HasDatabaseName("ux_procurement_batches_date_product");
        builder.HasIndex(x => x.ProcurementProductId).HasDatabaseName("ix_procurement_batches_product_id");
        builder.HasIndex(x => x.ReceiptStorageLocationId).HasDatabaseName("ix_procurement_batches_receipt_storage_location_id");
        builder.HasIndex(x => x.ProcessingRouteId).HasDatabaseName("ix_procurement_batches_route_id");
        builder.HasIndex(x => new { x.ProcessingRouteVersionId, x.ProcessingRouteId }).HasDatabaseName("ix_procurement_batches_route_version_route");
        builder.HasIndex(x => x.CreatedByAccountId).HasDatabaseName("ix_procurement_batches_created_by_account_id");
        builder.HasIndex(x => x.CompletedByAccountId).HasDatabaseName("ix_procurement_batches_completed_by_account_id");
        builder.HasIndex(x => x.ClosedByAccountId).HasDatabaseName("ix_procurement_batches_closed_by_account_id");
        builder.HasIndex(x => x.DeletedByAccountId).HasDatabaseName("ix_procurement_batches_deleted_by_account_id");
    }
}
