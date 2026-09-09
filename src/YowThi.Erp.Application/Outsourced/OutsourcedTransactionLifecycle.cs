using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Outsourced;

public sealed record SoftDeleteOutsourcedSupplyBatchCommand(Guid OutsourcedSupplyBatchId, long ExpectedRowVersion);
public sealed record RestoreOutsourcedSupplyBatchCommand(Guid OutsourcedSupplyBatchId, long ExpectedRowVersion);
public sealed record HardDeleteOutsourcedSupplyBatchCommand(Guid OutsourcedSupplyBatchId, long ExpectedRowVersion);
public sealed record SoftDeleteOutsourcedSupplyDetailCommand(Guid OutsourcedSupplyDetailId, long ExpectedRowVersion);
public sealed record RestoreOutsourcedSupplyDetailCommand(Guid OutsourcedSupplyDetailId, long ExpectedRowVersion);
public sealed record HardDeleteOutsourcedSupplyDetailCommand(Guid OutsourcedSupplyDetailId, long ExpectedRowVersion);

public sealed record SoftDeleteOutsourcedSupplyBatchExecution(CommandId CommandId, CommandRequestHash RequestHash, ActorAccountId ActorAccountId, SoftDeleteOutsourcedSupplyBatchCommand Command);
public sealed record RestoreOutsourcedSupplyBatchExecution(CommandId CommandId, CommandRequestHash RequestHash, ActorAccountId ActorAccountId, RestoreOutsourcedSupplyBatchCommand Command);
public sealed record HardDeleteOutsourcedSupplyBatchExecution(CommandId CommandId, CommandRequestHash RequestHash, ActorAccountId ActorAccountId, HardDeleteOutsourcedSupplyBatchCommand Command);
public sealed record SoftDeleteOutsourcedSupplyDetailExecution(CommandId CommandId, CommandRequestHash RequestHash, ActorAccountId ActorAccountId, SoftDeleteOutsourcedSupplyDetailCommand Command);
public sealed record RestoreOutsourcedSupplyDetailExecution(CommandId CommandId, CommandRequestHash RequestHash, ActorAccountId ActorAccountId, RestoreOutsourcedSupplyDetailCommand Command);
public sealed record HardDeleteOutsourcedSupplyDetailExecution(CommandId CommandId, CommandRequestHash RequestHash, ActorAccountId ActorAccountId, HardDeleteOutsourcedSupplyDetailCommand Command);

public sealed record OutsourcedTransactionLifecycleResult(Guid Id, long RowVersion, bool Deleted);
public sealed record HardDeleteOutsourcedTransactionResult(Guid Id);

public interface IOutsourcedTransactionLifecycleExecutor
{
    ValueTask<ApplicationResult<OutsourcedTransactionLifecycleResult>> SoftDeleteBatchAsync(SoftDeleteOutsourcedSupplyBatchExecution execution, CancellationToken cancellationToken);
    ValueTask<ApplicationResult<OutsourcedTransactionLifecycleResult>> RestoreBatchAsync(RestoreOutsourcedSupplyBatchExecution execution, CancellationToken cancellationToken);
    ValueTask<ApplicationResult<OutsourcedTransactionLifecycleResult>> SoftDeleteDetailAsync(SoftDeleteOutsourcedSupplyDetailExecution execution, CancellationToken cancellationToken);
    ValueTask<ApplicationResult<OutsourcedTransactionLifecycleResult>> RestoreDetailAsync(RestoreOutsourcedSupplyDetailExecution execution, CancellationToken cancellationToken);
}

public interface IHardDeleteOutsourcedTransactionExecutor
{
    ValueTask<ApplicationResult<HardDeleteOutsourcedTransactionResult>> HardDeleteBatchAsync(HardDeleteOutsourcedSupplyBatchExecution execution, CancellationToken cancellationToken);
    ValueTask<ApplicationResult<HardDeleteOutsourcedTransactionResult>> HardDeleteDetailAsync(HardDeleteOutsourcedSupplyDetailExecution execution, CancellationToken cancellationToken);
}

public static class OutsourcedTransactionLifecycleErrorCodes
{
    public const string InvalidInput = "outsourced.transaction-lifecycle-invalid";
    public const string BatchNotFound = "outsourced.batch-not-found";
    public const string DetailNotFound = "outsourced.supply-detail-not-found";
    public const string AlreadyDeleted = "outsourced.transaction-already-deleted";
    public const string NotDeleted = "outsourced.transaction-not-deleted";
    public const string StaleRowVersion = "concurrency.stale-row-version";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
    public const string HardDeleteInvalid = "outsourced.transaction-hard-delete-invalid";
    public const string HardDeleteClosureInvalid = "outsourced.transaction-hard-delete-closure-invalid";
}

public static class OutsourcedTransactionLifecycleValidation
{
    public static ApplicationError? Validate(SoftDeleteOutsourcedSupplyBatchCommand command) => Validate(command.OutsourcedSupplyBatchId, command.ExpectedRowVersion, false);
    public static ApplicationError? Validate(RestoreOutsourcedSupplyBatchCommand command) => Validate(command.OutsourcedSupplyBatchId, command.ExpectedRowVersion, false);
    public static ApplicationError? Validate(HardDeleteOutsourcedSupplyBatchCommand command) => Validate(command.OutsourcedSupplyBatchId, command.ExpectedRowVersion, true);
    public static ApplicationError? Validate(SoftDeleteOutsourcedSupplyDetailCommand command) => Validate(command.OutsourcedSupplyDetailId, command.ExpectedRowVersion, false);
    public static ApplicationError? Validate(RestoreOutsourcedSupplyDetailCommand command) => Validate(command.OutsourcedSupplyDetailId, command.ExpectedRowVersion, false);
    public static ApplicationError? Validate(HardDeleteOutsourcedSupplyDetailCommand command) => Validate(command.OutsourcedSupplyDetailId, command.ExpectedRowVersion, true);

    private static ApplicationError? Validate(Guid id, long expectedRowVersion, bool hardDelete)
    {
        if (id != Guid.Empty && expectedRowVersion >= 1)
        {
            return null;
        }

        return ApplicationError.Create(
            ApplicationErrorKind.Validation,
            hardDelete ? OutsourcedTransactionLifecycleErrorCodes.HardDeleteInvalid : OutsourcedTransactionLifecycleErrorCodes.InvalidInput);
    }
}
