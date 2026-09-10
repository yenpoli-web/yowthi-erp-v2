using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using YowThi.Erp.Infrastructure.Persistence;

namespace YowThi.Erp.ArchitectureTests;

public sealed class M3RelationalModelTests
{
    [Fact]
    public void M3_maps_exactly_procurement_and_processing_relations()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);

        var expected = new[]
        {
            "processing.processing_execution_inputs",
            "processing.processing_execution_outputs",
            "processing.processing_executions",
            "procurement.procurement_batches",
            "procurement.procurement_entries",
        };

        var actual = model.GetEntityTypes()
            .Where(entityType => entityType.GetSchema() is "procurement" or "processing")
            .Select(entityType => $"{entityType.GetSchema()}.{entityType.GetTableName()}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.OrderBy(value => value, StringComparer.Ordinal), actual);
    }

    [Fact]
    public void Procurement_batch_identity_route_binding_and_lifecycle_are_structural()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);
        var batch = GetTable(model, "procurement", "procurement_batches");

        Assert.Contains(
            batch.GetIndexes(),
            index => index.IsUnique && PropertyNames(index.Properties).SequenceEqual(new[] { "ProcurementDate", "ProcurementProductId" }) && index.GetFilter() is null);

        var routeVersionForeignKey = Assert.Single(
            batch.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "processing_route_versions");
        Assert.Equal(new[] { "ProcessingRouteVersionId", "ProcessingRouteId" }, PropertyNames(routeVersionForeignKey.Properties));
        Assert.Equal(new[] { "Id", "ProcessingRouteId" }, PropertyNames(routeVersionForeignKey.PrincipalKey.Properties));
        Assert.Equal(DeleteBehavior.Restrict, routeVersionForeignKey.DeleteBehavior);

        var receiptLocationForeignKey = Assert.Single(
            batch.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "storage_locations");
        Assert.Equal(new[] { "ReceiptStorageLocationId" }, PropertyNames(receiptLocationForeignKey.Properties));
        Assert.Equal(DeleteBehavior.Restrict, receiptLocationForeignKey.DeleteBehavior);
        Assert.Contains(
            batch.GetIndexes(),
            index => PropertyNames(index.Properties).SequenceEqual(new[] { "ReceiptStorageLocationId" }));

        var checks = CheckNames(batch);
        Assert.Contains("ck_procurement_batches_status", checks);
        Assert.Contains("ck_procurement_batches_lifecycle_status", checks);
        Assert.Contains("ck_procurement_batches_route_binding", checks);
        Assert.Contains("ck_procurement_batches_completion_state", checks);
        Assert.Contains("ck_procurement_batches_closing_state", checks);
        Assert.Contains("ck_procurement_batches_deleted_pair", checks);

        var rowVersion = batch.FindProperty("RowVersion");
        Assert.NotNull(rowVersion);
        Assert.True(rowVersion!.IsConcurrencyToken);
        Assert.Equal(1L, rowVersion.GetDefaultValue());
    }

    [Fact]
    public void Procurement_entry_uses_typed_party_source_and_row_local_amount_formula()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);
        var entry = GetTable(model, "procurement", "procurement_entries");
        var checks = CheckNames(entry);

        Assert.Contains("ck_procurement_entries_source_type", checks);
        Assert.Contains("ck_procurement_entries_typed_source", checks);
        Assert.Contains("ck_procurement_entries_net_quantity", checks);
        Assert.Contains("ck_procurement_entries_unit_price", checks);
        Assert.Contains("ck_procurement_entries_amount", checks);
        Assert.Contains("ck_procurement_entries_unit_code_snapshot", checks);

        foreach (var principal in new[] { "procurement_batches", "suppliers", "farmers" })
        {
            var foreignKey = Assert.Single(
                entry.GetForeignKeys(),
                candidate => candidate.PrincipalEntityType.GetTableName() == principal);
            Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        }

        Assert.Equal(typeof(long), entry.FindProperty("AmountThb")!.ClrType);
        Assert.True(entry.FindProperty("RowVersion")!.IsConcurrencyToken);
        Assert.DoesNotContain(entry.GetIndexes(), index => index.IsUnique);
    }

    [Fact]
    public void Processing_execution_has_same_version_module_fk_without_batch_route_version_composite_fk()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);
        var execution = GetTable(model, "processing", "processing_executions");

        var moduleForeignKey = Assert.Single(
            execution.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "processing_modules");
        Assert.Equal(new[] { "ProcessingModuleId", "ProcessingRouteVersionId" }, PropertyNames(moduleForeignKey.Properties));
        Assert.Equal(new[] { "Id", "ProcessingRouteVersionId" }, PropertyNames(moduleForeignKey.PrincipalKey.Properties));

        var routeVersionForeignKey = Assert.Single(
            execution.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "processing_route_versions");
        Assert.Equal(new[] { "ProcessingRouteVersionId" }, PropertyNames(routeVersionForeignKey.Properties));

        var batchForeignKey = Assert.Single(
            execution.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "procurement_batches");
        Assert.Equal(new[] { "ProcurementBatchId" }, PropertyNames(batchForeignKey.Properties));
        Assert.DoesNotContain(
            execution.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "procurement_batches" && foreignKey.Properties.Count > 1);

        var checks = CheckNames(execution);
        Assert.Contains("ck_processing_executions_execution_mode", checks);
        Assert.Contains("ck_processing_executions_negative_inventory_policy", checks);
        Assert.Contains("ck_processing_executions_source_shape", checks);
        Assert.True(execution.FindProperty("RowVersion")!.IsConcurrencyToken);
    }

    [Fact]
    public void Processing_input_is_one_to_one_and_has_measurement_decimal_shape_checks()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);
        var input = GetTable(model, "processing", "processing_execution_inputs");

        Assert.Equal(new[] { "ProcessingExecutionId" }, PropertyNames(input.FindPrimaryKey()!.Properties));
        var executionForeignKey = Assert.Single(
            input.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "processing_executions");
        Assert.True(executionForeignKey.IsUnique);
        Assert.Equal(DeleteBehavior.Restrict, executionForeignKey.DeleteBehavior);

        var deletedByForeignKey = Assert.Single(
            input.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "accounts");
        Assert.Equal(new[] { "DeletedByAccountId" }, PropertyNames(deletedByForeignKey.Properties));
        Assert.Equal(DeleteBehavior.Restrict, deletedByForeignKey.DeleteBehavior);

        var checks = CheckNames(input);
        Assert.Contains("ck_processing_execution_inputs_consumption_basis", checks);
        Assert.Contains("ck_processing_execution_inputs_consumed_quantity", checks);
        Assert.Contains("ck_processing_execution_inputs_scale_reading", checks);
        Assert.Contains("ck_processing_execution_inputs_derived_net_quantity", checks);
        Assert.Contains("ck_processing_execution_inputs_shape", checks);
        Assert.Contains("ck_processing_execution_inputs_row_version", checks);
        Assert.Contains("ck_processing_execution_inputs_deleted_pair", checks);
        var inputRowVersion = input.FindProperty("RowVersion");
        Assert.NotNull(inputRowVersion);
        Assert.True(inputRowVersion!.IsConcurrencyToken);
        Assert.Equal(1L, inputRowVersion.GetDefaultValue());
        Assert.Contains(
            input.GetIndexes(),
            index => PropertyNames(index.Properties).SequenceEqual(new[] { "DeletedByAccountId" }));
    }

    [Fact]
    public void Processing_output_identity_and_final_packaging_formula_are_structural()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);
        var output = GetTable(model, "processing", "processing_execution_outputs");

        Assert.Contains(
            output.GetIndexes(),
            index => index.IsUnique && PropertyNames(index.Properties).SequenceEqual(new[] { "ProcessingExecutionId", "ProcessingModuleOutputId" }));

        var oneSalesProduct = Assert.Single(
            output.GetIndexes(),
            index => index.IsUnique && PropertyNames(index.Properties).SequenceEqual(new[] { "ProcessingExecutionId" }));
        Assert.Equal("output_kind_snapshot = 'SALES_PRODUCT'", oneSalesProduct.GetFilter());

        var checks = CheckNames(output);
        Assert.Contains("ck_processing_execution_outputs_kind", checks);
        Assert.Contains("ck_processing_execution_outputs_wage_rate", checks);
        Assert.Contains("ck_processing_execution_outputs_scale_reading", checks);
        Assert.Contains("ck_processing_execution_outputs_completed_quantity", checks);
        Assert.Contains("ck_processing_execution_outputs_shape", checks);
        Assert.Contains("ck_processing_execution_outputs_final_packaging_formula", checks);
        Assert.Contains("ck_processing_execution_outputs_row_version", checks);
        Assert.Contains("ck_processing_execution_outputs_deleted_pair", checks);

        var definitionForeignKey = Assert.Single(
            output.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "processing_module_outputs");
        Assert.Equal(new[] { "ProcessingModuleOutputId" }, PropertyNames(definitionForeignKey.Properties));
        Assert.Equal(DeleteBehavior.Restrict, definitionForeignKey.DeleteBehavior);

        var deletedByForeignKey = Assert.Single(
            output.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "accounts");
        Assert.Equal(new[] { "DeletedByAccountId" }, PropertyNames(deletedByForeignKey.Properties));
        Assert.Equal(DeleteBehavior.Restrict, deletedByForeignKey.DeleteBehavior);

        Assert.Equal(ValueGenerated.Never, output.FindProperty("Id")!.ValueGenerated);
        var outputRowVersion = output.FindProperty("RowVersion");
        Assert.NotNull(outputRowVersion);
        Assert.True(outputRowVersion!.IsConcurrencyToken);
        Assert.Equal(1L, outputRowVersion.GetDefaultValue());
        Assert.Contains(
            output.GetIndexes(),
            index => PropertyNames(index.Properties).SequenceEqual(new[] { "DeletedByAccountId" }));
    }

    [Fact]
    public void No_M3_entity_uses_a_global_query_filter()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);

        var m3Entities = model.GetEntityTypes()
            .Where(entityType => entityType.GetSchema() is "procurement" or "processing")
            .ToArray();

        Assert.Equal(5, m3Entities.Length);
        Assert.All(m3Entities, entityType => Assert.Empty(entityType.GetDeclaredQueryFilters()));
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
