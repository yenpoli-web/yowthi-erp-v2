using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Inventory;
using YowThi.Erp.Domain.Outsourced;
using YowThi.Erp.Domain.Processing;
using YowThi.Erp.Domain.Procurement;
using YowThi.Erp.Domain.Sales;
using YowThi.Erp.Infrastructure.Persistence.System;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Inventory;

internal sealed class InventoryOperationConfiguration : IEntityTypeConfiguration<InventoryOperation>
{
    public void Configure(EntityTypeBuilder<InventoryOperation> builder)
    {
        builder.ToTable("inventory_operations", "inventory", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_inventory_operations_type", "operation_type IN ('PROCUREMENT_RECEIPT', 'PROCESSING', 'TRANSFER', 'ADJUSTMENT', 'BATCH_RECONCILIATION', 'OUTSOURCED_RECEIPT', 'SALES_ISSUE', 'SALES_ALLOCATION_REVISION')");
            tableBuilder.HasCheckConstraint("ck_inventory_operations_source_shape", "(operation_type = 'PROCUREMENT_RECEIPT' AND procurement_entry_id IS NOT NULL AND processing_execution_id IS NULL AND outsourced_supply_detail_id IS NULL AND sales_id IS NULL AND sales_allocation_revision_id IS NULL AND procurement_batch_id IS NULL) OR (operation_type = 'PROCESSING' AND procurement_entry_id IS NULL AND processing_execution_id IS NOT NULL AND outsourced_supply_detail_id IS NULL AND sales_id IS NULL AND sales_allocation_revision_id IS NULL AND procurement_batch_id IS NULL) OR (operation_type = 'OUTSOURCED_RECEIPT' AND procurement_entry_id IS NULL AND processing_execution_id IS NULL AND outsourced_supply_detail_id IS NOT NULL AND sales_id IS NULL AND sales_allocation_revision_id IS NULL AND procurement_batch_id IS NULL) OR (operation_type = 'SALES_ISSUE' AND procurement_entry_id IS NULL AND processing_execution_id IS NULL AND outsourced_supply_detail_id IS NULL AND sales_id IS NOT NULL AND sales_allocation_revision_id IS NULL AND procurement_batch_id IS NULL) OR (operation_type = 'SALES_ALLOCATION_REVISION' AND procurement_entry_id IS NULL AND processing_execution_id IS NULL AND outsourced_supply_detail_id IS NULL AND sales_id IS NULL AND sales_allocation_revision_id IS NOT NULL AND procurement_batch_id IS NULL) OR (operation_type = 'BATCH_RECONCILIATION' AND procurement_entry_id IS NULL AND processing_execution_id IS NULL AND outsourced_supply_detail_id IS NULL AND sales_id IS NULL AND sales_allocation_revision_id IS NULL AND procurement_batch_id IS NOT NULL) OR (operation_type IN ('TRANSFER', 'ADJUSTMENT') AND procurement_entry_id IS NULL AND processing_execution_id IS NULL AND outsourced_supply_detail_id IS NULL AND sales_id IS NULL AND sales_allocation_revision_id IS NULL AND procurement_batch_id IS NULL)");
        });

        builder.HasKey(x => x.Id).HasName("pk_inventory_operations");

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.OperationType).HasColumnName("operation_type").HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(x => x.ProcurementEntryId).HasColumnName("procurement_entry_id");
        builder.Property(x => x.ProcessingExecutionId).HasColumnName("processing_execution_id");
        builder.Property(x => x.OutsourcedSupplyDetailId).HasColumnName("outsourced_supply_detail_id");
        builder.Property(x => x.SalesId).HasColumnName("sales_id");
        builder.Property(x => x.SalesAllocationRevisionId).HasColumnName("sales_allocation_revision_id");
        builder.Property(x => x.ProcurementBatchId).HasColumnName("procurement_batch_id");
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.RecordedByAccountId).HasColumnName("recorded_by_account_id").IsRequired();

        builder.HasOne<ProcurementEntry>().WithMany().HasForeignKey(x => x.ProcurementEntryId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_inventory_operations_procurement_entry");
        builder.HasOne<ProcessingExecution>().WithMany().HasForeignKey(x => x.ProcessingExecutionId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_inventory_operations_processing_execution");
        builder.HasOne<OutsourcedSupplyDetail>().WithMany().HasForeignKey(x => x.OutsourcedSupplyDetailId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_inventory_operations_outsourced_supply_detail");
        builder.HasOne<Sale>().WithMany().HasForeignKey(x => x.SalesId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_inventory_operations_sales");
        builder.HasOne<SalesAllocationRevision>().WithMany().HasForeignKey(x => x.SalesAllocationRevisionId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_inventory_operations_sales_allocation_revision");
        builder.HasOne<ProcurementBatch>().WithMany().HasForeignKey(x => x.ProcurementBatchId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_inventory_operations_procurement_batch");
        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.RecordedByAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_inventory_operations_recorded_by_account");

        builder.HasIndex(x => x.ProcurementEntryId).IsUnique().HasFilter("operation_type = 'PROCUREMENT_RECEIPT'").HasDatabaseName("ux_inventory_operations_procurement_receipt_source");
        builder.HasIndex(x => x.ProcessingExecutionId).IsUnique().HasFilter("operation_type = 'PROCESSING'").HasDatabaseName("ux_inventory_operations_processing_source");
        builder.HasIndex(x => x.OutsourcedSupplyDetailId).IsUnique().HasFilter("operation_type = 'OUTSOURCED_RECEIPT'").HasDatabaseName("ux_inventory_operations_outsourced_receipt_source");
        builder.HasIndex(x => x.SalesId).IsUnique().HasFilter("operation_type = 'SALES_ISSUE'").HasDatabaseName("ux_inventory_operations_sales_issue_source");
        builder.HasIndex(x => x.SalesAllocationRevisionId).IsUnique().HasFilter("operation_type = 'SALES_ALLOCATION_REVISION'").HasDatabaseName("ux_inventory_operations_sales_allocation_revision_source");
        builder.HasIndex(x => x.ProcurementBatchId).HasDatabaseName("ix_inventory_operations_procurement_batch_id");
        builder.HasIndex(x => x.RecordedByAccountId).HasDatabaseName("ix_inventory_operations_recorded_by_account_id");
    }
}
