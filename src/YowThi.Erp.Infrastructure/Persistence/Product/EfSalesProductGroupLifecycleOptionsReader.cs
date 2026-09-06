using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Product;
using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Infrastructure.Persistence.Product;

internal sealed class EfSalesProductGroupLifecycleOptionsReader(ErpDbContext dbContext)
    : ISalesProductGroupLifecycleOptionsReader
{
    public async ValueTask<SalesProductGroupLifecycleOptionPage<SalesProductGroupLifecycleOption>> GetAsync(
        SalesProductGroupLifecycleOptionsQuery query,
        CancellationToken cancellationToken)
    {
        var source = dbContext.Set<SalesProductGroup>()
            .AsNoTracking()
            .Select(item => new
            {
                item.Id,
                Name = query.Locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh,
                item.Active,
                item.RowVersion,
                item.DeletedAt,
            });

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(item => item.Name != null && item.Name.Contains(search));
        }

        var rows = await source
            .OrderBy(item => item.DeletedAt != null)
            .ThenByDescending(item => item.Active)
            .ThenBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        var items = rows.Take(query.Limit)
            .Select(item => new SalesProductGroupLifecycleOption(
                item.Id,
                string.IsNullOrWhiteSpace(item.Name) ? item.Id.ToString() : item.Name,
                item.Active,
                item.RowVersion,
                item.DeletedAt is not null,
                item.DeletedAt))
            .ToArray();

        return new SalesProductGroupLifecycleOptionPage<SalesProductGroupLifecycleOption>(
            items,
            hasMore ? query.Offset + query.Limit : null);
    }
}
