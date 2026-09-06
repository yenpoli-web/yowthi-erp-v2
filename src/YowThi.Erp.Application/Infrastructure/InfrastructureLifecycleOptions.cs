namespace YowThi.Erp.Application.Infrastructure;

public sealed record InfrastructureLifecycleOptionsQuery(
    string Locale,
    string? Search,
    int Offset,
    int Limit);

public sealed record InfrastructureLifecycleOptionPage<T>(
    IReadOnlyList<T> Items,
    int? NextOffset);

public sealed record ContainerLifecycleOption(
    Guid Id,
    string DisplayName,
    decimal TareWeight,
    bool Active,
    long RowVersion,
    bool Deleted,
    DateTimeOffset? DeletedAt);

public sealed record WarehouseLifecycleOption(
    Guid Id,
    string DisplayName,
    string? Code,
    bool Active,
    long RowVersion,
    bool Deleted,
    DateTimeOffset? DeletedAt);

public interface IInfrastructureLifecycleOptionsReader
{
    ValueTask<InfrastructureLifecycleOptionPage<ContainerLifecycleOption>> GetContainersAsync(
        InfrastructureLifecycleOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<InfrastructureLifecycleOptionPage<WarehouseLifecycleOption>> GetWarehousesAsync(
        InfrastructureLifecycleOptionsQuery query,
        CancellationToken cancellationToken);
}
