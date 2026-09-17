using Microsoft.Extensions.DependencyInjection;
using YowThi.Erp.Application.DataProtection;
using YowThi.Erp.Infrastructure.Persistence.Product;

namespace YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

public static class ProductDeletionServiceCollectionExtensions
{
    public static IServiceCollection AddProductDeletionPersistence(this IServiceCollection services)
    {
        services.AddScoped<IHardDeleteProcurementProductExecutor, PostgreSqlHardDeleteProcurementProductExecutor>();
        services.AddScoped<IHardDeleteSalesProductExecutor, PostgreSqlHardDeleteSalesProductExecutor>();
        services.AddScoped<IHardDeleteSalesProductGroupExecutor, PostgreSqlHardDeleteSalesProductGroupExecutor>();
        services.AddScoped<IProductHardDeleteOptionsReader, EfProductHardDeleteOptionsReader>();
        return services;
    }
}