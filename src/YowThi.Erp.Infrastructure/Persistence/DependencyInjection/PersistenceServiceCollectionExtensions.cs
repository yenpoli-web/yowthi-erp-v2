using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using YowThi.Erp.Infrastructure.Persistence.Concurrency;

namespace YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddErpPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton<RowVersionSaveChangesInterceptor>();
        services.AddDbContext<ErpDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(connectionString, PostgreSqlProviderOptions.Configure);
            options.AddInterceptors(
                serviceProvider.GetRequiredService<RowVersionSaveChangesInterceptor>());
        });

        return services;
    }
}
