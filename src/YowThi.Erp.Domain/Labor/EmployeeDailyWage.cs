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

    public static EmployeeDailyWage Create(
        Guid id,
        DateOnly workDate,
        Guid employeeId,
        long processingWageTotalThb,
        long salesPackagingWageTotalThb,
        DateTimeOffset confirmedAt,
        Guid confirmedByAccountId)
    {
        if (id == Guid.Empty
            || employeeId == Guid.Empty
            || confirmedByAccountId == Guid.Empty
            || processingWageTotalThb < 0
            || salesPackagingWageTotalThb < 0)
        {
            throw new ArgumentException("Employee Daily Wage is invalid.");
        }

        var total = checked(processingWageTotalThb + salesPackagingWageTotalThb);
        return new EmployeeDailyWage
        {
            Id = id,
            WorkDate = workDate,
            EmployeeId = employeeId,
            ProcessingWageTotalThb = processingWageTotalThb,
            SalesPackagingWageTotalThb = salesPackagingWageTotalThb,
            TotalWageThb = total,
            ConfirmedAt = confirmedAt,
            ConfirmedByAccountId = confirmedByAccountId,
            RowVersion = 1,
        };
    }
}
