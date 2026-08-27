using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;

namespace YowThi.Erp.Application.Common.Commands;

public sealed record CommandExecutionRequest<TCommand>(
    CommandId CommandId,
    CommandType CommandType,
    CommandRequestHash RequestHash,
    TCommand Command)
    where TCommand : notnull;
