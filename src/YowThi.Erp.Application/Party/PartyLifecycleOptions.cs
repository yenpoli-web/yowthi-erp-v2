namespace YowThi.Erp.Application.Party;

public sealed record PartyLifecycleOptionsQuery(
    string Locale,
    string? Search,
    int Offset,
    int Limit);

public sealed record PartyLifecycleOptionPage<T>(
    IReadOnlyList<T> Items,
    int? NextOffset);

public sealed record PartyLifecycleOption(
    Guid Id,
    string DisplayName,
    bool Active,
    long RowVersion,
    bool Deleted,
    DateTimeOffset? DeletedAt);

public interface IPartyLifecycleOptionsReader
{
    ValueTask<PartyLifecycleOptionPage<PartyLifecycleOption>> GetSuppliersAsync(
        PartyLifecycleOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<PartyLifecycleOptionPage<PartyLifecycleOption>> GetCustomersAsync(
        PartyLifecycleOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<PartyLifecycleOptionPage<PartyLifecycleOption>> GetOutsourcedVendorsAsync(
        PartyLifecycleOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<PartyLifecycleOptionPage<PartyLifecycleOption>> GetFarmersAsync(
        PartyLifecycleOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<PartyLifecycleOptionPage<PartyLifecycleOption>> GetEmployeesAsync(
        PartyLifecycleOptionsQuery query,
        CancellationToken cancellationToken);
}
