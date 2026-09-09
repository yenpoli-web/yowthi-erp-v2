using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Inventory;
using YowThi.Erp.Domain.Infrastructure;
using YowThi.Erp.Domain.Inventory;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.ProcessingConfiguration;
using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Infrastructure.Persistence.Inventory;

internal sealed class EfInventoryPositionReader : IInventoryPositionReader
{
    private readonly ErpDbContext _dbContext;

    public EfInventoryPositionReader(ErpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<InventoryPositionPage> GetAsync(InventoryPositionQuery query, CancellationToken cancellationToken)
    {
        var source = _dbContext.Set<InventoryPosition>().AsNoTracking().AsQueryable();
        if (query.Origin is { } origin) source = source.Where(x => x.Origin == origin);
        if (query.ObjectKind is { } objectKind) source = source.Where(x => x.InventoryObjectKind == objectKind);
        if (query.StorageLocationId is { } locationId) source = source.Where(x => x.StorageLocationId == locationId);
        if (!query.IncludeZeroBalance) source = source.Where(x => x.BalanceQuantity != 0);
        if (query.WarehouseId is { } warehouseId)
        {
            source = source.Where(x => _dbContext.Set<StorageLocation>()
                .Any(location => location.Id == x.StorageLocationId && location.WarehouseId == warehouseId));
        }

        var projected = source.Select(x => new
        {
            x.Id,
            x.Origin,
            SourceBatchId = x.Origin == InventoryOrigin.IN_HOUSE ? x.ProcurementBatchId!.Value : x.OutsourcedSupplyBatchId!.Value,
            ObjectKind = x.InventoryObjectKind,
            ObjectId = x.InventoryObjectKind == InventoryObjectKind.PROCUREMENT_PRODUCT
                ? x.ProcurementProductId!.Value
                : x.InventoryObjectKind == InventoryObjectKind.PROCESS_MATERIAL
                    ? x.ProcessMaterialId!.Value
                    : x.SalesProductId!.Value,
            ObjectDisplayName = x.InventoryObjectKind == InventoryObjectKind.PROCUREMENT_PRODUCT
                ? _dbContext.Set<ProcurementProduct>().Where(item => item.Id == x.ProcurementProductId)
                    .Select(item => query.Locale == "th-TH"
                        ? item.NameThTh ?? item.NameZhTw ?? item.Id.ToString()
                        : item.NameZhTw ?? item.NameThTh ?? item.Id.ToString()).First()
                : x.InventoryObjectKind == InventoryObjectKind.PROCESS_MATERIAL
                    ? _dbContext.Set<ProcessMaterial>().Where(item => item.Id == x.ProcessMaterialId)
                        .Select(item => query.Locale == "th-TH"
                        ? item.NameThTh ?? item.NameZhTw ?? item.Id.ToString()
                        : item.NameZhTw ?? item.NameThTh ?? item.Id.ToString()).First()
                    : _dbContext.Set<SalesProduct>().Where(item => item.Id == x.SalesProductId)
                        .Select(item => query.Locale == "th-TH"
                        ? item.NameThTh ?? item.NameZhTw ?? item.Id.ToString()
                        : item.NameZhTw ?? item.NameThTh ?? item.Id.ToString()).First(),
            x.StorageLocationId,
            StorageLocationDisplayName = _dbContext.Set<StorageLocation>().Where(location => location.Id == x.StorageLocationId)
                .Select(location => query.Locale == "th-TH"
                    ? location.NameThTh ?? location.NameZhTw ?? location.Code ?? location.Id.ToString()
                    : location.NameZhTw ?? location.NameThTh ?? location.Code ?? location.Id.ToString()).First(),
            WarehouseId = _dbContext.Set<StorageLocation>().Where(location => location.Id == x.StorageLocationId)
                .Select(location => location.WarehouseId).First(),
            WarehouseDisplayName = _dbContext.Set<StorageLocation>().Where(location => location.Id == x.StorageLocationId)
                .Select(location => _dbContext.Set<Warehouse>().Where(warehouse => warehouse.Id == location.WarehouseId)
                    .Select(warehouse => query.Locale == "th-TH"
                        ? warehouse.NameThTh ?? warehouse.NameZhTw ?? warehouse.Code ?? warehouse.Id.ToString()
                        : warehouse.NameZhTw ?? warehouse.NameThTh ?? warehouse.Code ?? warehouse.Id.ToString()).First()).First(),
            x.RawSourceKind,
            x.SupplierId,
            SupplierDisplayName = x.SupplierId == null ? null : _dbContext.Set<Supplier>().Where(supplier => supplier.Id == x.SupplierId.Value)
                .Select(supplier => query.Locale == "th-TH"
                    ? supplier.Code ?? supplier.NameThTh ?? supplier.NameZhTw ?? supplier.Id.ToString()
                    : supplier.Code ?? supplier.NameZhTw ?? supplier.NameThTh ?? supplier.Id.ToString()).FirstOrDefault(),
            x.BalanceQuantity,
            x.RowVersion,
        });

        var search = query.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            projected = projected.Where(x =>
                x.ObjectDisplayName.Contains(search)
                || x.StorageLocationDisplayName.Contains(search)
                || x.WarehouseDisplayName.Contains(search)
                || (x.SupplierDisplayName != null && x.SupplierDisplayName.Contains(search)));
        }

        var rows = await projected
            .OrderBy(x => x.WarehouseDisplayName)
            .ThenBy(x => x.StorageLocationDisplayName)
            .ThenBy(x => x.ObjectDisplayName)
            .ThenBy(x => x.Id)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        return new InventoryPositionPage(
            rows.Select(x => new InventoryPositionItem(
                x.Id,
                x.Origin,
                x.SourceBatchId,
                x.ObjectKind,
                x.ObjectId,
                x.ObjectDisplayName,
                x.WarehouseId,
                x.WarehouseDisplayName,
                x.StorageLocationId,
                x.StorageLocationDisplayName,
                x.RawSourceKind,
                x.SupplierId,
                x.SupplierDisplayName,
                x.BalanceQuantity,
                x.RowVersion)).ToArray(),
            hasMore ? query.Offset + query.Limit : null);
    }
}
