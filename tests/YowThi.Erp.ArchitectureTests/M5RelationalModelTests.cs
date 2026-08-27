using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using YowThi.Erp.Infrastructure.Persistence;

namespace YowThi.Erp.ArchitectureTests;

public sealed class M5RelationalModelTests
{
    [Fact]
    public void M5_maps_exactly_sales_handling_and_labor_relations()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);

        var expected = new[]
        {
            "labor.employee_daily_wages",
            "labor.processing_wage_component_sources",
            "labor.processing_wage_components",
            "labor.sales_packaging_wage_components",
            "sales_handling.sales_packaging_items",
            "sales_handling.sales_packaging_work_records",
        };

        var actual = model.GetEntityTypes()
            .Where(entityType => entityType.GetSchema() is "sales_handling" or "labor")
            .Select(entityType => $"{entityType.GetSchema()}.{entityType.GetTableName()}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.OrderBy(value => value, StringComparer.Ordinal), actual);
    }

    [Fact]
    public void Sales_packaging_item_is_bilingual_master_without_wage_rate()
    {
        using var context = CreateContext();
        var item = GetTable(GetDesignTimeModel(context), "sales_handling", "sales_packaging_items");

        Assert.Contains("ck_sales_packaging_items_name_present", CheckNames(item));
        Assert.Null(item.FindProperty("WageRate"));

        var rowVersion = item.FindProperty("RowVersion");
        Assert.NotNull(rowVersion);
        Assert.True(rowVersion!.IsConcurrencyToken);
        Assert.Equal(1L, rowVersion.GetDefaultValue());
    }

    [Fact]
    public void Sales_packaging_work_record_has_day_rate_shape_and_no_unconfirmed_business_unique()
    {
        using var context = CreateContext();
        var record = GetTable(GetDesignTimeModel(context), "sales_handling", "sales_packaging_work_records");

        Assert.Contains("ck_sales_packaging_work_records_confirmed_wage", CheckNames(record));
        Assert.Contains("ck_sales_packaging_work_records_deleted_pair", CheckNames(record));
        Assert.DoesNotContain(record.GetIndexes(), index => index.IsUnique);

        Assert.Null(record.FindProperty("Quantity"));
        Assert.Null(record.FindProperty("Weight"));
        Assert.Null(record.FindProperty("BoxCount"));
        Assert.Null(record.FindProperty("Hours"));
        Assert.Null(record.FindProperty("UnitRate"));

        var rowVersion = record.FindProperty("RowVersion");
        Assert.NotNull(rowVersion);
        Assert.True(rowVersion!.IsConcurrencyToken);
    }

    [Fact]
    public void Employee_daily_wage_uses_date_employee_identity_and_row_local_total_formula()
    {
        using var context = CreateContext();
        var wage = GetTable(GetDesignTimeModel(context), "labor", "employee_daily_wages");

        Assert.Contains(
            wage.GetIndexes(),
            index => index.IsUnique && PropertyNames(index.Properties).SequenceEqual(new[] { "WorkDate", "EmployeeId" }));

        var checks = CheckNames(wage);
        Assert.Contains("ck_employee_daily_wages_processing_total", checks);
        Assert.Contains("ck_employee_daily_wages_packaging_total", checks);
        Assert.Contains("ck_employee_daily_wages_total", checks);
        Assert.Contains("ck_employee_daily_wages_total_formula", checks);

        var rowVersion = wage.FindProperty("RowVersion");
        Assert.NotNull(rowVersion);
        Assert.True(rowVersion!.IsConcurrencyToken);
        Assert.Equal(1L, rowVersion.GetDefaultValue());
    }

    [Fact]
    public void Processing_wage_component_uses_aggregate_then_floor_formula()
    {
        using var context = CreateContext();
        var component = GetTable(GetDesignTimeModel(context), "labor", "processing_wage_components");

        var checks = CheckNames(component);
        Assert.Contains("ck_processing_wage_components_configured_rate", checks);
        Assert.Contains("ck_processing_wage_components_applied_rate", checks);
        Assert.Contains("ck_processing_wage_components_aggregated_quantity", checks);
        Assert.Contains("ck_processing_wage_components_amount", checks);
        Assert.Contains("ck_processing_wage_components_amount_formula", checks);
        Assert.Null(component.FindProperty("RowVersion"));

        Assert.Single(component.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "employee_daily_wages");
        Assert.Single(component.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType.GetTableName() == "processing_module_outputs");
    }

    [Fact]
    public void Processing_wage_source_lineage_prevents_execution_output_double_inclusion()
    {
        using var context = CreateContext();
        var source = GetTable(GetDesignTimeModel(context), "labor", "processing_wage_component_sources");

        Assert.Equal(
            new[] { "ProcessingWageComponentId", "ProcessingExecutionOutputId" },
            PropertyNames(source.FindPrimaryKey()!.Properties));

        Assert.Contains(
            source.GetIndexes(),
            index => index.IsUnique && PropertyNames(index.Properties).SequenceEqual(new[] { "ProcessingExecutionOutputId" }));

        Assert.Null(source.FindProperty("RowVersion"));
    }

    [Fact]
    public void Sales_packaging_wage_component_allows_each_work_record_only_once()
    {
        using var context = CreateContext();
        var component = GetTable(GetDesignTimeModel(context), "labor", "sales_packaging_wage_components");

        Assert.Contains("ck_sales_packaging_wage_components_amount", CheckNames(component));
        Assert.Contains(
            component.GetIndexes(),
            index => index.IsUnique && PropertyNames(index.Properties).SequenceEqual(new[] { "SalesPackagingWorkRecordId" }));
        Assert.Null(component.FindProperty("RowVersion"));
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
