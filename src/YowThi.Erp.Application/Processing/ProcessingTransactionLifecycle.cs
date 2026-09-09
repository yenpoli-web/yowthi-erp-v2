using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Processing;

public sealed record SoftDeleteProcessingExecutionCommand(Guid ProcessingExecutionId, long ExpectedRowVersion);
public sealed record RestoreProcessingExecutionCommand(Guid ProcessingExecutionId, long ExpectedRowVersion);
public sealed record HardDeleteProcessingExecutionCommand(Guid ProcessingExecutionId, long ExpectedRowVersion);
public sealed record SoftDeleteProcessingExecutionInputCommand(Guid ProcessingExecutionId, long ExpectedRowVersion);
public sealed record RestoreProcessingExecutionInputCommand(Guid ProcessingExecutionId, long ExpectedRowVersion);
public sealed record HardDeleteProcessingExecutionInputCommand(Guid ProcessingExecutionId, long ExpectedRowVersion);
public sealed record SoftDeleteProcessingExecutionOutputCommand(Guid ProcessingExecutionOutputId, long ExpectedRowVersion);
public sealed record RestoreProcessingExecutionOutputCommand(Guid ProcessingExecutionOutputId, long ExpectedRowVersion);
public sealed record HardDeleteProcessingExecutionOutputCommand(Guid ProcessingExecutionOutputId, long ExpectedRowVersion);

public sealed record SoftDeleteProcessingExecutionExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    SoftDeleteProcessingExecutionCommand Command);

public sealed record RestoreProcessingExecutionExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    RestoreProcessingExecutionCommand Command);

public sealed record HardDeleteProcessingExecutionExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    HardDeleteProcessingExecutionCommand Command);

public sealed record SoftDeleteProcessingExecutionInputExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    SoftDeleteProcessingExecutionInputCommand Command);

public sealed record RestoreProcessingExecutionInputExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    RestoreProcessingExecutionInputCommand Command);

public sealed record HardDeleteProcessingExecutionInputExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    HardDeleteProcessingExecutionInputCommand Command);

public sealed record SoftDeleteProcessingExecutionOutputExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    SoftDeleteProcessingExecutionOutputCommand Command);

public sealed record RestoreProcessingExecutionOutputExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    RestoreProcessingExecutionOutputCommand Command);

public sealed record HardDeleteProcessingExecutionOutputExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    HardDeleteProcessingExecutionOutputCommand Command);

public sealed record ProcessingTransactionLifecycleResult(Guid Id, long RowVersion, bool Deleted);
public sealed record HardDeleteProcessingTransactionResult(Guid Id);

public interface IProcessingTransactionLifecycleExecutor
{
    ValueTask<ApplicationResult<ProcessingTransactionLifecycleResult>> SoftDeleteExecutionAsync(
        SoftDeleteProcessingExecutionExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<ProcessingTransactionLifecycleResult>> RestoreExecutionAsync(
        RestoreProcessingExecutionExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<ProcessingTransactionLifecycleResult>> SoftDeleteInputAsync(
        SoftDeleteProcessingExecutionInputExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<ProcessingTransactionLifecycleResult>> RestoreInputAsync(
        RestoreProcessingExecutionInputExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<ProcessingTransactionLifecycleResult>> SoftDeleteOutputAsync(
        SoftDeleteProcessingExecutionOutputExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<ProcessingTransactionLifecycleResult>> RestoreOutputAsync(
        RestoreProcessingExecutionOutputExecution execution,
        CancellationToken cancellationToken);
}

public interface IHardDeleteProcessingTransactionExecutor
{
    ValueTask<ApplicationResult<HardDeleteProcessingTransactionResult>> HardDeleteExecutionAsync(
        HardDeleteProcessingExecutionExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<HardDeleteProcessingTransactionResult>> HardDeleteInputAsync(
        HardDeleteProcessingExecutionInputExecution execution,
        CancellationToken cancellationToken);

    ValueTask<ApplicationResult<HardDeleteProcessingTransactionResult>> HardDeleteOutputAsync(
        HardDeleteProcessingExecutionOutputExecution execution,
        CancellationToken cancellationToken);
}

public static class ProcessingTransactionLifecycleErrorCodes
{
    public const string InvalidInput = "processing.transaction-lifecycle-invalid";
    public const string ExecutionNotFound = "processing.execution-not-found";
    public const string InputNotFound = "processing.execution-input-not-found";
    public const string OutputNotFound = "processing.execution-output-not-found";
    public const string AlreadyDeleted = "processing.transaction-already-deleted";
    public const string NotDeleted = "processing.transaction-not-deleted";
    public const string StaleRowVersion = "concurrency.stale-row-version";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
    public const string InputHardDeleteClosureInvalid = "processing.execution-input-hard-delete-closure-invalid";
    public const string OutputHardDeleteClosureAmbiguous = "processing.execution-output-hard-delete-closure-ambiguous";
}

public static class ProcessingTransactionLifecycleValidation
{
    public static ApplicationError? Validate(SoftDeleteProcessingExecutionCommand command) =>
        Validate(command.ProcessingExecutionId, command.ExpectedRowVersion);

    public static ApplicationError? Validate(RestoreProcessingExecutionCommand command) =>
        Validate(command.ProcessingExecutionId, command.ExpectedRowVersion);

    public static ApplicationError? Validate(HardDeleteProcessingExecutionCommand command) =>
        Validate(command.ProcessingExecutionId, command.ExpectedRowVersion);

    public static ApplicationError? Validate(SoftDeleteProcessingExecutionInputCommand command) =>
        Validate(command.ProcessingExecutionId, command.ExpectedRowVersion);

    public static ApplicationError? Validate(RestoreProcessingExecutionInputCommand command) =>
        Validate(command.ProcessingExecutionId, command.ExpectedRowVersion);

    public static ApplicationError? Validate(HardDeleteProcessingExecutionInputCommand command) =>
        Validate(command.ProcessingExecutionId, command.ExpectedRowVersion);

    public static ApplicationError? Validate(SoftDeleteProcessingExecutionOutputCommand command) =>
        Validate(command.ProcessingExecutionOutputId, command.ExpectedRowVersion);

    public static ApplicationError? Validate(RestoreProcessingExecutionOutputCommand command) =>
        Validate(command.ProcessingExecutionOutputId, command.ExpectedRowVersion);

    public static ApplicationError? Validate(HardDeleteProcessingExecutionOutputCommand command) =>
        Validate(command.ProcessingExecutionOutputId, command.ExpectedRowVersion);

    private static ApplicationError? Validate(Guid id, long expectedRowVersion)
    {
        if (id == Guid.Empty || expectedRowVersion < 1)
        {
            return ApplicationError.Create(
                ApplicationErrorKind.Validation,
                ProcessingTransactionLifecycleErrorCodes.InvalidInput);
        }

        return null;
    }
}
