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

internal sealed class PostgreSqlHardDeleteEmployeeExecutor(ErpDbContext db, ICommandTransactionRunner tx, TimeProvider time) : IHardDeleteEmployeeExecutor
{
    private readonly PostgreSqlHardDeleteProductMasterSupport _support = new(db, tx, time);

    public async ValueTask<ApplicationResult<HardDeleteOperationalMasterResult>> ExecuteAsync(
        HardDeleteOperationalMasterExecution execution,
        CancellationToken cancellationToken)
    {
        var mapped = new HardDeleteProductMasterExecution(
            execution.CommandId,
            execution.RequestHash,
            execution.ActorAccountId,
            new HardDeleteProductMasterCommand(execution.Command.Id, execution.Command.ExpectedRowVersion));
        var result = await _support.ExecuteAsync(
            mapped,
            "HardDeleteEmployee",
            "party.employees",
            "party.employee",
            "SELECT EXISTS (SELECT 1 FROM labor.employee_daily_wages WHERE employee_id = @id) OR EXISTS (SELECT 1 FROM processing.processing_executions WHERE employee_id = @id) OR EXISTS (SELECT 1 FROM sales_handling.sales_packaging_work_records WHERE employee_id = @id);",
            OperationalMasterHardDeleteErrorCodes.EmployeeNotFound,
            OperationalMasterHardDeleteErrorCodes.EmployeeDependencyBlocked,
            cancellationToken);
        return result.IsSuccess
            ? ApplicationResult<HardDeleteOperationalMasterResult>.Success(new HardDeleteOperationalMasterResult(result.Value.Id))
            : ApplicationResult<HardDeleteOperationalMasterResult>.Failure(result.Error);
    }
}
