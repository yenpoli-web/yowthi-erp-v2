using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Party;

public sealed record SoftDeleteSupplierCommand(Guid SupplierId, long ExpectedRowVersion);

public sealed record SoftDeleteSupplierExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    SoftDeleteSupplierCommand Command);

public sealed record RestoreSupplierCommand(Guid SupplierId, long ExpectedRowVersion);

public sealed record RestoreSupplierExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    RestoreSupplierCommand Command);

public sealed record SupplierLifecycleResult(
    Guid SupplierId,
    long RowVersion,
    bool Deleted);

public interface ISupplierLifecycleExecutor
{
    ValueTask<ApplicationResult<SupplierLifecycleResult>> SoftDeleteAsync(
        SoftDeleteSupplierExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<SupplierLifecycleResult>> RestoreAsync(
        RestoreSupplierExecution execution,
        CancellationToken cancellationToken);
}

public static class SupplierLifecycleErrorCodes
{
    public const string InvalidInput = "party.supplier-lifecycle-invalid";
    public const string SupplierNotFound = "party.supplier-not-found";
    public const string AlreadyDeleted = "party.supplier-already-deleted";
    public const string NotDeleted = "party.supplier-not-deleted";
    public const string StaleRowVersion = "concurrency.stale-row-version";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class SupplierLifecycleValidation
{
    public static ApplicationError? Validate(SoftDeleteSupplierCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Validate(command.SupplierId, command.ExpectedRowVersion);
    }

    public static ApplicationError? Validate(RestoreSupplierCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Validate(command.SupplierId, command.ExpectedRowVersion);
    }

    private static ApplicationError? Validate(Guid supplierId, long expectedRowVersion)
    {
        if (supplierId == Guid.Empty || expectedRowVersion < 1)
        {
            return ApplicationError.Create(
                ApplicationErrorKind.Validation,
                SupplierLifecycleErrorCodes.InvalidInput);
        }

        return null;
    }
}
