using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using YowThi.Erp.Domain.ProcessingConfiguration;
using YowThi.Erp.Domain.Product;
using YowThi.Erp.Infrastructure.Persistence;

namespace YowThi.Erp.ArchitectureTests;

public sealed class M2RelationalModelTests
{
    [Fact]
    public void M2_foundation_schemas_remain_exactly_twenty_one_relations()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);

        var expected = new[]
        {
            "infrastructure.containers",
            "infrastructure.storage_locations",
            "infrastructure.warehouses",
            "party.customers",
            "party.employees",
            "party.farmers",
            "party.outsourced_vendors",
            "party.suppliers",
            "processing_config.process_materials",
            "processing_config.processing_module_outputs",
            "processing_config.processing_modules",
            "processing_config.processing_route_versions",
            "processing_config.processing_routes",
            "processing_config.route_input_configs",
            "product.procurement_products",
            "product.sales_product_groups",
            "product.sales_products",
            "system.account_capability_grants",
            "system.accounts",
            "system.command_executions",
            "system.outbox_messages",
        };

        var actual = model.GetEntityTypes()
            .Where(entityType => entityType.GetSchema() is "system" or "party" or "infrastructure" or "product" or "processing_config")
            .Select(entityType => $"{entityType.GetSchema()}.{entityType.GetTableName()}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.OrderBy(value => value, StringComparer.Ordinal), actual);
    }

    [Fact]
    public void M2_mutable_master_and_route_material_rows_use_explicit_row_version()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);

        foreach (var (schema, table) in new[]
        {
            ("infrastructure", "containers"),
            ("infrastructure", "warehouses"),
            ("infrastructure", "storage_locations"),
            ("product", "procurement_products"),
            ("product", "sales_product_groups"),
            ("product", "sales_products"),
            ("processing_config", "processing_routes"),
            ("processing_config", "process_materials"),
        })
        {
            var rowVersion = GetTable(model, schema, table).FindProperty("RowVersion");
            Assert.NotNull(rowVersion);
            Assert.True(rowVersion!.IsConcurrencyToken);
            Assert.Equal(typeof(long), rowVersion.ClrType);
            Assert.Equal(1L, rowVersion.GetDefaultValue());
        }

        foreach (var table in new[] { "processing_route_versions", "route_input_configs", "processing_modules", "processing_module_outputs" })
        {
            Assert.Null(GetTable(model, "processing_config", table).FindProperty("RowVersion"));
        }
    }

    [Fact]
    public void M2_lifecycle_rows_have_restrict_actor_foreign_keys()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);

        foreach (var (schema, table) in new[]
        {
            ("infrastructure", "containers"),
            ("infrastructure", "warehouses"),
            ("infrastructure", "storage_locations"),
            ("product", "procurement_products"),
            ("product", "sales_product_groups"),
            ("product", "sales_products"),
            ("processing_config", "processing_routes"),
            ("processing_config", "process_materials"),
        })
        {
            var actorForeignKeys = GetTable(model, schema, table).GetForeignKeys()
                .Where(foreignKey => foreignKey.PrincipalEntityType.GetSchema() == "system" && foreignKey.PrincipalEntityType.GetTableName() == "accounts")
                .ToArray();

            Assert.Equal(2, actorForeignKeys.Length);
            Assert.All(actorForeignKeys, foreignKey => Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));
        }
    }

    [Fact]
    public void Infrastructure_and_product_constraints_match_the_consolidated_baseline()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);

        var containerChecks = CheckNames(GetTable(model, "infrastructure", "containers"));
        Assert.Contains("ck_containers_tare_weight", containerChecks);

        Assert.DoesNotContain(GetTable(model, "infrastructure", "warehouses").GetIndexes(), index => index.IsUnique);
        Assert.DoesNotContain(GetTable(model, "infrastructure", "storage_locations").GetIndexes(), index => index.IsUnique);

        var procurementProduct = GetTable(model, "product", "procurement_products");
        Assert.Contains("ck_procurement_products_unit_code", CheckNames(procurementProduct));
        Assert.Contains(procurementProduct.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "storage_locations" && foreignKey.DeleteBehavior == DeleteBehavior.Restrict);

        var salesProduct = GetTable(model, "product", "sales_products");
        var salesChecks = CheckNames(salesProduct);
        Assert.Contains("ck_sales_products_pricing_basis", salesChecks);
        Assert.Contains("ck_sales_products_pricing_shape", salesChecks);
        Assert.Contains("ck_sales_products_packaging_weight", salesChecks);
        Assert.Contains("ck_sales_products_sales_weight", salesChecks);
        Assert.Equal(typeof(SalesPricingBasis), salesProduct.FindProperty("PricingBasis")!.ClrType);
        Assert.Equal("text", salesProduct.FindProperty("PricingBasis")!.GetColumnType());
        Assert.Contains(salesProduct.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "sales_product_groups" && foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
    }

    [Fact]
    public void Route_version_keys_and_one_active_index_are_structural()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);
        var routeVersion = GetTable(model, "processing_config", "processing_route_versions");

        Assert.Contains(routeVersion.GetKeys(), key => PropertyNames(key.Properties).SequenceEqual(new[] { "Id", "ProcessingRouteId" }));
        Assert.Contains(routeVersion.GetIndexes(), index => index.IsUnique && PropertyNames(index.Properties).SequenceEqual(new[] { "ProcessingRouteId", "VersionNumber" }));

        var activeIndex = Assert.Single(
            routeVersion.GetIndexes(),
            index => index.IsUnique && PropertyNames(index.Properties).SequenceEqual(new[] { "ProcessingRouteId" }));
        Assert.Equal("status = 'ACTIVE'", activeIndex.GetFilter());
        Assert.Equal(typeof(ProcessingRouteVersionStatus), routeVersion.FindProperty("Status")!.ClrType);
        Assert.Equal("text", routeVersion.FindProperty("Status")!.GetColumnType());
    }

    [Fact]
    public void Container_config_and_process_material_membership_are_explicit()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);

        var inputConfig = GetTable(model, "processing_config", "route_input_configs");
        Assert.Contains("ck_route_input_configs_container_shape", CheckNames(inputConfig));
        Assert.Contains("ck_route_input_configs_container_count", CheckNames(inputConfig));
        Assert.Equal(new[] { "ProcessingRouteVersionId" }, PropertyNames(inputConfig.FindPrimaryKey()!.Properties));

        var material = GetTable(model, "processing_config", "process_materials");
        Assert.Contains("ck_process_materials_container_shape", CheckNames(material));
        Assert.Contains(material.GetKeys(), key => PropertyNames(key.Properties).SequenceEqual(new[] { "Id", "ProcessingRouteVersionId" }));
    }

    [Fact]
    public void Processing_module_input_material_is_same_route_version_by_composite_fk()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);
        var module = GetTable(model, "processing_config", "processing_modules");

        var materialForeignKey = Assert.Single(
            module.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "process_materials");
        Assert.Equal(new[] { "InputProcessMaterialId", "ProcessingRouteVersionId" }, PropertyNames(materialForeignKey.Properties));
        Assert.Equal(new[] { "Id", "ProcessingRouteVersionId" }, PropertyNames(materialForeignKey.PrincipalKey.Properties));
        Assert.Equal(DeleteBehavior.Restrict, materialForeignKey.DeleteBehavior);
        Assert.Contains(module.GetKeys(), key => PropertyNames(key.Properties).SequenceEqual(new[] { "Id", "ProcessingRouteVersionId" }));
        Assert.Contains("ck_processing_modules_execution_mode", CheckNames(module));
        Assert.Contains("ck_processing_modules_negative_inventory_policy", CheckNames(module));
        Assert.Equal(typeof(ProcessingExecutionMode), module.FindProperty("ExecutionMode")!.ClrType);
    }

    [Fact]
    public void Module_output_uses_typed_output_shape_and_same_version_composite_fks()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);
        var output = GetTable(model, "processing_config", "processing_module_outputs");

        var checks = CheckNames(output);
        Assert.Contains("ck_processing_module_outputs_sequence", checks);
        Assert.Contains("ck_processing_module_outputs_kind", checks);
        Assert.Contains("ck_processing_module_outputs_typed_output", checks);
        Assert.Contains("ck_processing_module_outputs_default_wage_rate", checks);

        Assert.Contains(output.GetIndexes(), index => index.IsUnique && PropertyNames(index.Properties).SequenceEqual(new[] { "ProcessingModuleId", "OutputSequence" }));

        var moduleForeignKey = Assert.Single(
            output.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "processing_modules");
        Assert.Equal(new[] { "ProcessingModuleId", "ProcessingRouteVersionId" }, PropertyNames(moduleForeignKey.Properties));
        Assert.Equal(new[] { "Id", "ProcessingRouteVersionId" }, PropertyNames(moduleForeignKey.PrincipalKey.Properties));

        var materialForeignKey = Assert.Single(
            output.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "process_materials");
        Assert.Equal(new[] { "ProcessMaterialId", "ProcessingRouteVersionId" }, PropertyNames(materialForeignKey.Properties));
        Assert.Equal(new[] { "Id", "ProcessingRouteVersionId" }, PropertyNames(materialForeignKey.PrincipalKey.Properties));

        var salesProductForeignKey = Assert.Single(
            output.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "sales_products");
        Assert.Equal(DeleteBehavior.Restrict, salesProductForeignKey.DeleteBehavior);
        Assert.Equal(typeof(ProcessingOutputKind), output.FindProperty("OutputKind")!.ClrType);
        Assert.Equal("text", output.FindProperty("OutputKind")!.GetColumnType());
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
