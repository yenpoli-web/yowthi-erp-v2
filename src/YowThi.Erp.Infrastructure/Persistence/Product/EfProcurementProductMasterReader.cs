using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Product;
using YowThi.Erp.Domain.Infrastructure;
using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Infrastructure.Persistence.Product;

internal sealed class EfProcurementProductMasterReader : IProcurementProductMasterReader
{
    private readonly ErpDbContext _dbContext;

    public EfProcurementProductMasterReader(ErpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<ProcurementProductMasterPage> GetAsync(ProductMasterQuery query, CancellationToken cancellationToken)
    {
        IQueryable<ProcurementProduct> source = _dbContext.Set<ProcurementProduct>().AsNoTracking();
        source = query.Status switch
        {
            ProductMasterStatusFilter.Active => source.Where(x => x.DeletedAt == null && x.Active),
            ProductMasterStatusFilter.Inactive => source.Where(x => x.DeletedAt == null && !x.Active),
            ProductMasterStatusFilter.Deleted => source.Where(x => x.DeletedAt != null),
            _ => source,
        };

        var search = query.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            source = source.Where(x =>
                (x.NameZhTw != null && EF.Functions.ILike(x.NameZhTw, pattern))
                || (x.NameThTh != null && EF.Functions.ILike(x.NameThTh, pattern))
                || EF.Functions.ILike(x.UnitCode, pattern));
        }

        var rows = await source
            .OrderBy(x => x.NameZhTw ?? x.NameThTh ?? string.Empty)
            .ThenBy(x => x.Id)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .Select(x => new ProcurementProductMasterItem(
                x.Id,
                x.NameZhTw,
                x.NameThTh,
                x.UnitCode,
                x.DefaultStorageLocationId,
                x.DefaultStorageLocationId == null ? null : _dbContext.Set<StorageLocation>()
                    .Where(location => location.Id == x.DefaultStorageLocationId.Value)
                    .Select(location => query.Locale == "th-TH"
                        ? location.NameThTh ?? location.NameZhTw ?? location.Code ?? location.Id.ToString()
                        : location.NameZhTw ?? location.NameThTh ?? location.Code ?? location.Id.ToString())
                    .FirstOrDefault(),
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

        return new ProcurementProductMasterPage(rows, hasMore ? query.Offset + query.Limit : null);
    }
}
