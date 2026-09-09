using YowThi.Erp.Domain.Common;
using YowThi.Erp.Domain.ProcessingConfiguration;

namespace YowThi.Erp.Domain.Processing;

public sealed class ProcessingExecutionOutput : IHasRowVersion
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
    public long RowVersion { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedByAccountId { get; private set; }

    public static ProcessingExecutionOutput CreateProcessMaterial(
        Guid id,
        Guid processingExecutionId,
        Guid moduleOutputId,
        decimal wageRate,
        decimal observedScaleReading,
        int? actualContainerCount,
        decimal? tareWeightSnapshot)
    {
        var derived = actualContainerCount is null
            ? observedScaleReading
            : observedScaleReading - (actualContainerCount.Value * (tareWeightSnapshot ?? throw new ArgumentNullException(nameof(tareWeightSnapshot))));

        if (id == Guid.Empty || processingExecutionId == Guid.Empty || moduleOutputId == Guid.Empty
            || wageRate < 0 || observedScaleReading < 0 || actualContainerCount < 0 || derived < 0)
        {
            throw new ArgumentException("Processing material output is invalid.");
        }

        return new ProcessingExecutionOutput
        {
            Id = id,
            ProcessingExecutionId = processingExecutionId,
            ProcessingModuleOutputId = moduleOutputId,
            OutputKindSnapshot = ProcessingOutputKind.PROCESS_MATERIAL,
            ConfiguredWageRateSnapshot = wageRate,
            ObservedScaleReading = observedScaleReading,
            ActualContainerCount = actualContainerCount,
            TareWeightSnapshot = actualContainerCount is null ? null : tareWeightSnapshot,
            DerivedNetQuantity = derived,
        };
    }

    public static ProcessingExecutionOutput CreateSalesProduct(
        Guid id,
        Guid processingExecutionId,
        Guid moduleOutputId,
        decimal wageRate,
        decimal completedQuantity,
        decimal packagingWeight)
    {
        if (id == Guid.Empty || processingExecutionId == Guid.Empty || moduleOutputId == Guid.Empty
            || wageRate < 0 || completedQuantity < 0 || packagingWeight <= 0)
        {
            throw new ArgumentException("Final packaging output is invalid.");
        }

        return new ProcessingExecutionOutput
        {
            Id = id,
            ProcessingExecutionId = processingExecutionId,
            ProcessingModuleOutputId = moduleOutputId,
            OutputKindSnapshot = ProcessingOutputKind.SALES_PRODUCT,
            ConfiguredWageRateSnapshot = wageRate,
            CompletedQuantity = completedQuantity,
            PackagingWeightSnapshot = packagingWeight,
            SourceConsumptionQuantity = completedQuantity * packagingWeight,
        };
    }
}