using YowThi.Erp.Domain.Common;

namespace YowThi.Erp.Domain.SalesHandling;

public sealed class SalesPackagingWorkRecord : IHasRowVersion
{
    public Guid Id { get; private set; }
    public Guid SalesId { get; private set; }
    public DateOnly WorkDate { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid SalesPackagingItemId { get; private set; }
    public long ConfirmedWageThb { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public Guid RecordedByAccountId { get; private set; }
    public long RowVersion { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedByAccountId { get; private set; }
}
