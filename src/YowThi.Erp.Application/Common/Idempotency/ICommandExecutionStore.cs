using YowThi.Erp.Application.Common.Identity;

namespace YowThi.Erp.Application.Common.Idempotency;

public interface ICommandExecutionStore
{
    ValueTask<CommandExecutionAcquireResult> AcquireAsync(
        CommandExecutionStart execution,
        CancellationToken cancellationToken);

    ValueTask MarkSucceededAsync(
        CommandId commandId,
        CommandResultSnapshot resultSnapshot,
        DateTimeOffset executedAt,
        CancellationToken cancellationToken);
}
