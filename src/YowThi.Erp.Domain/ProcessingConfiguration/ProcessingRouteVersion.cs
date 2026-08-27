namespace YowThi.Erp.Domain.ProcessingConfiguration;

public sealed class ProcessingRouteVersion
{
    public Guid Id { get; private set; }
    public Guid ProcessingRouteId { get; private set; }
    public int VersionNumber { get; private set; }
    public ProcessingRouteVersionStatus Status { get; private set; }
}
