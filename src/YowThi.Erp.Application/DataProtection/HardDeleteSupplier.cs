using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.DataProtection;

public sealed record HardDeleteSupplierCommand(
    Guid SupplierId,
    long ExpectedRowVersion);

public sealed record HardDeleteSupplierExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    HardDeleteSupplierCommand Command);

public sealed record HardDeleteSupplierResult(Guid SupplierId);

public interface IHardDeleteSupplierExecutor
{
    ValueTask<ApplicationResult<HardDeleteSupplierResult>> ExecuteAsync(
        HardDeleteSupplierExecution execution,
        CancellationToken cancellationToken);
}

public static class SupplierHardDeleteErrorCodes
{
    public const string InvalidInput = "data-protection.hard-delete-invalid";
    public const string SupplierNotFound = "data-protection.supplier-not-found";
    public const string DependencyBlocked = "data-protection.supplier-dependency-blocked";
    public const string StaleRowVersion = "concurrency.stale-row-version";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class HardDeleteSupplierValidation
{
    public static ApplicationError? Validate(HardDeleteSupplierCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.SupplierId == Guid.Empty || command.ExpectedRowVersion < 1)
        {
            return ApplicationError.Create(
                ApplicationErrorKind.Validation,
                SupplierHardDeleteErrorCodes.InvalidInput);
        }

        return null;
    }
}
