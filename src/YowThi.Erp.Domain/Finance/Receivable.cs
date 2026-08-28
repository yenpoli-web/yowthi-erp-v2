using YowThi.Erp.Domain.Common;

namespace YowThi.Erp.Domain.Finance;

public sealed class Receivable : IHasRowVersion
{
    public Guid Id { get; private set; }
    public Guid SalesId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public long RowVersion { get; private set; }
}
