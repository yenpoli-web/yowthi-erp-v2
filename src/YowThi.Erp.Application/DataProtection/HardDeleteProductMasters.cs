using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.DataProtection;

public sealed record HardDeleteProductMasterCommand(Guid Id, long ExpectedRowVersion);
public sealed record HardDeleteProductMasterExecution(CommandId CommandId, CommandRequestHash RequestHash, ActorAccountId ActorAccountId, HardDeleteProductMasterCommand Command);
public sealed record HardDeleteProductMasterResult(Guid Id);

public interface IHardDeleteProcurementProductExecutor
{
    ValueTask<ApplicationResult<HardDeleteProductMasterResult>> ExecuteAsync(HardDeleteProductMasterExecution execution, CancellationToken cancellationToken);
}

public interface IHardDeleteSalesProductExecutor
{
    ValueTask<ApplicationResult<HardDeleteProductMasterResult>> ExecuteAsync(HardDeleteProductMasterExecution execution, CancellationToken cancellationToken);
}

public interface IHardDeleteSalesProductGroupExecutor
{
    ValueTask<ApplicationResult<HardDeleteProductMasterResult>> ExecuteAsync(HardDeleteProductMasterExecution execution, CancellationToken cancellationToken);
}

public interface IProductHardDeleteOptionsReader
{
    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetProcurementProductsAsync(HardDeleteOptionsQuery query, CancellationToken cancellationToken);
    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetSalesProductsAsync(HardDeleteOptionsQuery query, CancellationToken cancellationToken);
    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetSalesProductGroupsAsync(HardDeleteOptionsQuery query, CancellationToken cancellationToken);
}

public static class ProductMasterHardDeleteErrorCodes
{
    public const string InvalidInput = "data-protection.hard-delete-invalid";
    public const string ProcurementProductNotFound = "data-protection.procurement-product-not-found";
    public const string ProcurementProductDependencyBlocked = "data-protection.procurement-product-dependency-blocked";
    public const string SalesProductNotFound = "data-protection.sales-product-not-found";
    public const string SalesProductDependencyBlocked = "data-protection.sales-product-dependency-blocked";
    public const string SalesProductGroupNotFound = "data-protection.sales-product-group-not-found";
    public const string SalesProductGroupDependencyBlocked = "data-protection.sales-product-group-dependency-blocked";
    public const string StaleRowVersion = "concurrency.stale-row-version";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class HardDeleteProductMasterValidation
{
    public static ApplicationError? Validate(HardDeleteProductMasterCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.Id == Guid.Empty || command.ExpectedRowVersion < 1
            ? ApplicationError.Create(ApplicationErrorKind.Validation, ProductMasterHardDeleteErrorCodes.InvalidInput)
            : null;
    }
}