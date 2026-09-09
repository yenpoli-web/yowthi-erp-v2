using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Procurement;

public sealed record HardDeleteProcurementBatchCommand(
    Guid ProcurementBatchId,
    long ExpectedRowVersion);

public sealed record HardDeleteProcurementBatchExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    HardDeleteProcurementBatchCommand Command);

public sealed record HardDeleteProcurementBatchResult(Guid ProcurementBatchId);

public interface IHardDeleteProcurementBatchExecutor
{
    ValueTask<ApplicationResult<HardDeleteProcurementBatchResult>> ExecuteAsync(
        HardDeleteProcurementBatchExecution execution,
        CancellationToken cancellationToken);
}

public static class ProcurementBatchHardDeleteValidation
{
    public static ApplicationError? Validate(HardDeleteProcurementBatchCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.ProcurementBatchId != Guid.Empty && command.ExpectedRowVersion >= 1
            ? null
            : ApplicationError.Create(
                ApplicationErrorKind.Validation,
                ProcurementTransactionLifecycleErrorCodes.InvalidInput);
    }
}
