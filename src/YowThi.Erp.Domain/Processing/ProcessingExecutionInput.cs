namespace YowThi.Erp.Domain.Processing;

public sealed class ProcessingExecutionInput
{
    public Guid ProcessingExecutionId { get; private set; }
    public ProcessingConsumptionBasis ConsumptionBasis { get; private set; }
    public decimal ConsumedQuantity { get; private set; }
    public decimal? ObservedScaleReading { get; private set; }
    public int? ActualContainerCount { get; private set; }
    public decimal? TareWeightSnapshot { get; private set; }
    public decimal? DerivedNetQuantity { get; private set; }
}
