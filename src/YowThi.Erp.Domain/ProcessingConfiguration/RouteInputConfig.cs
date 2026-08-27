namespace YowThi.Erp.Domain.ProcessingConfiguration;

public sealed class RouteInputConfig
{
    public Guid ProcessingRouteVersionId { get; private set; }
    public bool UsesContainer { get; private set; }
    public Guid? ContainerId { get; private set; }
    public int? DefaultContainerCount { get; private set; }
    public Guid? DefaultStorageLocationId { get; private set; }
}
