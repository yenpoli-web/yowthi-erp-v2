using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Product;

public sealed record SoftDeleteProcurementProductCommand(Guid ProcurementProductId, long ExpectedRowVersion);
public sealed record RestoreProcurementProductCommand(Guid ProcurementProductId, long ExpectedRowVersion);

public sealed record SoftDeleteProcurementProductExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    SoftDeleteProcurementProductCommand Command);

public sealed record RestoreProcurementProductExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    RestoreProcurementProductCommand Command);

public sealed record ProcurementProductLifecycleResult(Guid ProcurementProductId, long RowVersion, bool Deleted);

public interface IProcurementProductLifecycleExecutor
{
    ValueTask<ApplicationResult<ProcurementProductLifecycleResult>> SoftDeleteAsync(
        SoftDeleteProcurementProductExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<ProcurementProductLifecycleResult>> RestoreAsync(
        RestoreProcurementProductExecution execution,
        CancellationToken cancellationToken);
}

public static class ProcurementProductLifecycleErrorCodes
{
    public const string InvalidInput = "product.procurement-product-lifecycle-invalid";
    public const string ProductNotFound = "product.procurement-product-not-found";
    public const string AlreadyDeleted = "product.procurement-product-already-deleted";
    public const string NotDeleted = "product.procurement-product-not-deleted";
    public const string StaleRowVersion = "concurrency.stale-row-version";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class ProcurementProductLifecycleValidation
{
    public static ApplicationError? Validate(SoftDeleteProcurementProductCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Validate(command.ProcurementProductId, command.ExpectedRowVersion);
    }

    public static ApplicationError? Validate(RestoreProcurementProductCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Validate(command.ProcurementProductId, command.ExpectedRowVersion);
    }

    private static ApplicationError? Validate(Guid id, long expectedRowVersion) =>
        id == Guid.Empty || expectedRowVersion < 1
            ? ApplicationError.Create(ApplicationErrorKind.Validation, ProcurementProductLifecycleErrorCodes.InvalidInput)
            : null;
}

public sealed record SoftDeleteSalesProductCommand(Guid SalesProductId, long ExpectedRowVersion);
public sealed record RestoreSalesProductCommand(Guid SalesProductId, long ExpectedRowVersion);

public sealed record SoftDeleteSalesProductExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    SoftDeleteSalesProductCommand Command);

public sealed record RestoreSalesProductExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    RestoreSalesProductCommand Command);

public sealed record SalesProductLifecycleResult(Guid SalesProductId, long RowVersion, bool Deleted);

public interface ISalesProductLifecycleExecutor
{
    ValueTask<ApplicationResult<SalesProductLifecycleResult>> SoftDeleteAsync(
        SoftDeleteSalesProductExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<SalesProductLifecycleResult>> RestoreAsync(
        RestoreSalesProductExecution execution,
        CancellationToken cancellationToken);
}

public static class SalesProductLifecycleErrorCodes
{
    public const string InvalidInput = "product.sales-product-lifecycle-invalid";
    public const string ProductNotFound = "product.sales-product-not-found";
    public const string AlreadyDeleted = "product.sales-product-already-deleted";
    public const string NotDeleted = "product.sales-product-not-deleted";
    public const string StaleRowVersion = "concurrency.stale-row-version";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class SalesProductLifecycleValidation
{
    public static ApplicationError? Validate(SoftDeleteSalesProductCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Validate(command.SalesProductId, command.ExpectedRowVersion);
    }

    public static ApplicationError? Validate(RestoreSalesProductCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Validate(command.SalesProductId, command.ExpectedRowVersion);
    }

    private static ApplicationError? Validate(Guid id, long expectedRowVersion) =>
        id == Guid.Empty || expectedRowVersion < 1
            ? ApplicationError.Create(ApplicationErrorKind.Validation, SalesProductLifecycleErrorCodes.InvalidInput)
            : null;
}
