using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Infrastructure;

public sealed record SoftDeleteContainerCommand(Guid ContainerId, long ExpectedRowVersion);

public sealed record SoftDeleteContainerExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    SoftDeleteContainerCommand Command);

public sealed record RestoreContainerCommand(Guid ContainerId, long ExpectedRowVersion);

public sealed record RestoreContainerExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    RestoreContainerCommand Command);

public sealed record ContainerLifecycleResult(Guid ContainerId, long RowVersion, bool Deleted);

public interface IContainerLifecycleExecutor
{
    ValueTask<ApplicationResult<ContainerLifecycleResult>> SoftDeleteAsync(
        SoftDeleteContainerExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<ContainerLifecycleResult>> RestoreAsync(
        RestoreContainerExecution execution,
        CancellationToken cancellationToken);
}

public static class ContainerLifecycleErrorCodes
{
    public const string InvalidInput = "infrastructure.container-lifecycle-invalid";
    public const string ContainerNotFound = "infrastructure.container-not-found";
    public const string AlreadyDeleted = "infrastructure.container-already-deleted";
    public const string NotDeleted = "infrastructure.container-not-deleted";
    public const string StaleRowVersion = "concurrency.stale-row-version";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class ContainerLifecycleValidation
{
    public static ApplicationError? Validate(SoftDeleteContainerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Validate(command.ContainerId, command.ExpectedRowVersion);
    }

    public static ApplicationError? Validate(RestoreContainerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Validate(command.ContainerId, command.ExpectedRowVersion);
    }

    private static ApplicationError? Validate(Guid containerId, long expectedRowVersion)
    {
        if (containerId == Guid.Empty || expectedRowVersion < 1)
        {
            return ApplicationError.Create(
                ApplicationErrorKind.Validation,
                ContainerLifecycleErrorCodes.InvalidInput);
        }

        return null;
    }
}
