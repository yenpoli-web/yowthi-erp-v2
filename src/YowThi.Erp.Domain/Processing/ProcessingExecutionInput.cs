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

    public static ProcessingExecutionInput FromScale(
        Guid processingExecutionId,
        decimal observedScaleReading,
        int? actualContainerCount,
        decimal? tareWeightSnapshot)
    {
        var derived = actualContainerCount is null
            ? observedScaleReading
            : observedScaleReading - (actualContainerCount.Value * (tareWeightSnapshot ?? throw new ArgumentNullException(nameof(tareWeightSnapshot))));

        if (processingExecutionId == Guid.Empty || observedScaleReading < 0 || actualContainerCount < 0 || derived < 0)
        {
            throw new ArgumentException("Processing scale input is invalid.");
        }

        return new ProcessingExecutionInput
        {
            ProcessingExecutionId = processingExecutionId,
            ConsumptionBasis = ProcessingConsumptionBasis.SCALE_NET,
            ConsumedQuantity = derived,
            ObservedScaleReading = observedScaleReading,
            ActualContainerCount = actualContainerCount,
            TareWeightSnapshot = actualContainerCount is null ? null : tareWeightSnapshot,
            DerivedNetQuantity = derived,
        };
    }

    public static ProcessingExecutionInput FromOutputQuantity(Guid processingExecutionId, decimal consumedQuantity) =>
        CreateDerived(processingExecutionId, ProcessingConsumptionBasis.OUTPUT_QUANTITY, consumedQuantity);

    public static ProcessingExecutionInput FromPackagingWeight(Guid processingExecutionId, decimal consumedQuantity) =>
        CreateDerived(processingExecutionId, ProcessingConsumptionBasis.PACKAGING_WEIGHT, consumedQuantity);

    private static ProcessingExecutionInput CreateDerived(
        Guid processingExecutionId,
        ProcessingConsumptionBasis basis,
        decimal consumedQuantity)
    {
        if (processingExecutionId == Guid.Empty || consumedQuantity < 0)
        {
            throw new ArgumentException("Processing derived consumption is invalid.");
        }

        return new ProcessingExecutionInput
        {
            ProcessingExecutionId = processingExecutionId,
            ConsumptionBasis = basis,
            ConsumedQuantity = consumedQuantity,
        };
    }
}