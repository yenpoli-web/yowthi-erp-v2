using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.DataProtection;

namespace YowThi.Erp.Infrastructure.Persistence.Product;

internal sealed class PostgreSqlHardDeleteSalesPackagingItemExecutor(
    ErpDbContext db,
    ICommandTransactionRunner tx,
    TimeProvider time) : IHardDeleteSalesPackagingItemExecutor
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
            "HardDeleteSalesPackagingItem",
            "sales_handling.sales_packaging_items",
            "sales-handling.sales-packaging-item",
            "SELECT EXISTS (SELECT 1 FROM sales_handling.sales_packaging_work_records WHERE sales_packaging_item_id = @id);",
            OperationalMasterHardDeleteErrorCodes.SalesPackagingItemNotFound,
            OperationalMasterHardDeleteErrorCodes.SalesPackagingItemDependencyBlocked,
            cancellationToken);
        return result.IsSuccess
            ? ApplicationResult<HardDeleteOperationalMasterResult>.Success(new HardDeleteOperationalMasterResult(result.Value.Id))
            : ApplicationResult<HardDeleteOperationalMasterResult>.Failure(result.Error);
    }
}
