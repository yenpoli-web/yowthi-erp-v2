namespace YowThi.Erp.Application.Product;

public sealed record SalesProductMasterGroupOption(Guid Id, string DisplayName);
public sealed record SalesProductMasterGroupOptions(IReadOnlyList<SalesProductMasterGroupOption> Items);

public interface ISalesProductMasterOptionsReader
{
    ValueTask<SalesProductMasterGroupOptions> GetGroupsAsync(
        string locale,
        string? search,
        int limit,
        CancellationToken cancellationToken);
}
