using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Party;

public sealed record SoftDeleteEmployeeCommand(Guid EmployeeId, long ExpectedRowVersion);

public sealed record SoftDeleteEmployeeExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    SoftDeleteEmployeeCommand Command);

public sealed record RestoreEmployeeCommand(Guid EmployeeId, long ExpectedRowVersion);

public sealed record RestoreEmployeeExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    RestoreEmployeeCommand Command);

public sealed record EmployeeLifecycleResult(
    Guid EmployeeId,
    long RowVersion,
    bool Deleted);

public interface IEmployeeLifecycleExecutor
{
    ValueTask<ApplicationResult<EmployeeLifecycleResult>> SoftDeleteAsync(
        SoftDeleteEmployeeExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<EmployeeLifecycleResult>> RestoreAsync(
        RestoreEmployeeExecution execution,
        CancellationToken cancellationToken);
}

public static class EmployeeLifecycleErrorCodes
{
    public const string InvalidInput = "party.employee-lifecycle-invalid";
    public const string EmployeeNotFound = "party.employee-not-found";
    public const string AlreadyDeleted = "party.employee-already-deleted";
    public const string NotDeleted = "party.employee-not-deleted";
    public const string StaleRowVersion = "concurrency.stale-row-version";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class EmployeeLifecycleValidation
{
    public static ApplicationError? Validate(SoftDeleteEmployeeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Validate(command.EmployeeId, command.ExpectedRowVersion);
    }

    public static ApplicationError? Validate(RestoreEmployeeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Validate(command.EmployeeId, command.ExpectedRowVersion);
    }

    private static ApplicationError? Validate(Guid employeeId, long expectedRowVersion)
    {
        if (employeeId == Guid.Empty || expectedRowVersion < 1)
        {
            return ApplicationError.Create(
                ApplicationErrorKind.Validation,
                EmployeeLifecycleErrorCodes.InvalidInput);
        }

        return null;
    }
}
