using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Procurement;

public sealed record ReopenProcurementBatchCommand(
    Guid ProcurementBatchId,
    long ExpectedRowVersion,
    string? ReasonText);

public sealed record ReopenProcurementBatchExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    ReopenProcurementBatchCommand Command);

public sealed record ReopenProcurementBatchResult(
    Guid ProcurementBatchId,
    long ReopenedRowVersion,
    DateTimeOffset ReopenedAt);

public interface IReopenProcurementBatchExecutor
{
    ValueTask<ApplicationResult<ReopenProcurementBatchResult>> ExecuteAsync(
        ReopenProcurementBatchExecution execution,
        CancellationToken cancellationToken);
}

public static class ProcurementBatchReopenErrorCodes
{
    public const string InvalidInput = "procurement.batch-reopen-invalid";
    public const string BatchNotFound = "procurement.batch-not-found";
    public const string BatchUnavailable = "procurement.batch-unavailable";
    public const string BatchNotClosed = "procurement.batch-not-closed";
    public const string ConcurrentChange = "procurement.concurrent-change";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}

public static class ReopenProcurementBatchValidation
{
    public static ApplicationError? Validate(ReopenProcurementBatchCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ProcurementBatchId == Guid.Empty || command.ExpectedRowVersion <= 0)
        {
            return ApplicationError.Create(
                ApplicationErrorKind.Validation,
                ProcurementBatchReopenErrorCodes.InvalidInput);
        }

        return null;
    }
}
