using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using YowThi.Erp.Infrastructure.Persistence;
using YowThi.Erp.Infrastructure.Persistence.Concurrency;

namespace YowThi.Erp.Infrastructure.Migrations.Persistence;

public sealed class ErpDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ErpDbContext>
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string DesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=yowthi_erp_v2;Username=postgres";

    public ErpDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = DesignTimeConnectionString;
        }

        var optionsBuilder = new DbContextOptionsBuilder<ErpDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsqlOptions =>
        {
            PostgreSqlProviderOptions.Configure(npgsqlOptions);
            npgsqlOptions.MigrationsAssembly(
                typeof(ErpDbContextDesignTimeFactory).Assembly.GetName().Name);
        });
        optionsBuilder.AddInterceptors(new RowVersionSaveChangesInterceptor());

        return new ErpDbContext(optionsBuilder.Options);
    }
}
