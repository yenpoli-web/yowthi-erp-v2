using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.DataProtection;

public sealed record HardDeleteOptionsQuery(
    string Locale,
    string? Search,
    int Offset,
    int Limit);

public sealed record HardDeleteOptionPage<T>(
    IReadOnlyList<T> Items,
    int? NextOffset);

public sealed record HardDeleteOption(
    Guid Id,
    string DisplayName,
    bool Active,
    long RowVersion,
    bool Deleted,
    DateTimeOffset? DeletedAt);

public interface IHardDeleteOptionsReader
{
    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetSuppliersAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetCustomersAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetOutsourcedVendorsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetFarmersAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetEmployeesAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetSalesPackagingItemsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetWarehousesAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetStorageLocationsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetContainersAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetOutsourcedSupplyBatchesAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetOutsourcedSupplyDetailsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetProcurementBatchesAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetProcurementEntriesAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetSalesAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetSalesDetailsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetProcessingExecutionsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetProcessingExecutionInputsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetProcessingExecutionOutputsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);
}

public sealed record HardDeleteOperationalMasterCommand(Guid Id, long ExpectedRowVersion);

public sealed record HardDeleteOperationalMasterExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    HardDeleteOperationalMasterCommand Command);

public sealed record HardDeleteOperationalMasterResult(Guid Id);

public interface IHardDeleteEmployeeExecutor
{
    ValueTask<ApplicationResult<HardDeleteOperationalMasterResult>> ExecuteAsync(
        HardDeleteOperationalMasterExecution execution,
        CancellationToken cancellationToken);
}

public interface IHardDeleteSalesPackagingItemExecutor
{
    ValueTask<ApplicationResult<HardDeleteOperationalMasterResult>> ExecuteAsync(
        HardDeleteOperationalMasterExecution execution,
        CancellationToken cancellationToken);
}

public interface IHardDeleteWarehouseExecutor
{
    ValueTask<ApplicationResult<HardDeleteOperationalMasterResult>> ExecuteAsync(
        HardDeleteOperationalMasterExecution execution,
        CancellationToken cancellationToken);
}

public interface IHardDeleteStorageLocationExecutor
{
    ValueTask<ApplicationResult<HardDeleteOperationalMasterResult>> ExecuteAsync(
        HardDeleteOperationalMasterExecution execution,
        CancellationToken cancellationToken);
}

public interface IHardDeleteContainerExecutor
{
    ValueTask<ApplicationResult<HardDeleteOperationalMasterResult>> ExecuteAsync(
        HardDeleteOperationalMasterExecution execution,
        CancellationToken cancellationToken);
}

public static class OperationalMasterHardDeleteErrorCodes
{
    public const string InvalidInput = "data-protection.hard-delete-invalid";
    public const string EmployeeNotFound = "data-protection.employee-not-found";
    public const string EmployeeDependencyBlocked = "data-protection.employee-dependency-blocked";
    public const string SalesPackagingItemNotFound = "data-protection.sales-packaging-item-not-found";
    public const string SalesPackagingItemDependencyBlocked = "data-protection.sales-packaging-item-dependency-blocked";
    public const string WarehouseNotFound = "data-protection.warehouse-not-found";
    public const string WarehouseDependencyBlocked = "data-protection.warehouse-dependency-blocked";
    public const string StorageLocationNotFound = "data-protection.storage-location-not-found";
    public const string StorageLocationDependencyBlocked = "data-protection.storage-location-dependency-blocked";
    public const string ContainerNotFound = "data-protection.container-not-found";
    public const string ContainerDependencyBlocked = "data-protection.container-dependency-blocked";
    public const string StaleRowVersion = "concurrency.stale-row-version";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class HardDeleteOperationalMasterValidation
{
    public static ApplicationError? Validate(HardDeleteOperationalMasterCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.Id == Guid.Empty || command.ExpectedRowVersion < 1
            ? ApplicationError.Create(ApplicationErrorKind.Validation, OperationalMasterHardDeleteErrorCodes.InvalidInput)
            : null;
    }
}
