using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.DataProtection;
using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Infrastructure.Persistence.Product;

internal sealed class EfProductHardDeleteOptionsReader(ErpDbContext dbContext) : IProductHardDeleteOptionsReader
{
    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetProcurementProductsAsync(HardDeleteOptionsQuery query, CancellationToken cancellationToken) =>
        GetPageAsync(dbContext.Set<ProcurementProduct>().AsNoTracking().Select(item => new Projection
        {
            Id = item.Id,
            DisplayName = query.Locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh,
            Active = item.Active,
            RowVersion = item.RowVersion,
            DeletedAt = item.DeletedAt,
        }), query, cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetSalesProductsAsync(HardDeleteOptionsQuery query, CancellationToken cancellationToken) =>
        GetPageAsync(dbContext.Set<SalesProduct>().AsNoTracking().Select(item => new Projection
        {
            Id = item.Id,
            DisplayName = query.Locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh,
            Active = item.Active,
            RowVersion = item.RowVersion,
            DeletedAt = item.DeletedAt,
        }), query, cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetSalesProductGroupsAsync(HardDeleteOptionsQuery query, CancellationToken cancellationToken) =>
        GetPageAsync(dbContext.Set<SalesProductGroup>().AsNoTracking().Select(item => new Projection
        {
            Id = item.Id,
            DisplayName = query.Locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh,
            Active = item.Active,
            RowVersion = item.RowVersion,
            DeletedAt = item.DeletedAt,
        }), query, cancellationToken);

    private static async ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetPageAsync(IQueryable<Projection> source, HardDeleteOptionsQuery query, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(item => item.DisplayName != null && item.DisplayName.Contains(search));
        }

        var rows = await source.OrderBy(item => item.DeletedAt == null).ThenBy(item => item.Active).ThenBy(item => item.DisplayName).ThenBy(item => item.Id)
            .Skip(query.Offset).Take(query.Limit + 1).ToListAsync(cancellationToken);
        var hasMore = rows.Count > query.Limit;
        return new HardDeleteOptionPage<HardDeleteOption>(rows.Take(query.Limit).Select(item => new HardDeleteOption(
            item.Id,
            string.IsNullOrWhiteSpace(item.DisplayName) ? item.Id.ToString() : item.DisplayName,
            item.Active,
            item.RowVersion,
            item.DeletedAt is not null,
            item.DeletedAt)).ToArray(), hasMore ? query.Offset + query.Limit : null);
    }

    private sealed class Projection
    {
        public Guid Id { get; init; }
        public string? DisplayName { get; init; }
        public bool Active { get; init; }
        public long RowVersion { get; init; }
        public DateTimeOffset? DeletedAt { get; init; }
    }
}