using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Product;

public sealed record SoftDeleteSalesProductGroupCommand(Guid SalesProductGroupId, long ExpectedRowVersion);

public sealed record SoftDeleteSalesProductGroupExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    SoftDeleteSalesProductGroupCommand Command);

public sealed record RestoreSalesProductGroupCommand(Guid SalesProductGroupId, long ExpectedRowVersion);

public sealed record RestoreSalesProductGroupExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    RestoreSalesProductGroupCommand Command);

public sealed record SalesProductGroupLifecycleResult(
    Guid SalesProductGroupId,
    long RowVersion,
    bool Deleted);

public interface ISalesProductGroupLifecycleExecutor
{
    ValueTask<ApplicationResult<SalesProductGroupLifecycleResult>> SoftDeleteAsync(
        SoftDeleteSalesProductGroupExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<SalesProductGroupLifecycleResult>> RestoreAsync(
        RestoreSalesProductGroupExecution execution,
        CancellationToken cancellationToken);
}

public static class SalesProductGroupLifecycleErrorCodes
{
    public const string InvalidInput = "product.sales-product-group-lifecycle-invalid";
    public const string GroupNotFound = "product.sales-product-group-not-found";
    public const string AlreadyDeleted = "product.sales-product-group-already-deleted";
    public const string NotDeleted = "product.sales-product-group-not-deleted";
    public const string StaleRowVersion = "concurrency.stale-row-version";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class SalesProductGroupLifecycleValidation
{
    public static ApplicationError? Validate(SoftDeleteSalesProductGroupCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Validate(command.SalesProductGroupId, command.ExpectedRowVersion);
    }

    public static ApplicationError? Validate(RestoreSalesProductGroupCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Validate(command.SalesProductGroupId, command.ExpectedRowVersion);
    }

    private static ApplicationError? Validate(Guid salesProductGroupId, long expectedRowVersion)
    {
        if (salesProductGroupId == Guid.Empty || expectedRowVersion < 1)
        {
            return ApplicationError.Create(
                ApplicationErrorKind.Validation,
                SalesProductGroupLifecycleErrorCodes.InvalidInput);
        }

        return null;
    }
}
