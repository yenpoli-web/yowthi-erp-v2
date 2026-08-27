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
    public void ErpDbContext_has_no_mapped_relations_before_M1()
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=yowthi_erp_v2;Username=postgres",
                PostgreSqlProviderOptions.Configure)
            .Options;

        using var context = new ErpDbContext(options);

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
        Assert.Empty(context.Model.GetEntityTypes());
    }
}
