using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Procurement;

public sealed record SoftDeleteProcurementBatchCommand(Guid ProcurementBatchId, long ExpectedRowVersion);
public sealed record RestoreProcurementBatchCommand(Guid ProcurementBatchId, long ExpectedRowVersion);
public sealed record SoftDeleteProcurementEntryCommand(Guid ProcurementEntryId, long ExpectedRowVersion);
public sealed record RestoreProcurementEntryCommand(Guid ProcurementEntryId, long ExpectedRowVersion);
public sealed record HardDeleteProcurementEntryCommand(Guid ProcurementEntryId, long ExpectedRowVersion);

public sealed record SoftDeleteProcurementBatchExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    SoftDeleteProcurementBatchCommand Command);

public sealed record RestoreProcurementBatchExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    RestoreProcurementBatchCommand Command);

public sealed record SoftDeleteProcurementEntryExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    SoftDeleteProcurementEntryCommand Command);

public sealed record RestoreProcurementEntryExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    RestoreProcurementEntryCommand Command);

public sealed record HardDeleteProcurementEntryExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    HardDeleteProcurementEntryCommand Command);

public sealed record ProcurementTransactionLifecycleResult(
    Guid Id,
    long RowVersion,
    bool Deleted);

public sealed record HardDeleteProcurementEntryResult(Guid ProcurementEntryId);

public interface IProcurementTransactionLifecycleExecutor
{
    ValueTask<ApplicationResult<ProcurementTransactionLifecycleResult>> SoftDeleteBatchAsync(
        SoftDeleteProcurementBatchExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<ProcurementTransactionLifecycleResult>> RestoreBatchAsync(
        RestoreProcurementBatchExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<ProcurementTransactionLifecycleResult>> SoftDeleteEntryAsync(
        SoftDeleteProcurementEntryExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<ProcurementTransactionLifecycleResult>> RestoreEntryAsync(
        RestoreProcurementEntryExecution execution,
        CancellationToken cancellationToken);
}

public interface IHardDeleteProcurementEntryExecutor
{
    ValueTask<ApplicationResult<HardDeleteProcurementEntryResult>> ExecuteAsync(
        HardDeleteProcurementEntryExecution execution,
        CancellationToken cancellationToken);
}

public static class ProcurementTransactionLifecycleErrorCodes
{
    public const string InvalidInput = "procurement.transaction-lifecycle-invalid";
    public const string BatchNotFound = "procurement.batch-not-found";
    public const string EntryNotFound = "procurement.entry-not-found";
    public const string AlreadyDeleted = "procurement.transaction-already-deleted";
    public const string NotDeleted = "procurement.transaction-not-deleted";
    public const string StaleRowVersion = "concurrency.stale-row-version";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
    public const string EntryHardDeleteInvalid = "procurement.entry-hard-delete-invalid";
}

public static class ProcurementTransactionLifecycleValidation
{
    public static ApplicationError? Validate(SoftDeleteProcurementBatchCommand command) =>
        Validate(command.ProcurementBatchId, command.ExpectedRowVersion);

    public static ApplicationError? Validate(RestoreProcurementBatchCommand command) =>
        Validate(command.ProcurementBatchId, command.ExpectedRowVersion);

    public static ApplicationError? Validate(SoftDeleteProcurementEntryCommand command) =>
        Validate(command.ProcurementEntryId, command.ExpectedRowVersion);

    public static ApplicationError? Validate(RestoreProcurementEntryCommand command) =>
        Validate(command.ProcurementEntryId, command.ExpectedRowVersion);

    public static ApplicationError? Validate(HardDeleteProcurementEntryCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.ProcurementEntryId != Guid.Empty && command.ExpectedRowVersion >= 1
            ? null
            : ApplicationError.Create(
                ApplicationErrorKind.Validation,
                ProcurementTransactionLifecycleErrorCodes.EntryHardDeleteInvalid);
    }

    private static ApplicationError? Validate(Guid id, long expectedRowVersion)
    {
        if (id == Guid.Empty || expectedRowVersion < 1)
        {
            return ApplicationError.Create(
                ApplicationErrorKind.Validation,
                ProcurementTransactionLifecycleErrorCodes.InvalidInput);
        }

        return null;
    }
}
