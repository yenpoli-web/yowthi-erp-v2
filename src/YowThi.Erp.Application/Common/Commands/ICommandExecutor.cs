using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Common.Commands;

public interface ICommandExecutor<TCommand, TResult>
    where TCommand : IApplicationCommand<TResult>
{
    ValueTask<ApplicationResult<TResult>> ExecuteAsync(
        CommandExecutionRequest<TCommand> request,
        CancellationToken cancellationToken);
}
