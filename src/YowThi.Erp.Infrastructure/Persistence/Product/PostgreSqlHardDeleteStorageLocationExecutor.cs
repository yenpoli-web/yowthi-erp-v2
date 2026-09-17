using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.DataProtection;

namespace YowThi.Erp.Infrastructure.Persistence.Product;

internal sealed class PostgreSqlHardDeleteStorageLocationExecutor(
    ErpDbContext db,
    ICommandTransactionRunner tx,
    TimeProvider time) : IHardDeleteStorageLocationExecutor
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
            "HardDeleteStorageLocation",
            "infrastructure.storage_locations",
            "infrastructure.storage-location",
            "SELECT EXISTS (SELECT 1 FROM inventory.inventory_movements WHERE storage_location_id = @id) OR EXISTS (SELECT 1 FROM inventory.inventory_positions WHERE storage_location_id = @id) OR EXISTS (SELECT 1 FROM processing_config.process_materials WHERE default_storage_location_id = @id) OR EXISTS (SELECT 1 FROM processing_config.route_input_configs WHERE default_storage_location_id = @id) OR EXISTS (SELECT 1 FROM procurement.procurement_batches WHERE receipt_storage_location_id = @id) OR EXISTS (SELECT 1 FROM product.procurement_products WHERE default_storage_location_id = @id) OR EXISTS (SELECT 1 FROM product.sales_products WHERE default_storage_location_id = @id);",
            OperationalMasterHardDeleteErrorCodes.StorageLocationNotFound,
            OperationalMasterHardDeleteErrorCodes.StorageLocationDependencyBlocked,
            cancellationToken);
        return result.IsSuccess
            ? ApplicationResult<HardDeleteOperationalMasterResult>.Success(new HardDeleteOperationalMasterResult(result.Value.Id))
            : ApplicationResult<HardDeleteOperationalMasterResult>.Failure(result.Error);
    }
}
