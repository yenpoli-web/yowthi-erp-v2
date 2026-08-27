using YowThi.Erp.Domain.Common;

namespace YowThi.Erp.Domain.Product;

public sealed class ProcurementProduct : IHasRowVersion
{
    public Guid Id { get; private set; }
    public string? NameZhTw { get; private set; }
    public string? NameThTh { get; private set; }
    public string UnitCode { get; private set; } = null!;
    public Guid? DefaultStorageLocationId { get; private set; }
    public bool Active { get; private set; }
    public long RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedByAccountId { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedByAccountId { get; private set; }
}
