using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Domain.Infrastructure;
using YowThi.Erp.Domain.Inventory;
using YowThi.Erp.Domain.Outsourced;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.ProcessingConfiguration;
using YowThi.Erp.Domain.Procurement;
using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Inventory;

internal sealed class InventoryPositionConfiguration : IEntityTypeConfiguration<InventoryPosition>
{
    public void Configure(EntityTypeBuilder<InventoryPosition> builder)
    {
        builder.ToTable("inventory_positions", "inventory", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_inventory_positions_origin", "origin IN ('IN_HOUSE', 'OUTSOURCED')");
            tableBuilder.HasCheckConstraint("ck_inventory_positions_source_batch", "(origin = 'IN_HOUSE' AND procurement_batch_id IS NOT NULL AND outsourced_supply_batch_id IS NULL) OR (origin = 'OUTSOURCED' AND procurement_batch_id IS NULL AND outsourced_supply_batch_id IS NOT NULL)");
            tableBuilder.HasCheckConstraint("ck_inventory_positions_object_kind", "inventory_object_kind IN ('PROCUREMENT_PRODUCT', 'PROCESS_MATERIAL', 'SALES_PRODUCT')");
            tableBuilder.HasCheckConstraint("ck_inventory_positions_typed_object", "(inventory_object_kind = 'PROCUREMENT_PRODUCT' AND procurement_product_id IS NOT NULL AND process_material_id IS NULL AND sales_product_id IS NULL) OR (inventory_object_kind = 'PROCESS_MATERIAL' AND procurement_product_id IS NULL AND process_material_id IS NOT NULL AND sales_product_id IS NULL) OR (inventory_object_kind = 'SALES_PRODUCT' AND procurement_product_id IS NULL AND process_material_id IS NULL AND sales_product_id IS NOT NULL)");
            tableBuilder.HasCheckConstraint("ck_inventory_positions_raw_source_kind", "raw_source_kind IS NULL OR raw_source_kind IN ('SUPPLIER', 'FARMERS_COMBINED')");
            tableBuilder.HasCheckConstraint("ck_inventory_positions_raw_source_shape", "(raw_source_kind IS NULL AND supplier_id IS NULL) OR (origin = 'IN_HOUSE' AND inventory_object_kind = 'PROCUREMENT_PRODUCT' AND raw_source_kind = 'SUPPLIER' AND supplier_id IS NOT NULL) OR (origin = 'IN_HOUSE' AND inventory_object_kind = 'PROCUREMENT_PRODUCT' AND raw_source_kind = 'FARMERS_COMBINED' AND supplier_id IS NULL)");
            tableBuilder.HasCheckConstraint("ck_inventory_positions_balance_quantity", "balance_quantity NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric)");
            tableBuilder.HasCheckConstraint("ck_inventory_positions_row_version", "row_version >= 1");
        });

        builder.HasKey(x => x.Id).HasName("pk_inventory_positions");

        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
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
        builder.Property(x => x.BalanceQuantity).HasColumnName("balance_quantity").HasColumnType("numeric").IsRequired();
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(1L).IsConcurrencyToken();

        builder.HasOne<ProcurementBatch>().WithMany().HasForeignKey(x => x.ProcurementBatchId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_inventory_positions_procurement_batch");
        builder.HasOne<OutsourcedSupplyBatch>().WithMany().HasForeignKey(x => x.OutsourcedSupplyBatchId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_inventory_positions_outsourced_batch");
        builder.HasOne<ProcurementProduct>().WithMany().HasForeignKey(x => x.ProcurementProductId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_inventory_positions_procurement_product");
        builder.HasOne<ProcessMaterial>().WithMany().HasForeignKey(x => x.ProcessMaterialId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_inventory_positions_process_material");
        builder.HasOne<SalesProduct>().WithMany().HasForeignKey(x => x.SalesProductId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_inventory_positions_sales_product");
        builder.HasOne<StorageLocation>().WithMany().HasForeignKey(x => x.StorageLocationId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_inventory_positions_storage_location");
        builder.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_inventory_positions_supplier");

        builder.HasIndex(x => new
        {
            x.Origin,
            x.ProcurementBatchId,
            x.OutsourcedSupplyBatchId,
            x.InventoryObjectKind,
            x.ProcurementProductId,
            x.ProcessMaterialId,
            x.SalesProductId,
            x.StorageLocationId,
            x.RawSourceKind,
            x.SupplierId,
        })
        .IsUnique()
        .AreNullsDistinct(false)
        .HasDatabaseName("ux_inventory_positions_full_identity");

        builder.HasIndex(x => x.ProcurementBatchId).HasDatabaseName("ix_inventory_positions_procurement_batch_id");
        builder.HasIndex(x => x.OutsourcedSupplyBatchId).HasDatabaseName("ix_inventory_positions_outsourced_batch_id");
        builder.HasIndex(x => x.ProcurementProductId).HasDatabaseName("ix_inventory_positions_procurement_product_id");
        builder.HasIndex(x => x.ProcessMaterialId).HasDatabaseName("ix_inventory_positions_process_material_id");
        builder.HasIndex(x => x.SalesProductId).HasDatabaseName("ix_inventory_positions_sales_product_id");
        builder.HasIndex(x => x.StorageLocationId).HasDatabaseName("ix_inventory_positions_storage_location_id");
        builder.HasIndex(x => x.SupplierId).HasDatabaseName("ix_inventory_positions_supplier_id");
    }
}
