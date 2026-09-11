using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Infrastructure;

public sealed record SoftDeleteStorageLocationCommand(Guid StorageLocationId, long ExpectedRowVersion);
public sealed record RestoreStorageLocationCommand(Guid StorageLocationId, long ExpectedRowVersion);

public sealed record SoftDeleteStorageLocationExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    SoftDeleteStorageLocationCommand Command);

public sealed record RestoreStorageLocationExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    RestoreStorageLocationCommand Command);

public sealed record StorageLocationLifecycleResult(
    Guid StorageLocationId,
    long RowVersion,
    bool Deleted);

public interface IStorageLocationLifecycleExecutor
{
    ValueTask<ApplicationResult<StorageLocationLifecycleResult>> SoftDeleteAsync(
        SoftDeleteStorageLocationExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<StorageLocationLifecycleResult>> RestoreAsync(
        RestoreStorageLocationExecution execution,
        CancellationToken cancellationToken);
}

public static class StorageLocationLifecycleErrorCodes
{
    public const string InvalidInput = "infrastructure.storage-location-lifecycle-invalid";
    public const string NotFound = "infrastructure.storage-location-not-found";
    public const string AlreadyDeleted = "infrastructure.storage-location-already-deleted";
    public const string NotDeleted = "infrastructure.storage-location-not-deleted";
    public const string StaleRowVersion = "concurrency.stale-row-version";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class StorageLocationLifecycleValidation
{
    public static ApplicationError? Validate(SoftDeleteStorageLocationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Validate(command.StorageLocationId, command.ExpectedRowVersion);
    }

    public static ApplicationError? Validate(RestoreStorageLocationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Validate(command.StorageLocationId, command.ExpectedRowVersion);
    }

    private static ApplicationError? Validate(Guid storageLocationId, long expectedRowVersion) =>
        storageLocationId == Guid.Empty || expectedRowVersion < 1
            ? ApplicationError.Create(ApplicationErrorKind.Validation, StorageLocationLifecycleErrorCodes.InvalidInput)
            : null;
}
