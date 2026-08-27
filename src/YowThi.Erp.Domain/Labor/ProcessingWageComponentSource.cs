namespace YowThi.Erp.Domain.Labor;

public sealed class ProcessingWageComponentSource
{
    public Guid ProcessingWageComponentId { get; private set; }
    public Guid ProcessingExecutionOutputId { get; private set; }
    public decimal QuantitySnapshot { get; private set; }
}
