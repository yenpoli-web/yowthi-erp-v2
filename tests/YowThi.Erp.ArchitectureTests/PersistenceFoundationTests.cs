using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Infrastructure.Persistence;

namespace YowThi.Erp.ArchitectureTests;

public sealed class PersistenceFoundationTests
{
    [Fact]
    public void PostgreSql_provider_baseline_is_version_18_with_system_migration_history()
    {
        Assert.Equal(18, PostgreSqlProviderOptions.MajorVersion);
        Assert.Equal(0, PostgreSqlProviderOptions.MinorVersion);
        Assert.Equal("__ef_migrations_history", PostgreSqlProviderOptions.MigrationsHistoryTableName);
        Assert.Equal("system", PostgreSqlProviderOptions.MigrationsHistorySchema);
    }

    [Fact]
    public void ErpDbContext_contains_exactly_M1_relations()
    {
        using var context = CreateContext();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
        Assert.Equal(8, context.Model.GetEntityTypes().Count());
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
}
