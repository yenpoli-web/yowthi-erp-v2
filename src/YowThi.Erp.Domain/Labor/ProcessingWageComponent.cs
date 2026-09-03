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

    public static ProcessingWageComponent Create(
        Guid id,
        Guid employeeDailyWageId,
        Guid processingModuleOutputId,
        decimal configuredWageRateSnapshot,
        decimal appliedWageRate,
        bool rateOverridden,
        decimal aggregatedQuantity)
    {
        if (id == Guid.Empty
            || employeeDailyWageId == Guid.Empty
            || processingModuleOutputId == Guid.Empty
            || configuredWageRateSnapshot < 0
            || appliedWageRate < 0
            || aggregatedQuantity < 0
            || (!rateOverridden && appliedWageRate != configuredWageRateSnapshot)
            || !ProcessingWageAmount.TryCalculate(aggregatedQuantity, appliedWageRate, out var amountThb))
        {
            throw new ArgumentException("Processing Wage Component is invalid.");
        }

        return new ProcessingWageComponent
        {
            Id = id,
            EmployeeDailyWageId = employeeDailyWageId,
            ProcessingModuleOutputId = processingModuleOutputId,
            ConfiguredWageRateSnapshot = configuredWageRateSnapshot,
            AppliedWageRate = appliedWageRate,
            RateOverridden = rateOverridden,
            AggregatedQuantity = aggregatedQuantity,
            AmountThb = amountThb,
        };
    }
}
