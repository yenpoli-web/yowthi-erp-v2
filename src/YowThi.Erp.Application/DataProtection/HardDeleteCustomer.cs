using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.DataProtection;

public sealed record HardDeleteCustomerCommand(
    Guid CustomerId,
    long ExpectedRowVersion);

public sealed record HardDeleteCustomerExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    HardDeleteCustomerCommand Command);

public sealed record HardDeleteCustomerResult(Guid CustomerId);

public interface IHardDeleteCustomerExecutor
{
    ValueTask<ApplicationResult<HardDeleteCustomerResult>> ExecuteAsync(
        HardDeleteCustomerExecution execution,
        CancellationToken cancellationToken);
}

public static class CustomerHardDeleteErrorCodes
{
    public const string InvalidInput = "data-protection.hard-delete-invalid";
    public const string CustomerNotFound = "data-protection.customer-not-found";
    public const string DependencyBlocked = "data-protection.customer-dependency-blocked";
    public const string StaleRowVersion = "concurrency.stale-row-version";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class HardDeleteCustomerValidation
{
    public static ApplicationError? Validate(HardDeleteCustomerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.CustomerId == Guid.Empty || command.ExpectedRowVersion < 1)
        {
            return ApplicationError.Create(
                ApplicationErrorKind.Validation,
                CustomerHardDeleteErrorCodes.InvalidInput);
        }

        return null;
    }
}
