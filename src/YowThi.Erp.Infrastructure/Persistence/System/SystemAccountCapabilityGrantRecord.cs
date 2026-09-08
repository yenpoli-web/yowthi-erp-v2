using YowThi.Erp.Domain.Common;

namespace YowThi.Erp.Infrastructure.Persistence.System;

internal sealed class SystemAccountCapabilityGrantRecord : IHasRowVersion
{
    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public string CapabilityName { get; private set; } = null!;
    public bool Active { get; private set; }
    public long RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedByAccountId { get; private set; }
}
