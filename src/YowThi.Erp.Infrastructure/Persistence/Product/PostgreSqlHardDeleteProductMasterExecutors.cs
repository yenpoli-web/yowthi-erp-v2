using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.DataProtection;

namespace YowThi.Erp.Infrastructure.Persistence.Product;

internal sealed class PostgreSqlHardDeleteProcurementProductExecutor(ErpDbContext db, ICommandTransactionRunner tx, TimeProvider time) : IHardDeleteProcurementProductExecutor
{
    private readonly PostgreSqlHardDeleteProductMasterSupport _support = new(db, tx, time);
    public ValueTask<ApplicationResult<HardDeleteProductMasterResult>> ExecuteAsync(HardDeleteProductMasterExecution execution, CancellationToken cancellationToken) =>
        _support.ExecuteAsync(execution, "HardDeleteProcurementProduct", "product.procurement_products", "product.procurement-product",
            "SELECT EXISTS (SELECT 1 FROM procurement.procurement_batches WHERE procurement_product_id = @id) OR EXISTS (SELECT 1 FROM processing_config.processing_routes WHERE procurement_product_id = @id) OR EXISTS (SELECT 1 FROM inventory.inventory_movements WHERE procurement_product_id = @id) OR EXISTS (SELECT 1 FROM inventory.inventory_positions WHERE procurement_product_id = @id);",
            ProductMasterHardDeleteErrorCodes.ProcurementProductNotFound, ProductMasterHardDeleteErrorCodes.ProcurementProductDependencyBlocked, cancellationToken);
}

internal sealed class PostgreSqlHardDeleteSalesProductExecutor(ErpDbContext db, ICommandTransactionRunner tx, TimeProvider time) : IHardDeleteSalesProductExecutor
{
    private readonly PostgreSqlHardDeleteProductMasterSupport _support = new(db, tx, time);
    public ValueTask<ApplicationResult<HardDeleteProductMasterResult>> ExecuteAsync(HardDeleteProductMasterExecution execution, CancellationToken cancellationToken) =>
        _support.ExecuteAsync(execution, "HardDeleteSalesProduct", "product.sales_products", "product.sales-product",
            "SELECT EXISTS (SELECT 1 FROM sales.sales_details WHERE sales_product_id = @id) OR EXISTS (SELECT 1 FROM outsourced.outsourced_supply_details WHERE sales_product_id = @id) OR EXISTS (SELECT 1 FROM processing_config.processing_module_outputs WHERE sales_product_id = @id) OR EXISTS (SELECT 1 FROM inventory.inventory_movements WHERE sales_product_id = @id) OR EXISTS (SELECT 1 FROM inventory.inventory_positions WHERE sales_product_id = @id);",
            ProductMasterHardDeleteErrorCodes.SalesProductNotFound, ProductMasterHardDeleteErrorCodes.SalesProductDependencyBlocked, cancellationToken);
}

internal sealed class PostgreSqlHardDeleteSalesProductGroupExecutor(ErpDbContext db, ICommandTransactionRunner tx, TimeProvider time) : IHardDeleteSalesProductGroupExecutor
{
    private readonly PostgreSqlHardDeleteProductMasterSupport _support = new(db, tx, time);
    public ValueTask<ApplicationResult<HardDeleteProductMasterResult>> ExecuteAsync(HardDeleteProductMasterExecution execution, CancellationToken cancellationToken) =>
        _support.ExecuteAsync(execution, "HardDeleteSalesProductGroup", "product.sales_product_groups", "product.sales-product-group",
            "SELECT EXISTS (SELECT 1 FROM product.sales_products WHERE sales_product_group_id = @id);",
            ProductMasterHardDeleteErrorCodes.SalesProductGroupNotFound, ProductMasterHardDeleteErrorCodes.SalesProductGroupDependencyBlocked, cancellationToken);
}