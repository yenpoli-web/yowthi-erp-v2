using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using YowThi.Erp.Infrastructure.Persistence;

namespace YowThi.Erp.ArchitectureTests;

public sealed class FullRelationalModelTests
{
    private static readonly string[] ExpectedSchemas =
    [
        "audit",
        "finance",
        "infrastructure",
        "inventory",
        "labor",
        "outsourced",
        "party",
        "processing",
        "processing_config",
        "procurement",
        "product",
        "sales",
        "sales_handling",
        "system",
    ];

    [Fact]
    public void Full_model_contains_exactly_fifty_five_relations_across_fourteen_schemas()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);
        var entityTypes = model.GetEntityTypes().ToArray();

        Assert.Equal(55, entityTypes.Length);
        Assert.All(entityTypes, entityType =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entityType.GetSchema()));
            Assert.False(string.IsNullOrWhiteSpace(entityType.GetTableName()));
        });

        var actualSchemas = entityTypes
            .Select(entityType => entityType.GetSchema()!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedSchemas, actualSchemas);
    }

    [Fact]
    public void Full_model_has_no_global_query_filters_or_cascade_delete()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);

        Assert.All(model.GetEntityTypes(), entityType => Assert.Empty(entityType.GetDeclaredQueryFilters()));
        Assert.DoesNotContain(
            model.GetEntityTypes().SelectMany(entityType => entityType.GetForeignKeys()),
            foreignKey => foreignKey.DeleteBehavior is DeleteBehavior.Cascade or DeleteBehavior.ClientCascade);
    }

    [Fact]
    public void Full_model_uses_explicit_row_version_properties_and_never_xmin()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);

        Assert.DoesNotContain(
            model.GetEntityTypes().SelectMany(entityType => entityType.GetProperties()),
            property => string.Equals(property.Name, "xmin", StringComparison.OrdinalIgnoreCase));

        foreach (var property in model.GetEntityTypes()
                     .SelectMany(entityType => entityType.GetProperties())
                     .Where(property => property.Name == "RowVersion"))
        {
            Assert.Equal(typeof(long), property.ClrType);
            Assert.True(property.IsConcurrencyToken);
        }
    }

    [Fact]
    public void All_account_id_references_are_real_foreign_keys_to_system_accounts()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);

        foreach (var entityType in model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties()
                         .Where(property => property.Name.EndsWith("AccountId", StringComparison.Ordinal)))
            {
                Assert.Contains(
                    entityType.GetForeignKeys(),
                    foreignKey => foreignKey.Properties.Contains(property) &&
                                  foreignKey.PrincipalEntityType.GetSchema() == "system" &&
                                  foreignKey.PrincipalEntityType.GetTableName() == "accounts");
            }
        }
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
}
