using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.DataProtection;

namespace YowThi.Erp.Infrastructure.Persistence.Product;

internal sealed class PostgreSqlHardDeleteContainerExecutor(
    ErpDbContext db,
    ICommandTransactionRunner tx,
    TimeProvider time) : IHardDeleteContainerExecutor
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
            "HardDeleteContainer",
            "infrastructure.containers",
            "infrastructure.container",
            "SELECT EXISTS (SELECT 1 FROM processing_config.process_materials WHERE container_id = @id) OR EXISTS (SELECT 1 FROM processing_config.route_input_configs WHERE container_id = @id);",
            OperationalMasterHardDeleteErrorCodes.ContainerNotFound,
            OperationalMasterHardDeleteErrorCodes.ContainerDependencyBlocked,
            cancellationToken);
        return result.IsSuccess
            ? ApplicationResult<HardDeleteOperationalMasterResult>.Success(new HardDeleteOperationalMasterResult(result.Value.Id))
            : ApplicationResult<HardDeleteOperationalMasterResult>.Failure(result.Error);
    }
}
