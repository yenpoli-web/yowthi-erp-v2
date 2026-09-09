using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Sales;

public sealed record SoftDeleteSaleCommand(Guid SalesId, long ExpectedRowVersion);
public sealed record RestoreSaleCommand(Guid SalesId, long ExpectedRowVersion);
public sealed record HardDeleteSaleCommand(Guid SalesId, long ExpectedRowVersion);
public sealed record SoftDeleteSalesDetailCommand(Guid SalesDetailId, long ExpectedRowVersion);
public sealed record RestoreSalesDetailCommand(Guid SalesDetailId, long ExpectedRowVersion);
public sealed record HardDeleteSalesDetailCommand(Guid SalesDetailId, long ExpectedRowVersion);

public sealed record SoftDeleteSaleExecution(CommandId CommandId, CommandRequestHash RequestHash, ActorAccountId ActorAccountId, SoftDeleteSaleCommand Command);
public sealed record RestoreSaleExecution(CommandId CommandId, CommandRequestHash RequestHash, ActorAccountId ActorAccountId, RestoreSaleCommand Command);
public sealed record HardDeleteSaleExecution(CommandId CommandId, CommandRequestHash RequestHash, ActorAccountId ActorAccountId, HardDeleteSaleCommand Command);
public sealed record SoftDeleteSalesDetailExecution(CommandId CommandId, CommandRequestHash RequestHash, ActorAccountId ActorAccountId, SoftDeleteSalesDetailCommand Command);
public sealed record RestoreSalesDetailExecution(CommandId CommandId, CommandRequestHash RequestHash, ActorAccountId ActorAccountId, RestoreSalesDetailCommand Command);
public sealed record HardDeleteSalesDetailExecution(CommandId CommandId, CommandRequestHash RequestHash, ActorAccountId ActorAccountId, HardDeleteSalesDetailCommand Command);

public sealed record SalesTransactionLifecycleResult(Guid Id, long RowVersion, bool Deleted);
public sealed record HardDeleteSalesTransactionResult(Guid Id);

public interface ISalesTransactionLifecycleExecutor
{
    ValueTask<ApplicationResult<SalesTransactionLifecycleResult>> SoftDeleteSaleAsync(SoftDeleteSaleExecution execution, CancellationToken cancellationToken);
    ValueTask<ApplicationResult<SalesTransactionLifecycleResult>> RestoreSaleAsync(RestoreSaleExecution execution, CancellationToken cancellationToken);
    ValueTask<ApplicationResult<SalesTransactionLifecycleResult>> SoftDeleteDetailAsync(SoftDeleteSalesDetailExecution execution, CancellationToken cancellationToken);
    ValueTask<ApplicationResult<SalesTransactionLifecycleResult>> RestoreDetailAsync(RestoreSalesDetailExecution execution, CancellationToken cancellationToken);
}

public interface IHardDeleteSalesTransactionExecutor
{
    ValueTask<ApplicationResult<HardDeleteSalesTransactionResult>> HardDeleteSaleAsync(HardDeleteSaleExecution execution, CancellationToken cancellationToken);
    ValueTask<ApplicationResult<HardDeleteSalesTransactionResult>> HardDeleteDetailAsync(HardDeleteSalesDetailExecution execution, CancellationToken cancellationToken);
}

public static class SalesTransactionLifecycleErrorCodes
{
    public const string InvalidInput = "sales.transaction-lifecycle-invalid";
    public const string SaleNotFound = "sales.sale-not-found";
    public const string DetailNotFound = "sales.detail-not-found";
    public const string AlreadyDeleted = "sales.transaction-already-deleted";
    public const string NotDeleted = "sales.transaction-not-deleted";
    public const string StaleRowVersion = "concurrency.stale-row-version";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
    public const string HardDeleteInvalid = "sales.transaction-hard-delete-invalid";
    public const string ClosureInvalid = "sales.transaction-hard-delete-closure-invalid";
}

public static class SalesTransactionLifecycleValidation
{
    public static ApplicationError? Validate(SoftDeleteSaleCommand command) => Validate(command.SalesId, command.ExpectedRowVersion, false);
    public static ApplicationError? Validate(RestoreSaleCommand command) => Validate(command.SalesId, command.ExpectedRowVersion, false);
    public static ApplicationError? Validate(HardDeleteSaleCommand command) => Validate(command.SalesId, command.ExpectedRowVersion, true);
    public static ApplicationError? Validate(SoftDeleteSalesDetailCommand command) => Validate(command.SalesDetailId, command.ExpectedRowVersion, false);
    public static ApplicationError? Validate(RestoreSalesDetailCommand command) => Validate(command.SalesDetailId, command.ExpectedRowVersion, false);
    public static ApplicationError? Validate(HardDeleteSalesDetailCommand command) => Validate(command.SalesDetailId, command.ExpectedRowVersion, true);

    private static ApplicationError? Validate(Guid id, long expectedRowVersion, bool hardDelete)
    {
        if (id != Guid.Empty && expectedRowVersion >= 1)
        {
            return null;
        }

        return ApplicationError.Create(
            ApplicationErrorKind.Validation,
            hardDelete ? SalesTransactionLifecycleErrorCodes.HardDeleteInvalid : SalesTransactionLifecycleErrorCodes.InvalidInput);
    }
}
