namespace YowThi.Erp.Domain.Labor;

public sealed class SalesPackagingWageComponent
{
    public Guid Id { get; private set; }
    public Guid EmployeeDailyWageId { get; private set; }
    public Guid SalesPackagingWorkRecordId { get; private set; }
    public long WageAmountThb { get; private set; }
}
