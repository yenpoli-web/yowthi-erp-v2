using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.SalesHandling;
using YowThi.Erp.Domain.SalesHandling;

namespace YowThi.Erp.Infrastructure.Persistence.SalesHandling;

internal sealed class EfSalesPackagingItemMasterReader : ISalesPackagingItemMasterReader
{
    private readonly ErpDbContext _dbContext;

    public EfSalesPackagingItemMasterReader(ErpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<SalesPackagingItemMasterPage> GetAsync(
        SalesPackagingItemMasterQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<SalesPackagingItem> salesPackagingItems = _dbContext.Set<SalesPackagingItem>().AsNoTracking();
        salesPackagingItems = query.Status switch
        {
            SalesPackagingItemMasterStatusFilter.Active => salesPackagingItems.Where(x => x.DeletedAt == null && x.Active),
            SalesPackagingItemMasterStatusFilter.Inactive => salesPackagingItems.Where(x => x.DeletedAt == null && !x.Active),
            SalesPackagingItemMasterStatusFilter.Deleted => salesPackagingItems.Where(x => x.DeletedAt != null),
            _ => salesPackagingItems,
        };

        var search = query.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            salesPackagingItems = salesPackagingItems.Where(x =>
                (x.NameZhTw != null && EF.Functions.ILike(x.NameZhTw, pattern))
                || (x.NameThTh != null && EF.Functions.ILike(x.NameThTh, pattern)));
        }

        var rows = await salesPackagingItems
            .OrderBy(x => x.NameZhTw ?? x.NameThTh ?? string.Empty)
            .ThenBy(x => x.Id)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .Select(x => new SalesPackagingItemMasterItem(
                x.Id,
                x.NameZhTw,
                x.NameThTh,
                x.Active,
                x.RowVersion,
                x.CreatedAt,
                x.DeletedAt))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return new SalesPackagingItemMasterPage(
            rows,
            hasMore ? query.Offset + query.Limit : null);
    }
}
