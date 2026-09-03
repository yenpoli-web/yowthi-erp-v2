namespace YowThi.Erp.Domain.Labor;

public sealed class SalesPackagingWageComponent
{
    public Guid Id { get; private set; }
    public Guid EmployeeDailyWageId { get; private set; }
    public Guid SalesPackagingWorkRecordId { get; private set; }
    public long WageAmountThb { get; private set; }

    public static SalesPackagingWageComponent Create(
        Guid id,
        Guid employeeDailyWageId,
        Guid salesPackagingWorkRecordId,
        long wageAmountThb)
    {
        if (id == Guid.Empty
            || employeeDailyWageId == Guid.Empty
            || salesPackagingWorkRecordId == Guid.Empty
            || wageAmountThb < 0)
        {
            throw new ArgumentException("Sales Packaging Wage Component is invalid.");
        }

        return new SalesPackagingWageComponent
        {
            Id = id,
            EmployeeDailyWageId = employeeDailyWageId,
            SalesPackagingWorkRecordId = salesPackagingWorkRecordId,
            WageAmountThb = wageAmountThb,
        };
    }
}
