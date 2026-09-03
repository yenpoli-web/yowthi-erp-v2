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

    public static SalesPackagingWorkRecord Create(
        Guid id,
        Guid salesId,
        DateOnly workDate,
        Guid employeeId,
        Guid salesPackagingItemId,
        long confirmedWageThb,
        DateTimeOffset recordedAt,
        Guid recordedByAccountId)
    {
        if (id == Guid.Empty
            || salesId == Guid.Empty
            || employeeId == Guid.Empty
            || salesPackagingItemId == Guid.Empty
            || recordedByAccountId == Guid.Empty
            || confirmedWageThb < 0)
        {
            throw new ArgumentException("Sales Packaging Work Record is invalid.");
        }

        return new SalesPackagingWorkRecord
        {
            Id = id,
            SalesId = salesId,
            WorkDate = workDate,
            EmployeeId = employeeId,
            SalesPackagingItemId = salesPackagingItemId,
            ConfirmedWageThb = confirmedWageThb,
            RecordedAt = recordedAt,
            RecordedByAccountId = recordedByAccountId,
            RowVersion = 1,
        };
    }
}
