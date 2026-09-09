using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Product;
using YowThi.Erp.Domain.Infrastructure;

namespace YowThi.Erp.Infrastructure.Persistence.Product;

internal sealed class EfProductMasterOptionsReader : IProductMasterOptionsReader
{
    private readonly ErpDbContext _dbContext;

    public EfProductMasterOptionsReader(ErpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<ProductMasterStorageLocationOptions> GetStorageLocationsAsync(
        string locale,
        string? search,
        int limit,
        CancellationToken cancellationToken)
    {
        var source = _dbContext.Set<StorageLocation>()
            .AsNoTracking()
            .Where(x => x.Active && x.DeletedAt == null);

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
            .OrderBy(x => locale == "th-TH" ? x.NameThTh ?? x.NameZhTw ?? x.Code ?? string.Empty : x.NameZhTw ?? x.NameThTh ?? x.Code ?? string.Empty)
            .ThenBy(x => x.Id)
            .Take(limit)
            .Select(x => new ProductMasterStorageLocationOption(
                x.Id,
                locale == "th-TH" ? x.NameThTh ?? x.NameZhTw ?? x.Code ?? x.Id.ToString() : x.NameZhTw ?? x.NameThTh ?? x.Code ?? x.Id.ToString(),
                x.Code,
                x.WarehouseId,
                _dbContext.Set<Warehouse>()
                    .Where(warehouse => warehouse.Id == x.WarehouseId)
                    .Select(warehouse => locale == "th-TH"
                        ? warehouse.NameThTh ?? warehouse.NameZhTw ?? warehouse.Code ?? warehouse.Id.ToString()
                        : warehouse.NameZhTw ?? warehouse.NameThTh ?? warehouse.Code ?? warehouse.Id.ToString())
                    .First()))
            .ToListAsync(cancellationToken);

        return new ProductMasterStorageLocationOptions(rows);
    }
}
