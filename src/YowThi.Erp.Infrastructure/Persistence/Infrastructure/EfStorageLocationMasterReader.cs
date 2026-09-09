using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Infrastructure;
using YowThi.Erp.Domain.Infrastructure;

namespace YowThi.Erp.Infrastructure.Persistence.Infrastructure;

internal sealed class EfStorageLocationMasterReader : IStorageLocationMasterReader
{
    private readonly ErpDbContext _dbContext;

    public EfStorageLocationMasterReader(ErpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<StorageLocationMasterPage> GetAsync(StorageLocationMasterQuery query, CancellationToken cancellationToken)
    {
        IQueryable<StorageLocation> source = _dbContext.Set<StorageLocation>().AsNoTracking();
        source = query.Status switch
        {
            InfrastructureMasterStatusFilter.Active => source.Where(x => x.DeletedAt == null && x.Active),
            InfrastructureMasterStatusFilter.Inactive => source.Where(x => x.DeletedAt == null && !x.Active),
            InfrastructureMasterStatusFilter.Deleted => source.Where(x => x.DeletedAt != null),
            _ => source,
        };
        if (query.WarehouseId is { } warehouseId) source = source.Where(x => x.WarehouseId == warehouseId);

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
            .Select(x => new StorageLocationMasterItem(
                x.Id,
                x.WarehouseId,
                _dbContext.Set<Warehouse>().Where(warehouse => warehouse.Id == x.WarehouseId)
                    .Select(warehouse => query.Locale == "th-TH"
                        ? warehouse.NameThTh ?? warehouse.NameZhTw ?? warehouse.Code ?? warehouse.Id.ToString()
                        : warehouse.NameZhTw ?? warehouse.NameThTh ?? warehouse.Code ?? warehouse.Id.ToString()).First(),
                x.Code,
                x.NameZhTw,
                x.NameThTh,
                x.Active,
                x.RowVersion,
                x.CreatedAt,
                x.DeletedAt))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        return new StorageLocationMasterPage(rows, hasMore ? query.Offset + query.Limit : null);
    }

    public async ValueTask<WarehouseMasterOptions> GetWarehouseOptionsAsync(string locale, string? search, int limit, CancellationToken cancellationToken)
    {
        var source = _dbContext.Set<Warehouse>().AsNoTracking().Where(x => x.Active && x.DeletedAt == null);
        var normalized = search?.Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            var pattern = $"%{normalized}%";
            source = source.Where(x =>
                (x.Code != null && EF.Functions.ILike(x.Code, pattern))
                || (x.NameZhTw != null && EF.Functions.ILike(x.NameZhTw, pattern))
                || (x.NameThTh != null && EF.Functions.ILike(x.NameThTh, pattern)));
        }

        var rows = await source
            .OrderBy(x => locale == "th-TH" ? x.NameThTh ?? x.NameZhTw ?? x.Code : x.NameZhTw ?? x.NameThTh ?? x.Code)
            .ThenBy(x => x.Id)
            .Take(limit)
            .Select(x => new WarehouseMasterOption(
                x.Id,
                locale == "th-TH" ? x.NameThTh ?? x.NameZhTw ?? x.Code ?? x.Id.ToString() : x.NameZhTw ?? x.NameThTh ?? x.Code ?? x.Id.ToString(),
                x.Code))
            .ToListAsync(cancellationToken);
        return new WarehouseMasterOptions(rows);
    }
}
