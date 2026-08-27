using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Infrastructure;
using YowThi.Erp.Domain.Inventory;
using YowThi.Erp.Domain.Outsourced;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.ProcessingConfiguration;
using YowThi.Erp.Domain.Procurement;
using YowThi.Erp.Domain.Product;
using YowThi.Erp.Domain.Sales;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Inventory;

internal sealed class InventoryMovementConfiguration : IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> builder)
    {
        builder.ToTable("inventory_movements", "inventory", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_inventory_movements_sequence", "sequence > 0");
            tableBuilder.HasCheckConstraint("ck_inventory_movements_type", "movement_type IN ('PURCHASE_RECEIPT', 'PROCESS_CONSUME', 'PROCESS_PRODUCE', 'FINAL_PACKAGE_CONSUME', 'FINAL_PACKAGE_PRODUCE', 'OUTSOURCED_RECEIPT', 'TRANSFER_OUT', 'TRANSFER_IN', 'SALES_ISSUE', 'SALES_ALLOCATION_ADJUSTMENT', 'ADJUSTMENT', 'BATCH_RECONCILIATION')");
            tableBuilder.HasCheckConstraint("ck_inventory_movements_origin", "origin IN ('IN_HOUSE', 'OUTSOURCED')");
            tableBuilder.HasCheckConstraint("ck_inventory_movements_source_batch", "(origin = 'IN_HOUSE' AND procurement_batch_id IS NOT NULL AND outsourced_supply_batch_id IS NULL) OR (origin = 'OUTSOURCED' AND procurement_batch_id IS NULL AND outsourced_supply_batch_id IS NOT NULL)");
            tableBuilder.HasCheckConstraint("ck_inventory_movements_object_kind", "inventory_object_kind IN ('PROCUREMENT_PRODUCT', 'PROCESS_MATERIAL', 'SALES_PRODUCT')");
            tableBuilder.HasCheckConstraint("ck_inventory_movements_typed_object", "(inventory_object_kind = 'PROCUREMENT_PRODUCT' AND procurement_product_id IS NOT NULL AND process_material_id IS NULL AND sales_product_id IS NULL) OR (inventory_object_kind = 'PROCESS_MATERIAL' AND procurement_product_id IS NULL AND process_material_id IS NOT NULL AND sales_product_id IS NULL) OR (inventory_object_kind = 'SALES_PRODUCT' AND procurement_product_id IS NULL AND process_material_id IS NULL AND sales_product_id IS NOT NULL)");
            tableBuilder.HasCheckConstraint("ck_inventory_movements_raw_source_kind", "raw_source_kind IS NULL OR raw_source_kind IN ('SUPPLIER', 'FARMERS_COMBINED')");
            tableBuilder.HasCheckConstraint("ck_inventory_movements_raw_source_shape", "(raw_source_kind IS NULL AND supplier_id IS NULL) OR (origin = 'IN_HOUSE' AND inventory_object_kind = 'PROCUREMENT_PRODUCT' AND raw_source_kind = 'SUPPLIER' AND supplier_id IS NOT NULL) OR (origin = 'IN_HOUSE' AND inventory_object_kind = 'PROCUREMENT_PRODUCT' AND raw_source_kind = 'FARMERS_COMBINED' AND supplier_id IS NULL)");
            tableBuilder.HasCheckConstraint("ck_inventory_movements_quantity_sign", "quantity_delta NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric) AND ((movement_type IN ('PURCHASE_RECEIPT', 'PROCESS_PRODUCE', 'FINAL_PACKAGE_PRODUCE', 'OUTSOURCED_RECEIPT', 'TRANSFER_IN') AND quantity_delta > 0) OR (movement_type IN ('PROCESS_CONSUME', 'FINAL_PACKAGE_CONSUME', 'TRANSFER_OUT', 'SALES_ISSUE') AND quantity_delta < 0) OR (movement_type IN ('SALES_ALLOCATION_ADJUSTMENT', 'ADJUSTMENT', 'BATCH_RECONCILIATION') AND quantity_delta <> 0))");
            tableBuilder.HasCheckConstraint("ck_inventory_movements_allocation_lineage", "(movement_type IN ('SALES_ISSUE', 'SALES_ALLOCATION_ADJUSTMENT') AND sales_allocation_revision_item_id IS NOT NULL) OR (movement_type NOT IN ('SALES_ISSUE', 'SALES_ALLOCATION_ADJUSTMENT') AND sales_allocation_revision_item_id IS NULL)");
        });

        builder.HasKey(x => x.Id).HasName("pk_inventory_movements");

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.InventoryOperationId).HasColumnName("inventory_operation_id").IsRequired();
        builder.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(x => x.MovementType).HasColumnName("movement_type").HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(x => x.Origin).HasColumnName("origin").HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(x => x.ProcurementBatchId).HasColumnName("procurement_batch_id");
        builder.Property(x => x.OutsourcedSupplyBatchId).HasColumnName("outsourced_supply_batch_id");
        builder.Property(x => x.InventoryObjectKind).HasColumnName("inventory_object_kind").HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(x => x.ProcurementProductId).HasColumnName("procurement_product_id");
        builder.Property(x => x.ProcessMaterialId).HasColumnName("process_material_id");
        builder.Property(x => x.SalesProductId).HasColumnName("sales_product_id");
        builder.Property(x => x.StorageLocationId).HasColumnName("storage_location_id").IsRequired();
        builder.Property(x => x.RawSourceKind).HasColumnName("raw_source_kind").HasConversion<string>().HasColumnType("text");
        builder.Property(x => x.SupplierId).HasColumnName("supplier_id");
        builder.Property(x => x.QuantityDelta).HasColumnName("quantity_delta").HasColumnType("numeric").IsRequired();
        builder.Property(x => x.SalesAllocationRevisionItemId).HasColumnName("sales_allocation_revision_item_id");
        builder.Property(x => x.RecordedAt).HasColumnName("recorded_at").HasColumnType("timestamp with time zone").IsRequired();

        builder.HasOne<InventoryOperation>().WithMany().HasForeignKey(x => x.InventoryOperationId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_inventory_movements_operation");
        builder.HasOne<ProcurementBatch>().WithMany().HasForeignKey(x => x.ProcurementBatchId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_inventory_movements_procurement_batch");
        builder.HasOne<OutsourcedSupplyBatch>().WithMany().HasForeignKey(x => x.OutsourcedSupplyBatchId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_inventory_movements_outsourced_batch");
        builder.HasOne<ProcurementProduct>().WithMany().HasForeignKey(x => x.ProcurementProductId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_inventory_movements_procurement_product");
        builder.HasOne<ProcessMaterial>().WithMany().HasForeignKey(x => x.ProcessMaterialId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_inventory_movements_process_material");
        builder.HasOne<SalesProduct>().WithMany().HasForeignKey(x => x.SalesProductId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_inventory_movements_sales_product");
        builder.HasOne<StorageLocation>().WithMany().HasForeignKey(x => x.StorageLocationId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_inventory_movements_storage_location");
        builder.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_inventory_movements_supplier");
        builder.HasOne<SalesAllocationRevisionItem>().WithMany().HasForeignKey(x => x.SalesAllocationRevisionItemId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_inventory_movements_sales_allocation_revision_item");

        builder.HasIndex(x => new { x.InventoryOperationId, x.Sequence }).IsUnique().HasDatabaseName("ux_inventory_movements_operation_sequence");
        builder.HasIndex(x => x.ProcurementBatchId).HasDatabaseName("ix_inventory_movements_procurement_batch_id");
        builder.HasIndex(x => x.OutsourcedSupplyBatchId).HasDatabaseName("ix_inventory_movements_outsourced_batch_id");
        builder.HasIndex(x => x.ProcurementProductId).HasDatabaseName("ix_inventory_movements_procurement_product_id");
        builder.HasIndex(x => x.ProcessMaterialId).HasDatabaseName("ix_inventory_movements_process_material_id");
        builder.HasIndex(x => x.SalesProductId).HasDatabaseName("ix_inventory_movements_sales_product_id");
        builder.HasIndex(x => x.StorageLocationId).HasDatabaseName("ix_inventory_movements_storage_location_id");
        builder.HasIndex(x => x.SupplierId).HasDatabaseName("ix_inventory_movements_supplier_id");
        builder.HasIndex(x => x.SalesAllocationRevisionItemId).HasDatabaseName("ix_inventory_movements_sales_allocation_revision_item_id");
    }
}
