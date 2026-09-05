using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.DataProtection;

public sealed record HardDeleteFarmerCommand(
    Guid FarmerId,
    long ExpectedRowVersion);

public sealed record HardDeleteFarmerExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    HardDeleteFarmerCommand Command);

public sealed record HardDeleteFarmerResult(Guid FarmerId);

public interface IHardDeleteFarmerExecutor
{
    ValueTask<ApplicationResult<HardDeleteFarmerResult>> ExecuteAsync(
        HardDeleteFarmerExecution execution,
        CancellationToken cancellationToken);
}

public static class FarmerHardDeleteErrorCodes
{
    public const string InvalidInput = "data-protection.hard-delete-invalid";
    public const string FarmerNotFound = "data-protection.farmer-not-found";
    public const string DependencyBlocked = "data-protection.farmer-dependency-blocked";
    public const string StaleRowVersion = "concurrency.stale-row-version";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class HardDeleteFarmerValidation
{
    public static ApplicationError? Validate(HardDeleteFarmerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.FarmerId == Guid.Empty || command.ExpectedRowVersion < 1)
        {
            return ApplicationError.Create(
                ApplicationErrorKind.Validation,
                FarmerHardDeleteErrorCodes.InvalidInput);
        }

        return null;
    }
}
