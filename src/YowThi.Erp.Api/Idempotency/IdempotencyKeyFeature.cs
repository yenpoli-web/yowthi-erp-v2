using YowThi.Erp.Application.Common.Identity;

namespace YowThi.Erp.Api.Idempotency;

public sealed record IdempotencyKeyFeature(CommandId CommandId);
