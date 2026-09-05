using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Infrastructure;

public sealed record SoftDeleteWarehouseCommand(Guid WarehouseId, long ExpectedRowVersion);

public sealed record SoftDeleteWarehouseExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    SoftDeleteWarehouseCommand Command);

public sealed record RestoreWarehouseCommand(Guid WarehouseId, long ExpectedRowVersion);

public sealed record RestoreWarehouseExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    RestoreWarehouseCommand Command);

public sealed record WarehouseLifecycleResult(Guid WarehouseId, long RowVersion, bool Deleted);

public interface IWarehouseLifecycleExecutor
{
    ValueTask<ApplicationResult<WarehouseLifecycleResult>> SoftDeleteAsync(
        SoftDeleteWarehouseExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<WarehouseLifecycleResult>> RestoreAsync(
        RestoreWarehouseExecution execution,
        CancellationToken cancellationToken);
}

public static class WarehouseLifecycleErrorCodes
{
    public const string InvalidInput = "infrastructure.warehouse-lifecycle-invalid";
    public const string WarehouseNotFound = "infrastructure.warehouse-not-found";
    public const string AlreadyDeleted = "infrastructure.warehouse-already-deleted";
    public const string NotDeleted = "infrastructure.warehouse-not-deleted";
    public const string StaleRowVersion = "concurrency.stale-row-version";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class WarehouseLifecycleValidation
{
    public static ApplicationError? Validate(SoftDeleteWarehouseCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Validate(command.WarehouseId, command.ExpectedRowVersion);
    }

    public static ApplicationError? Validate(RestoreWarehouseCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Validate(command.WarehouseId, command.ExpectedRowVersion);
    }

    private static ApplicationError? Validate(Guid warehouseId, long expectedRowVersion)
    {
        if (warehouseId == Guid.Empty || expectedRowVersion < 1)
        {
            return ApplicationError.Create(
                ApplicationErrorKind.Validation,
                WarehouseLifecycleErrorCodes.InvalidInput);
        }

        return null;
    }
}
