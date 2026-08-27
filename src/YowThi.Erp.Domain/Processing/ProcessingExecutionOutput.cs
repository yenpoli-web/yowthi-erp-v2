using YowThi.Erp.Domain.ProcessingConfiguration;

namespace YowThi.Erp.Domain.Processing;

public sealed class ProcessingExecutionOutput
{
    public Guid Id { get; private set; }
    public Guid ProcessingExecutionId { get; private set; }
    public Guid ProcessingModuleOutputId { get; private set; }
    public ProcessingOutputKind OutputKindSnapshot { get; private set; }
    public decimal ConfiguredWageRateSnapshot { get; private set; }
    public decimal? ObservedScaleReading { get; private set; }
    public int? ActualContainerCount { get; private set; }
    public decimal? TareWeightSnapshot { get; private set; }
    public decimal? DerivedNetQuantity { get; private set; }
    public decimal? CompletedQuantity { get; private set; }
    public decimal? PackagingWeightSnapshot { get; private set; }
    public decimal? SourceConsumptionQuantity { get; private set; }
}
