using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Outsourced;
using YowThi.Erp.Application.Procurement;
using YowThi.Erp.Application.Processing;
using YowThi.Erp.Application.Sales;
using YowThi.Erp.Infrastructure.Persistence.Concurrency;
using YowThi.Erp.Infrastructure.Persistence.Idempotency;
using YowThi.Erp.Infrastructure.Persistence.Outsourced;
using YowThi.Erp.Infrastructure.Persistence.Procurement;
using YowThi.Erp.Infrastructure.Persistence.Processing;
using YowThi.Erp.Infrastructure.Persistence.Sales;
using YowThi.Erp.Infrastructure.Persistence.Transactions;

namespace YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddErpPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICommandRequestHasher, Sha256CommandRequestHasher>();
        services.AddScoped<ICommandTransactionRunner, EfCommandTransactionRunner>();
        services.AddScoped<IConfirmProcurementEntryExecutor, PostgreSqlConfirmProcurementEntryExecutor>();
        services.AddScoped<IProcurementEntryOptionsReader, EfProcurementEntryOptionsReader>();
        services.AddScoped<IConfirmOutsourcedSupplyDetailExecutor, PostgreSqlConfirmOutsourcedSupplyDetailExecutor>();
        services.AddScoped<IOutsourcedSupplyDetailOptionsReader, EfOutsourcedSupplyDetailOptionsReader>();
        services.AddScoped<IConfirmProcessingExecutionExecutor, PostgreSqlConfirmProcessingExecutionExecutor>();
        services.AddScoped<IProcessingExecutionOptionsReader, EfProcessingExecutionOptionsReader>();
        services.AddScoped<IConfirmSalesExecutor, PostgreSqlConfirmSalesExecutor>();
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