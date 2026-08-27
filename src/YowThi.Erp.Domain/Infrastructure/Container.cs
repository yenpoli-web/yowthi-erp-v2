using YowThi.Erp.Domain.Common;

namespace YowThi.Erp.Domain.Infrastructure;

public sealed class Container : IHasRowVersion
{
    public Guid Id { get; private set; }
    public string? NameZhTw { get; private set; }
    public string? NameThTh { get; private set; }
    public decimal TareWeight { get; private set; }
    public bool Active { get; private set; }
    public long RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedByAccountId { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedByAccountId { get; private set; }
}
