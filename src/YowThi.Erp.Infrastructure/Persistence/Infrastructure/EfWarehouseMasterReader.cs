using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Infrastructure;
using YowThi.Erp.Domain.Infrastructure;

namespace YowThi.Erp.Infrastructure.Persistence.Infrastructure;

internal sealed class EfWarehouseMasterReader : IWarehouseMasterReader
{
    private readonly ErpDbContext _dbContext;

    public EfWarehouseMasterReader(ErpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<WarehouseMasterPage> GetAsync(WarehouseMasterQuery query, CancellationToken cancellationToken)
    {
        IQueryable<Warehouse> source = _dbContext.Set<Warehouse>().AsNoTracking();
        source = query.Status switch
        {
            InfrastructureMasterStatusFilter.Active => source.Where(x => x.DeletedAt == null && x.Active),
            InfrastructureMasterStatusFilter.Inactive => source.Where(x => x.DeletedAt == null && !x.Active),
            InfrastructureMasterStatusFilter.Deleted => source.Where(x => x.DeletedAt != null),
            _ => source,
        };

        var search = query.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            source = source.Where(x =>
                (x.Code != null && EF.Functions.ILike(x.Code, pattern))
                || (x.NameZhTw != null && EF.Functions.ILike(x.NameZhTw, pattern))
                || (x.NameThTh != null && EF.Functions.ILike(x.NameThTh, pattern)));
        }

        var rows = await source
            .OrderBy(x => x.NameZhTw ?? x.NameThTh ?? x.Code ?? string.Empty)
            .ThenBy(x => x.Id)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .Select(x => new WarehouseMasterItem(x.Id, x.Code, x.NameZhTw, x.NameThTh, x.Active, x.RowVersion, x.CreatedAt, x.DeletedAt))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        return new WarehouseMasterPage(rows, hasMore ? query.Offset + query.Limit : null);
    }
}
