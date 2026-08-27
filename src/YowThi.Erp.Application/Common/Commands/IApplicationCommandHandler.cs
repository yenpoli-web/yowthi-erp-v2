using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Common.Commands;

public interface IApplicationCommandHandler<in TCommand, TResult>
    where TCommand : IApplicationCommand<TResult>
{
    ValueTask<ApplicationResult<TResult>> HandleAsync(
        TCommand command,
        CancellationToken cancellationToken);
}
