using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Party;

public sealed record SoftDeleteOutsourcedVendorCommand(
    Guid OutsourcedVendorId,
    long ExpectedRowVersion);

public sealed record SoftDeleteOutsourcedVendorExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    SoftDeleteOutsourcedVendorCommand Command);

public sealed record RestoreOutsourcedVendorCommand(
    Guid OutsourcedVendorId,
    long ExpectedRowVersion);

public sealed record RestoreOutsourcedVendorExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    RestoreOutsourcedVendorCommand Command);

public sealed record OutsourcedVendorLifecycleResult(
    Guid OutsourcedVendorId,
    long RowVersion,
    bool Deleted);

public interface IOutsourcedVendorLifecycleExecutor
{
    ValueTask<ApplicationResult<OutsourcedVendorLifecycleResult>> SoftDeleteAsync(
        SoftDeleteOutsourcedVendorExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<OutsourcedVendorLifecycleResult>> RestoreAsync(
        RestoreOutsourcedVendorExecution execution,
        CancellationToken cancellationToken);
}

public static class OutsourcedVendorLifecycleErrorCodes
{
    public const string InvalidInput = "party.outsourced-vendor-lifecycle-invalid";
    public const string VendorNotFound = "party.outsourced-vendor-not-found";
    public const string AlreadyDeleted = "party.outsourced-vendor-already-deleted";
    public const string NotDeleted = "party.outsourced-vendor-not-deleted";
    public const string StaleRowVersion = "concurrency.stale-row-version";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class OutsourcedVendorLifecycleValidation
{
    public static ApplicationError? Validate(SoftDeleteOutsourcedVendorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Validate(command.OutsourcedVendorId, command.ExpectedRowVersion);
    }

    public static ApplicationError? Validate(RestoreOutsourcedVendorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Validate(command.OutsourcedVendorId, command.ExpectedRowVersion);
    }

    private static ApplicationError? Validate(Guid outsourcedVendorId, long expectedRowVersion)
    {
        if (outsourcedVendorId == Guid.Empty || expectedRowVersion < 1)
        {
            return ApplicationError.Create(
                ApplicationErrorKind.Validation,
                OutsourcedVendorLifecycleErrorCodes.InvalidInput);
        }

        return null;
    }
}
