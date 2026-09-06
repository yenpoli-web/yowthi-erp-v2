namespace YowThi.Erp.Application.Product;

public sealed record SalesProductGroupLifecycleOptionsQuery(
    string Locale,
    string? Search,
    int Offset,
    int Limit);

public sealed record SalesProductGroupLifecycleOptionPage<T>(
    IReadOnlyList<T> Items,
    int? NextOffset);

public sealed record SalesProductGroupLifecycleOption(
    Guid Id,
    string DisplayName,
    bool Active,
    long RowVersion,
    bool Deleted,
    DateTimeOffset? DeletedAt);

public interface ISalesProductGroupLifecycleOptionsReader
{
    ValueTask<SalesProductGroupLifecycleOptionPage<SalesProductGroupLifecycleOption>> GetAsync(
        SalesProductGroupLifecycleOptionsQuery query,
        CancellationToken cancellationToken);
}
