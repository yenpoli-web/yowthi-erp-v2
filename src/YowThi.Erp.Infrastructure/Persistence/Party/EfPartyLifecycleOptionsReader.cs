using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Party;
using YowThi.Erp.Domain.Party;

namespace YowThi.Erp.Infrastructure.Persistence.Party;

internal sealed class EfPartyLifecycleOptionsReader(ErpDbContext dbContext) : IPartyLifecycleOptionsReader
{
    public ValueTask<PartyLifecycleOptionPage<PartyLifecycleOption>> GetSuppliersAsync(
        PartyLifecycleOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<Supplier>().AsNoTracking().Select(item => new PartyProjection(
                item.Id,
                query.Locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh,
                item.Active,
                item.RowVersion,
                item.DeletedAt)),
            query,
            cancellationToken);

    public ValueTask<PartyLifecycleOptionPage<PartyLifecycleOption>> GetCustomersAsync(
        PartyLifecycleOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<Customer>().AsNoTracking().Select(item => new PartyProjection(
                item.Id,
                query.Locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh,
                item.Active,
                item.RowVersion,
                item.DeletedAt)),
            query,
            cancellationToken);

    public ValueTask<PartyLifecycleOptionPage<PartyLifecycleOption>> GetOutsourcedVendorsAsync(
        PartyLifecycleOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<OutsourcedVendor>().AsNoTracking().Select(item => new PartyProjection(
                item.Id,
                query.Locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh,
                item.Active,
                item.RowVersion,
                item.DeletedAt)),
            query,
            cancellationToken);

    public ValueTask<PartyLifecycleOptionPage<PartyLifecycleOption>> GetFarmersAsync(
        PartyLifecycleOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<Farmer>().AsNoTracking().Select(item => new PartyProjection(
                item.Id,
                query.Locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh,
                item.Active,
                item.RowVersion,
                item.DeletedAt)),
            query,
            cancellationToken);

    public ValueTask<PartyLifecycleOptionPage<PartyLifecycleOption>> GetEmployeesAsync(
        PartyLifecycleOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<Employee>().AsNoTracking().Select(item => new PartyProjection(
                item.Id,
                query.Locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh,
                item.Active,
                item.RowVersion,
                item.DeletedAt)),
            query,
            cancellationToken);

    private static async ValueTask<PartyLifecycleOptionPage<PartyLifecycleOption>> GetPageAsync(
        IQueryable<PartyProjection> source,
        PartyLifecycleOptionsQuery query,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(item => item.DisplayName != null && item.DisplayName.Contains(search));
        }

        var rows = await source
            .OrderBy(item => item.DeletedAt != null)
            .ThenByDescending(item => item.Active)
            .ThenBy(item => item.DisplayName)
            .ThenBy(item => item.Id)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        var items = rows.Take(query.Limit)
            .Select(item => new PartyLifecycleOption(
                item.Id,
                string.IsNullOrWhiteSpace(item.DisplayName) ? item.Id.ToString() : item.DisplayName,
                item.Active,
                item.RowVersion,
                item.DeletedAt is not null,
                item.DeletedAt))
            .ToArray();

        return new PartyLifecycleOptionPage<PartyLifecycleOption>(
            items,
            hasMore ? query.Offset + query.Limit : null);
    }

    private sealed record PartyProjection(
        Guid Id,
        string? DisplayName,
        bool Active,
        long RowVersion,
        DateTimeOffset? DeletedAt);
}
