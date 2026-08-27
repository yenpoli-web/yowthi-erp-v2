using YowThi.Erp.Application.Common.Identity;

namespace YowThi.Erp.Application.Common.Commands;

public sealed record CommandExecutionRequest<TCommand>(
    CommandId CommandId,
    TCommand Command)
    where TCommand : notnull;
