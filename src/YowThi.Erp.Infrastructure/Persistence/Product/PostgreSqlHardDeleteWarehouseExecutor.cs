using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.DataProtection;

namespace YowThi.Erp.Infrastructure.Persistence.Product;

internal sealed class PostgreSqlHardDeleteWarehouseExecutor(
    ErpDbContext db,
    ICommandTransactionRunner tx,
    TimeProvider time) : IHardDeleteWarehouseExecutor
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
            "HardDeleteWarehouse",
            "infrastructure.warehouses",
            "infrastructure.warehouse",
            "SELECT EXISTS (SELECT 1 FROM infrastructure.storage_locations WHERE warehouse_id = @id);",
            OperationalMasterHardDeleteErrorCodes.WarehouseNotFound,
            OperationalMasterHardDeleteErrorCodes.WarehouseDependencyBlocked,
            cancellationToken);
        return result.IsSuccess
            ? ApplicationResult<HardDeleteOperationalMasterResult>.Success(new HardDeleteOperationalMasterResult(result.Value.Id))
            : ApplicationResult<HardDeleteOperationalMasterResult>.Failure(result.Error);
    }
}
