namespace YowThi.Erp.Domain.Labor;

public sealed class ProcessingWageComponent
{
    public Guid Id { get; private set; }
    public Guid EmployeeDailyWageId { get; private set; }
    public Guid ProcessingModuleOutputId { get; private set; }
    public decimal ConfiguredWageRateSnapshot { get; private set; }
    public decimal AppliedWageRate { get; private set; }
    public bool RateOverridden { get; private set; }
    public decimal AggregatedQuantity { get; private set; }
    public long AmountThb { get; private set; }
}
