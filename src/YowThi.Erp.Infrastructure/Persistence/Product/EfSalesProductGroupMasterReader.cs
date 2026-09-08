using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Product;
using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Infrastructure.Persistence.Product;

internal sealed class EfSalesProductGroupMasterReader : ISalesProductGroupMasterReader
{
    private readonly ErpDbContext _dbContext;

    public EfSalesProductGroupMasterReader(ErpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<SalesProductGroupMasterPage> GetAsync(
        SalesProductGroupMasterQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<SalesProductGroup> salesProductGroups = _dbContext.Set<SalesProductGroup>().AsNoTracking();
        salesProductGroups = query.Status switch
        {
            SalesProductGroupMasterStatusFilter.Active => salesProductGroups.Where(x => x.DeletedAt == null && x.Active),
            SalesProductGroupMasterStatusFilter.Inactive => salesProductGroups.Where(x => x.DeletedAt == null && !x.Active),
            SalesProductGroupMasterStatusFilter.Deleted => salesProductGroups.Where(x => x.DeletedAt != null),
            _ => salesProductGroups,
        };

        var search = query.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            salesProductGroups = salesProductGroups.Where(x =>
                (x.NameZhTw != null && EF.Functions.ILike(x.NameZhTw, pattern))
                || (x.NameThTh != null && EF.Functions.ILike(x.NameThTh, pattern)));
        }

        var rows = await salesProductGroups
            .OrderBy(x => x.NameZhTw ?? x.NameThTh ?? string.Empty)
            .ThenBy(x => x.Id)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .Select(x => new SalesProductGroupMasterItem(
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

        return new SalesProductGroupMasterPage(
            rows,
            hasMore ? query.Offset + query.Limit : null);
    }
}
