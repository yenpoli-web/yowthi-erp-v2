namespace YowThi.Erp.Domain.ProcessingConfiguration;

public sealed class ProcessingModuleOutput
{
    public Guid Id { get; private set; }
    public Guid ProcessingModuleId { get; private set; }
    public Guid ProcessingRouteVersionId { get; private set; }
    public int OutputSequence { get; private set; }
    public ProcessingOutputKind OutputKind { get; private set; }
    public Guid? ProcessMaterialId { get; private set; }
    public Guid? SalesProductId { get; private set; }
    public decimal DefaultWageRate { get; private set; }
}
