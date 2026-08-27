using YowThi.Erp.Application.Common.Identity;

namespace YowThi.Erp.Application.Common.Idempotency;

public sealed record CommandExecutionStart(
    CommandId CommandId,
    CommandType CommandType,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    DateTimeOffset StartedAt);
