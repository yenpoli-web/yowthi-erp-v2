using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.DataProtection;

public sealed record HardDeleteOutsourcedVendorCommand(
    Guid OutsourcedVendorId,
    long ExpectedRowVersion);

public sealed record HardDeleteOutsourcedVendorExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    HardDeleteOutsourcedVendorCommand Command);

public sealed record HardDeleteOutsourcedVendorResult(Guid OutsourcedVendorId);

public interface IHardDeleteOutsourcedVendorExecutor
{
    ValueTask<ApplicationResult<HardDeleteOutsourcedVendorResult>> ExecuteAsync(
        HardDeleteOutsourcedVendorExecution execution,
        CancellationToken cancellationToken);
}

public static class OutsourcedVendorHardDeleteErrorCodes
{
    public const string InvalidInput = "data-protection.hard-delete-invalid";
    public const string OutsourcedVendorNotFound = "data-protection.outsourced-vendor-not-found";
    public const string DependencyBlocked = "data-protection.outsourced-vendor-dependency-blocked";
    public const string StaleRowVersion = "concurrency.stale-row-version";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class HardDeleteOutsourcedVendorValidation
{
    public static ApplicationError? Validate(HardDeleteOutsourcedVendorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.OutsourcedVendorId == Guid.Empty || command.ExpectedRowVersion < 1)
        {
            return ApplicationError.Create(
                ApplicationErrorKind.Validation,
                OutsourcedVendorHardDeleteErrorCodes.InvalidInput);
        }

        return null;
    }
}
