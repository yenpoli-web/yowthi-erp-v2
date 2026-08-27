namespace YowThi.Erp.Domain.ProcessingConfiguration;

public sealed class ProcessingModule
{
    public Guid Id { get; private set; }
    public Guid ProcessingRouteVersionId { get; private set; }
    public string? NameZhTw { get; private set; }
    public string? NameThTh { get; private set; }
    public ProcessingExecutionMode ExecutionMode { get; private set; }
    public Guid? InputProcessMaterialId { get; private set; }
    public string NegativeInventoryPolicy { get; private set; } = null!;
}
