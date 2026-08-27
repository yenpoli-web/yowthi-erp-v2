using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using YowThi.Erp.Infrastructure.Persistence;

namespace YowThi.Erp.ArchitectureTests;

public sealed class M4RelationalModelTests
{
    [Fact]
    public void M4_maps_exactly_outsourced_sales_and_inventory_relations()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);

        var expected = new[]
        {
            "inventory.inventory_movements",
            "inventory.inventory_operations",
            "inventory.inventory_positions",
            "outsourced.outsourced_supply_batches",
            "outsourced.outsourced_supply_details",
            "sales.sales",
            "sales.sales_allocation_revision_items",
            "sales.sales_allocation_revisions",
            "sales.sales_allocations",
            "sales.sales_details",
        };

        var actual = model.GetEntityTypes()
            .Where(entityType => entityType.GetSchema() is "outsourced" or "sales" or "inventory")
            .Select(entityType => $"{entityType.GetSchema()}.{entityType.GetTableName()}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.OrderBy(value => value, StringComparer.Ordinal), actual);
    }

    [Fact]
    public void Outsourced_batch_identity_excludes_product_and_detail_pricing_is_row_local()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);
        var batch = GetTable(model, "outsourced", "outsourced_supply_batches");
        var detail = GetTable(model, "outsourced", "outsourced_supply_details");

        Assert.Contains(
            batch.GetIndexes(),
            index => index.IsUnique
                && PropertyNames(index.Properties).SequenceEqual(new[] { "SupplyDate", "OutsourcedVendorId" })
                && index.GetFilter() is null);
        Assert.DoesNotContain(batch.GetProperties(), property => property.Name.Contains("Product", StringComparison.Ordinal));
        Assert.Contains("ck_outsourced_supply_batches_closing_state", CheckNames(batch));

        var detailChecks = CheckNames(detail);
        Assert.Contains("ck_outsourced_supply_details_pricing_basis", detailChecks);
        Assert.Contains("ck_outsourced_supply_details_pricing_shape", detailChecks);
        Assert.Contains("ck_outsourced_supply_details_amount", detailChecks);
        Assert.Contains(detail.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "sales_products" && foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.DoesNotContain(detail.GetIndexes(), index => index.IsUnique);
    }

    [Fact]
    public void Sales_header_detail_keys_and_pricing_shape_match_baseline()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);
        var sale = GetTable(model, "sales", "sales");
        var detail = GetTable(model, "sales", "sales_details");

        var saleChecks = CheckNames(sale);
        Assert.Contains("ck_sales_status", saleChecks);
        Assert.Contains("ck_sales_confirmation_state", saleChecks);

        Assert.Contains(detail.GetKeys(), key => PropertyNames(key.Properties).SequenceEqual(new[] { "Id", "SalesId" }));
        Assert.Contains(
            detail.GetIndexes(),
            index => index.IsUnique && PropertyNames(index.Properties).SequenceEqual(new[] { "SalesId", "LineNumber" }));
        Assert.DoesNotContain(
            detail.GetIndexes(),
            index => index.IsUnique && PropertyNames(index.Properties).SequenceEqual(new[] { "SalesId", "SalesProductId" }));

        var detailChecks = CheckNames(detail);
        Assert.Contains("ck_sales_details_line_number", detailChecks);
        Assert.Contains("ck_sales_details_pricing_shape", detailChecks);
        Assert.Contains("ck_sales_details_amount", detailChecks);
    }

    [Fact]
    public void Allocation_revision_and_items_are_immutable_historical_truth_with_same_sale_integrity()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);
        var revision = GetTable(model, "sales", "sales_allocation_revisions");
        var item = GetTable(model, "sales", "sales_allocation_revision_items");

        Assert.Contains(revision.GetKeys(), key => PropertyNames(key.Properties).SequenceEqual(new[] { "Id", "SalesId" }));
        Assert.Contains(
            revision.GetIndexes(),
            index => index.IsUnique && PropertyNames(index.Properties).SequenceEqual(new[] { "SalesId", "RevisionNumber" }));
        Assert.Null(revision.FindProperty("RowVersion"));
        Assert.Null(revision.FindProperty("DeletedAt"));

        Assert.Contains(item.GetKeys(), key => PropertyNames(key.Properties).SequenceEqual(new[] { "Id", "SalesDetailId" }));
        Assert.Contains(
            item.GetIndexes(),
            index => index.IsUnique && PropertyNames(index.Properties).SequenceEqual(new[] { "SalesAllocationRevisionId", "SalesDetailId", "Sequence" }));
        Assert.Null(item.FindProperty("RowVersion"));
        Assert.Null(item.FindProperty("DeletedAt"));

        var revisionForeignKey = Assert.Single(
            item.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "sales_allocation_revisions");
        Assert.Equal(new[] { "SalesAllocationRevisionId", "SalesId" }, PropertyNames(revisionForeignKey.Properties));
        Assert.Equal(new[] { "Id", "SalesId" }, PropertyNames(revisionForeignKey.PrincipalKey.Properties));

        var detailForeignKey = Assert.Single(
            item.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "sales_details");
        Assert.Equal(new[] { "SalesDetailId", "SalesId" }, PropertyNames(detailForeignKey.Properties));
        Assert.Equal(new[] { "Id", "SalesId" }, PropertyNames(detailForeignKey.PrincipalKey.Properties));

        var checks = CheckNames(item);
        Assert.Contains("ck_sales_allocation_revision_items_typed_source_batch", checks);
        Assert.Contains("ck_sales_allocation_revision_items_allocated_quantity", checks);
    }

    [Fact]
    public void Current_sales_allocation_is_pointer_only_projection()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);
        var allocation = GetTable(model, "sales", "sales_allocations");

        Assert.Equal(new[] { "SalesDetailId", "Sequence" }, PropertyNames(allocation.FindPrimaryKey()!.Properties));
        Assert.Equal(
            new[] { "RowVersion", "SalesAllocationRevisionItemId", "SalesDetailId", "Sequence" },
            allocation.GetProperties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());

        Assert.Contains(
            allocation.GetIndexes(),
            index => index.IsUnique && PropertyNames(index.Properties).SequenceEqual(new[] { "SalesAllocationRevisionItemId" }));

        var pointerForeignKey = Assert.Single(
            allocation.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "sales_allocation_revision_items");
        Assert.Equal(new[] { "SalesAllocationRevisionItemId", "SalesDetailId" }, PropertyNames(pointerForeignKey.Properties));
        Assert.Equal(new[] { "Id", "SalesDetailId" }, PropertyNames(pointerForeignKey.PrincipalKey.Properties));

        var rowVersion = allocation.FindProperty("RowVersion");
        Assert.NotNull(rowVersion);
        Assert.True(rowVersion!.IsConcurrencyToken);
    }

    [Fact]
    public void Inventory_operation_uses_typed_owning_source_fks_and_partial_one_to_one_indexes()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);
        var operation = GetTable(model, "inventory", "inventory_operations");

        var checks = CheckNames(operation);
        Assert.Contains("ck_inventory_operations_type", checks);
        Assert.Contains("ck_inventory_operations_source_shape", checks);
        Assert.Null(operation.FindProperty("RowVersion"));

        AssertPartialUnique(operation, "ProcurementEntryId", "operation_type = 'PROCUREMENT_RECEIPT'");
        AssertPartialUnique(operation, "ProcessingExecutionId", "operation_type = 'PROCESSING'");
        AssertPartialUnique(operation, "OutsourcedSupplyDetailId", "operation_type = 'OUTSOURCED_RECEIPT'");
        AssertPartialUnique(operation, "SalesId", "operation_type = 'SALES_ISSUE'");
        AssertPartialUnique(operation, "SalesAllocationRevisionId", "operation_type = 'SALES_ALLOCATION_REVISION'");
    }

    [Fact]
    public void Inventory_movement_is_append_oriented_typed_ledger_with_allocation_lineage()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);
        var movement = GetTable(model, "inventory", "inventory_movements");

        Assert.Null(movement.FindProperty("RowVersion"));
        Assert.Null(movement.FindProperty("DeletedAt"));
        Assert.Contains(
            movement.GetIndexes(),
            index => index.IsUnique && PropertyNames(index.Properties).SequenceEqual(new[] { "InventoryOperationId", "Sequence" }));

        var checks = CheckNames(movement);
        Assert.Contains("ck_inventory_movements_source_batch", checks);
        Assert.Contains("ck_inventory_movements_typed_object", checks);
        Assert.Contains("ck_inventory_movements_raw_source_shape", checks);
        Assert.Contains("ck_inventory_movements_quantity_sign", checks);
        Assert.Contains("ck_inventory_movements_allocation_lineage", checks);

        var allocationForeignKey = Assert.Single(
            movement.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "sales_allocation_revision_items");
        Assert.Equal(DeleteBehavior.Restrict, allocationForeignKey.DeleteBehavior);
    }

    [Fact]
    public void Inventory_position_has_full_typed_identity_with_nulls_not_distinct()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);
        var position = GetTable(model, "inventory", "inventory_positions");

        var identityProperties = new[]
        {
            "Origin",
            "ProcurementBatchId",
            "OutsourcedSupplyBatchId",
            "InventoryObjectKind",
            "ProcurementProductId",
            "ProcessMaterialId",
            "SalesProductId",
            "StorageLocationId",
            "RawSourceKind",
            "SupplierId",
        };

        var identityIndex = Assert.Single(
            position.GetIndexes(),
            index => index.IsUnique && PropertyNames(index.Properties).SequenceEqual(identityProperties));
        Assert.Equal(false, identityIndex.GetAreNullsDistinct());

        var checks = CheckNames(position);
        Assert.Contains("ck_inventory_positions_source_batch", checks);
        Assert.Contains("ck_inventory_positions_typed_object", checks);
        Assert.Contains("ck_inventory_positions_raw_source_shape", checks);
        Assert.Contains("ck_inventory_positions_balance_quantity", checks);

        var rowVersion = position.FindProperty("RowVersion");
        Assert.NotNull(rowVersion);
        Assert.True(rowVersion!.IsConcurrencyToken);
        Assert.Equal(1L, rowVersion.GetDefaultValue());
    }

    [Fact]
    public void No_M4_entity_uses_a_global_query_filter()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);

        Assert.All(
            model.GetEntityTypes().Where(entityType => entityType.GetSchema() is "outsourced" or "sales" or "inventory"),
            entityType => Assert.Empty(entityType.GetDeclaredQueryFilters()));
    }

    private static void AssertPartialUnique(IEntityType entityType, string propertyName, string filter)
    {
        var index = Assert.Single(
            entityType.GetIndexes(),
            candidate => candidate.IsUnique && PropertyNames(candidate.Properties).SequenceEqual(new[] { propertyName }));
        Assert.Equal(filter, index.GetFilter());
    }

    private static HashSet<string> CheckNames(IEntityType entityType)
    {
        return entityType.GetCheckConstraints().Select(check => check.Name!).ToHashSet(StringComparer.Ordinal);
    }

    private static string[] PropertyNames(IEnumerable<IReadOnlyProperty> properties)
    {
        return properties.Select(property => property.Name).ToArray();
    }

    private static ErpDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=yowthi_erp_v2;Username=postgres",
                PostgreSqlProviderOptions.Configure)
            .Options;

        return new ErpDbContext(options);
    }

    private static IModel GetDesignTimeModel(ErpDbContext context)
    {
        return context.GetService<IDesignTimeModel>().Model;
    }

    private static IEntityType GetTable(IModel model, string schema, string table)
    {
        return model.GetEntityTypes().Single(entityType =>
            entityType.GetSchema() == schema && entityType.GetTableName() == table);
    }
}
