using YowThi.Erp.Domain.Common;

namespace YowThi.Erp.Domain.Labor;

public sealed class EmployeeDailyWage : IHasRowVersion
{
    public Guid Id { get; private set; }
    public DateOnly WorkDate { get; private set; }
    public Guid EmployeeId { get; private set; }
    public long ProcessingWageTotalThb { get; private set; }
    public long SalesPackagingWageTotalThb { get; private set; }
    public long TotalWageThb { get; private set; }
    public DateTimeOffset ConfirmedAt { get; private set; }
    public Guid ConfirmedByAccountId { get; private set; }
    public long RowVersion { get; private set; }
}
