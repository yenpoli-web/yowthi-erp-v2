using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Party;

public sealed record SoftDeleteFarmerCommand(Guid FarmerId, long ExpectedRowVersion);

public sealed record SoftDeleteFarmerExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    SoftDeleteFarmerCommand Command);

public sealed record RestoreFarmerCommand(Guid FarmerId, long ExpectedRowVersion);

public sealed record RestoreFarmerExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    RestoreFarmerCommand Command);

public sealed record FarmerLifecycleResult(
    Guid FarmerId,
    long RowVersion,
    bool Deleted);

public interface IFarmerLifecycleExecutor
{
    ValueTask<ApplicationResult<FarmerLifecycleResult>> SoftDeleteAsync(
        SoftDeleteFarmerExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<FarmerLifecycleResult>> RestoreAsync(
        RestoreFarmerExecution execution,
        CancellationToken cancellationToken);
}

public static class FarmerLifecycleErrorCodes
{
    public const string InvalidInput = "party.farmer-lifecycle-invalid";
    public const string FarmerNotFound = "party.farmer-not-found";
    public const string AlreadyDeleted = "party.farmer-already-deleted";
    public const string NotDeleted = "party.farmer-not-deleted";
    public const string StaleRowVersion = "concurrency.stale-row-version";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class FarmerLifecycleValidation
{
    public static ApplicationError? Validate(SoftDeleteFarmerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Validate(command.FarmerId, command.ExpectedRowVersion);
    }

    public static ApplicationError? Validate(RestoreFarmerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Validate(command.FarmerId, command.ExpectedRowVersion);
    }

    private static ApplicationError? Validate(Guid farmerId, long expectedRowVersion)
    {
        if (farmerId == Guid.Empty || expectedRowVersion < 1)
        {
            return ApplicationError.Create(
                ApplicationErrorKind.Validation,
                FarmerLifecycleErrorCodes.InvalidInput);
        }

        return null;
    }
}
