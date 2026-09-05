using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.DataProtection;
using YowThi.Erp.Application.Finance;
using YowThi.Erp.Application.Infrastructure;
using YowThi.Erp.Application.Inventory;
using YowThi.Erp.Application.Labor;
using YowThi.Erp.Application.Outsourced;
using YowThi.Erp.Application.Party;
using YowThi.Erp.Application.Product;
using YowThi.Erp.Application.Procurement;
using YowThi.Erp.Application.Processing;
using YowThi.Erp.Application.Sales;
using YowThi.Erp.Application.SalesHandling;
using YowThi.Erp.Infrastructure.Persistence.Concurrency;
using YowThi.Erp.Infrastructure.Persistence.DataProtection;
using YowThi.Erp.Infrastructure.Persistence.Finance;
using YowThi.Erp.Infrastructure.Persistence.Idempotency;
using YowThi.Erp.Infrastructure.Persistence.Infrastructure;
using YowThi.Erp.Infrastructure.Persistence.Inventory;
using YowThi.Erp.Infrastructure.Persistence.Labor;
using YowThi.Erp.Infrastructure.Persistence.Outsourced;
using YowThi.Erp.Infrastructure.Persistence.Party;
using YowThi.Erp.Infrastructure.Persistence.Product;
using YowThi.Erp.Infrastructure.Persistence.Procurement;
using YowThi.Erp.Infrastructure.Persistence.Processing;
using YowThi.Erp.Infrastructure.Persistence.Sales;
using YowThi.Erp.Infrastructure.Persistence.SalesHandling;
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
        services.AddScoped<ICloseProcurementBatchExecutor, PostgreSqlCloseProcurementBatchExecutor>();
        services.AddScoped<IReopenProcurementBatchExecutor, PostgreSqlReopenProcurementBatchExecutor>();
        services.AddScoped<IProcurementEntryOptionsReader, EfProcurementEntryOptionsReader>();
        services.AddScoped<IConfirmOutsourcedSupplyDetailExecutor, PostgreSqlConfirmOutsourcedSupplyDetailExecutor>();
        services.AddScoped<ICloseOutsourcedSupplyBatchExecutor, PostgreSqlCloseOutsourcedSupplyBatchExecutor>();
        services.AddScoped<IOutsourcedSupplyDetailOptionsReader, EfOutsourcedSupplyDetailOptionsReader>();
        services.AddScoped<IConfirmProcessingExecutionExecutor, PostgreSqlConfirmProcessingExecutionExecutor>();
        services.AddScoped<IProcessingExecutionOptionsReader, EfProcessingExecutionOptionsReader>();
        services.AddScoped<IConfirmSalesExecutor, PostgreSqlConfirmSalesExecutor>();
        services.AddScoped<ICorrectSalesAllocationExecutor, PostgreSqlCorrectSalesAllocationExecutor>();
        services.AddScoped<IRecordSalesPackagingWorkExecutor, PostgreSqlRecordSalesPackagingWorkExecutor>();
        services.AddScoped<ISalesPackagingItemLifecycleExecutor, PostgreSqlSalesPackagingItemLifecycleExecutor>();
        services.AddScoped<ISalesProductGroupLifecycleExecutor, PostgreSqlSalesProductGroupLifecycleExecutor>();
        services.AddScoped<IContainerLifecycleExecutor, PostgreSqlContainerLifecycleExecutor>();
        services.AddScoped<IWarehouseLifecycleExecutor, PostgreSqlWarehouseLifecycleExecutor>();
        services.AddScoped<IConfirmEmployeeDailyWageExecutor, PostgreSqlConfirmEmployeeDailyWageExecutor>();
        services.AddScoped<IAddPayableAdjustmentExecutor, PostgreSqlAddPayableAdjustmentExecutor>();
        services.AddScoped<IPayPayableExecutor, PostgreSqlPayPayableExecutor>();
        services.AddScoped<IReceiveReceivableExecutor, PostgreSqlReceiveReceivableExecutor>();
        services.AddScoped<ICorrectPaymentAmountExecutor, PostgreSqlCorrectPaymentAmountExecutor>();
        services.AddScoped<ICorrectReceiptAmountExecutor, PostgreSqlCorrectReceiptAmountExecutor>();
        services.AddScoped<ICorrectPayableAdjustmentExecutor, PostgreSqlCorrectPayableAdjustmentExecutor>();
        services.AddScoped<ITransferInventoryExecutor, PostgreSqlTransferInventoryExecutor>();
        services.AddScoped<IAdjustInventoryExecutor, PostgreSqlAdjustInventoryExecutor>();
        services.AddScoped<ISupplierLifecycleExecutor, PostgreSqlSupplierLifecycleExecutor>();
        services.AddScoped<IFarmerLifecycleExecutor, PostgreSqlFarmerLifecycleExecutor>();
        services.AddScoped<IEmployeeLifecycleExecutor, PostgreSqlEmployeeLifecycleExecutor>();
        services.AddScoped<ICustomerLifecycleExecutor, PostgreSqlCustomerLifecycleExecutor>();
        services.AddScoped<IOutsourcedVendorLifecycleExecutor, PostgreSqlOutsourcedVendorLifecycleExecutor>();
        services.AddScoped<IHardDeleteSupplierExecutor, PostgreSqlHardDeleteSupplierExecutor>();
        services.AddScoped<IHardDeleteCustomerExecutor, PostgreSqlHardDeleteCustomerExecutor>();
        services.AddScoped<IHardDeleteOutsourcedVendorExecutor, PostgreSqlHardDeleteOutsourcedVendorExecutor>();
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
