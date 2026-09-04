using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.SalesHandling;

public sealed record SoftDeleteSalesPackagingItemCommand(Guid SalesPackagingItemId, long ExpectedRowVersion);

public sealed record SoftDeleteSalesPackagingItemExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    SoftDeleteSalesPackagingItemCommand Command);

public sealed record RestoreSalesPackagingItemCommand(Guid SalesPackagingItemId, long ExpectedRowVersion);

public sealed record RestoreSalesPackagingItemExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    RestoreSalesPackagingItemCommand Command);

public sealed record SalesPackagingItemLifecycleResult(
    Guid SalesPackagingItemId,
    long RowVersion,
    bool Deleted);

public interface ISalesPackagingItemLifecycleExecutor
{
    ValueTask<ApplicationResult<SalesPackagingItemLifecycleResult>> SoftDeleteAsync(
        SoftDeleteSalesPackagingItemExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<SalesPackagingItemLifecycleResult>> RestoreAsync(
        RestoreSalesPackagingItemExecution execution,
        CancellationToken cancellationToken);
}

public static class SalesPackagingItemLifecycleErrorCodes
{
    public const string InvalidInput = "sales-handling.packaging-item-lifecycle-invalid";
    public const string ItemNotFound = "sales-handling.packaging-item-not-found";
    public const string AlreadyDeleted = "sales-handling.packaging-item-already-deleted";
    public const string NotDeleted = "sales-handling.packaging-item-not-deleted";
    public const string StaleRowVersion = "concurrency.stale-row-version";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class SalesPackagingItemLifecycleValidation
{
    public static ApplicationError? Validate(SoftDeleteSalesPackagingItemCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Validate(command.SalesPackagingItemId, command.ExpectedRowVersion);
    }

    public static ApplicationError? Validate(RestoreSalesPackagingItemCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Validate(command.SalesPackagingItemId, command.ExpectedRowVersion);
    }

    private static ApplicationError? Validate(Guid salesPackagingItemId, long expectedRowVersion)
    {
        if (salesPackagingItemId == Guid.Empty || expectedRowVersion < 1)
        {
            return ApplicationError.Create(
                ApplicationErrorKind.Validation,
                SalesPackagingItemLifecycleErrorCodes.InvalidInput);
        }

        return null;
    }
}
