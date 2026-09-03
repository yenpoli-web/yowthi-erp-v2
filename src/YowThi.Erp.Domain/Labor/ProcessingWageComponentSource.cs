namespace YowThi.Erp.Domain.Labor;

public sealed class ProcessingWageComponentSource
{
    public Guid ProcessingWageComponentId { get; private set; }
    public Guid ProcessingExecutionOutputId { get; private set; }
    public decimal QuantitySnapshot { get; private set; }

    public static ProcessingWageComponentSource Create(
        Guid processingWageComponentId,
        Guid processingExecutionOutputId,
        decimal quantitySnapshot)
    {
        if (processingWageComponentId == Guid.Empty
            || processingExecutionOutputId == Guid.Empty
            || quantitySnapshot < 0)
        {
            throw new ArgumentException("Processing Wage Component Source is invalid.");
        }

        return new ProcessingWageComponentSource
        {
            ProcessingWageComponentId = processingWageComponentId,
            ProcessingExecutionOutputId = processingExecutionOutputId,
            QuantitySnapshot = quantitySnapshot,
        };
    }
}
