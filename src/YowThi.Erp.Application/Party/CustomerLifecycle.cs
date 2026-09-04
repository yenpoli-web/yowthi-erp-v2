using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Party;

public sealed record SoftDeleteCustomerCommand(Guid CustomerId, long ExpectedRowVersion);

public sealed record SoftDeleteCustomerExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    SoftDeleteCustomerCommand Command);

public sealed record RestoreCustomerCommand(Guid CustomerId, long ExpectedRowVersion);

public sealed record RestoreCustomerExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    RestoreCustomerCommand Command);

public sealed record CustomerLifecycleResult(
    Guid CustomerId,
    long RowVersion,
    bool Deleted);

public interface ICustomerLifecycleExecutor
{
    ValueTask<ApplicationResult<CustomerLifecycleResult>> SoftDeleteAsync(
        SoftDeleteCustomerExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<CustomerLifecycleResult>> RestoreAsync(
        RestoreCustomerExecution execution,
        CancellationToken cancellationToken);
}

public static class CustomerLifecycleErrorCodes
{
    public const string InvalidInput = "party.customer-lifecycle-invalid";
    public const string CustomerNotFound = "party.customer-not-found";
    public const string AlreadyDeleted = "party.customer-already-deleted";
    public const string NotDeleted = "party.customer-not-deleted";
    public const string StaleRowVersion = "concurrency.stale-row-version";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class CustomerLifecycleValidation
{
    public static ApplicationError? Validate(SoftDeleteCustomerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Validate(command.CustomerId, command.ExpectedRowVersion);
    }

    public static ApplicationError? Validate(RestoreCustomerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Validate(command.CustomerId, command.ExpectedRowVersion);
    }

    private static ApplicationError? Validate(Guid customerId, long expectedRowVersion)
    {
        if (customerId == Guid.Empty || expectedRowVersion < 1)
        {
            return ApplicationError.Create(
                ApplicationErrorKind.Validation,
                CustomerLifecycleErrorCodes.InvalidInput);
        }

        return null;
    }
}
