using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Inventory;
using YowThi.Erp.Domain.Infrastructure;
using YowThi.Erp.Domain.Inventory;
using YowThi.Erp.Domain.Outsourced;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.ProcessingConfiguration;
using YowThi.Erp.Domain.Procurement;
using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Infrastructure.Persistence.Inventory;

internal sealed class EfInventoryOperationOptionsReader(ErpDbContext dbContext) : IInventoryOperationOptionsReader
{
    public async ValueTask<InventoryOperationOptionPage<InventoryTransferSourceOption>> GetTransferSourcesAsync(
        InventoryOperationOptionsQuery query,
        CancellationToken cancellationToken)
    {
        var source =
            from position in ActiveBatchPositions(query.Locale)
            join location in dbContext.Set<StorageLocation>().AsNoTracking()
                on position.StorageLocationId equals location.Id
            where position.BalanceQuantity > 0
            select new
            {
                position.InventoryPositionId,
                position.Origin,
                position.ProcurementBatchId,
                position.OutsourcedSupplyBatchId,
                position.BatchDate,
                position.InventoryObjectKind,
                position.ProcurementProductId,
                position.ProcessMaterialId,
                position.SalesProductId,
                position.ObjectDisplayName,
                position.RawSourceKind,
                position.SupplierId,
                position.RawSourceDisplayName,
                SourceStorageLocationId = location.Id,
                SourceLocationName = query.Locale == "th-TH"
                    ? location.NameThTh ?? location.NameZhTw
                    : location.NameZhTw ?? location.NameThTh,
                location.Code,
                location.Active,
                position.BalanceQuantity,
            };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(item =>
                (item.ObjectDisplayName != null && item.ObjectDisplayName.Contains(search))
                || (item.RawSourceDisplayName != null && item.RawSourceDisplayName.Contains(search))
                || (item.SourceLocationName != null && item.SourceLocationName.Contains(search))
                || (item.Code != null && item.Code.Contains(search)));
        }

        var rows = await source
            .OrderByDescending(item => item.BatchDate)
            .ThenBy(item => item.ObjectDisplayName)
            .ThenBy(item => item.SourceLocationName)
            .ThenBy(item => item.InventoryPositionId)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        var items = rows.Take(query.Limit)
            .Select(item => new InventoryTransferSourceOption(
                item.InventoryPositionId,
                ToIdentity(
                    item.Origin,
                    item.ProcurementBatchId,
                    item.OutsourcedSupplyBatchId,
                    item.BatchDate,
                    item.InventoryObjectKind,
                    item.ProcurementProductId,
                    item.ProcessMaterialId,
                    item.SalesProductId,
                    item.ObjectDisplayName,
                    item.RawSourceKind,
                    item.SupplierId,
                    item.RawSourceDisplayName),
                item.SourceStorageLocationId,
                DisplayLocation(item.SourceLocationName, item.Code, item.SourceStorageLocationId),
                item.Active,
                item.BalanceQuantity))
            .ToArray();

        return new InventoryOperationOptionPage<InventoryTransferSourceOption>(
            items,
            hasMore ? query.Offset + query.Limit : null);
    }

    public async ValueTask<InventoryOperationOptionPage<InventoryOperationIdentityOption>> GetAdjustmentIdentitiesAsync(
        InventoryOperationOptionsQuery query,
        CancellationToken cancellationToken)
    {
        var source = ActiveBatchPositions(query.Locale)
            .Select(position => new
            {
                position.Origin,
                position.ProcurementBatchId,
                position.OutsourcedSupplyBatchId,
                position.BatchDate,
                position.InventoryObjectKind,
                position.ProcurementProductId,
                position.ProcessMaterialId,
                position.SalesProductId,
                position.ObjectDisplayName,
                position.RawSourceKind,
                position.SupplierId,
                position.RawSourceDisplayName,
            });

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(item =>
                (item.ObjectDisplayName != null && item.ObjectDisplayName.Contains(search))
                || (item.RawSourceDisplayName != null && item.RawSourceDisplayName.Contains(search)));
        }

        var rows = await source
            .Distinct()
            .OrderByDescending(item => item.BatchDate)
            .ThenBy(item => item.ObjectDisplayName)
            .ThenBy(item => item.Origin)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        var items = rows.Take(query.Limit)
            .Select(item => ToIdentity(
                item.Origin,
                item.ProcurementBatchId,
                item.OutsourcedSupplyBatchId,
                item.BatchDate,
                item.InventoryObjectKind,
                item.ProcurementProductId,
                item.ProcessMaterialId,
                item.SalesProductId,
                item.ObjectDisplayName,
                item.RawSourceKind,
                item.SupplierId,
                item.RawSourceDisplayName))
            .ToArray();

        return new InventoryOperationOptionPage<InventoryOperationIdentityOption>(
            items,
            hasMore ? query.Offset + query.Limit : null);
    }

    public ValueTask<InventoryOperationOptionPage<InventoryStorageLocationOption>> GetTransferDestinationsAsync(
        InventoryOperationOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetLocationsAsync(query, requireActive: true, cancellationToken);

    public ValueTask<InventoryOperationOptionPage<InventoryStorageLocationOption>> GetAdjustmentLocationsAsync(
        InventoryOperationOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetLocationsAsync(query, requireActive: false, cancellationToken);

    private IQueryable<PositionProjection> ActiveBatchPositions(string locale)
    {
        return dbContext.Set<InventoryPosition>()
            .AsNoTracking()
            .Where(position =>
                position.Origin == InventoryOrigin.IN_HOUSE
                    ? dbContext.Set<ProcurementBatch>().Any(batch =>
                        batch.Id == position.ProcurementBatchId!.Value
                        && batch.LifecycleStatus == ProcurementBatchLifecycleStatus.ACTIVE
                        && batch.DeletedAt == null)
                    : position.Origin == InventoryOrigin.OUTSOURCED
                        && dbContext.Set<OutsourcedSupplyBatch>().Any(batch =>
                            batch.Id == position.OutsourcedSupplyBatchId!.Value
                            && batch.LifecycleStatus == OutsourcedSupplyBatchLifecycleStatus.ACTIVE
                            && batch.DeletedAt == null))
            .Select(position => new PositionProjection(
                position.Id,
                position.Origin,
                position.ProcurementBatchId,
                position.OutsourcedSupplyBatchId,
                position.Origin == InventoryOrigin.IN_HOUSE
                    ? dbContext.Set<ProcurementBatch>()
                        .Where(batch => batch.Id == position.ProcurementBatchId!.Value)
                        .Select(batch => batch.ProcurementDate)
                        .First()
                    : dbContext.Set<OutsourcedSupplyBatch>()
                        .Where(batch => batch.Id == position.OutsourcedSupplyBatchId!.Value)
                        .Select(batch => batch.SupplyDate)
                        .First(),
                position.InventoryObjectKind,
                position.ProcurementProductId,
                position.ProcessMaterialId,
                position.SalesProductId,
                position.InventoryObjectKind == InventoryObjectKind.PROCUREMENT_PRODUCT
                    ? dbContext.Set<ProcurementProduct>()
                        .Where(item => item.Id == position.ProcurementProductId!.Value)
                        .Select(item => locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh)
                        .FirstOrDefault()
                    : position.InventoryObjectKind == InventoryObjectKind.PROCESS_MATERIAL
                        ? dbContext.Set<ProcessMaterial>()
                            .Where(item => item.Id == position.ProcessMaterialId!.Value)
                            .Select(item => locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh)
                            .FirstOrDefault()
                        : dbContext.Set<SalesProduct>()
                            .Where(item => item.Id == position.SalesProductId!.Value)
                            .Select(item => locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh)
                            .FirstOrDefault(),
                position.RawSourceKind,
                position.SupplierId,
                position.RawSourceKind == InventoryRawSourceKind.SUPPLIER
                    ? dbContext.Set<Supplier>()
                        .Where(item => item.Id == position.SupplierId!.Value)
                        .Select(item => locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh)
                        .FirstOrDefault()
                    : position.RawSourceKind == InventoryRawSourceKind.FARMERS_COMBINED
                        ? "FARMERS_COMBINED"
                        : null,
                position.StorageLocationId,
                position.BalanceQuantity));
    }

    private async ValueTask<InventoryOperationOptionPage<InventoryStorageLocationOption>> GetLocationsAsync(
        InventoryOperationOptionsQuery query,
        bool requireActive,
        CancellationToken cancellationToken)
    {
        var source = dbContext.Set<StorageLocation>()
            .AsNoTracking()
            .Where(location => location.DeletedAt == null && (!requireActive || location.Active))
            .Select(location => new
            {
                location.Id,
                Name = query.Locale == "th-TH"
                    ? location.NameThTh ?? location.NameZhTw
                    : location.NameZhTw ?? location.NameThTh,
                location.Code,
                location.Active,
            });

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(item =>
                (item.Name != null && item.Name.Contains(search))
                || (item.Code != null && item.Code.Contains(search)));
        }

        var rows = await source
            .OrderByDescending(item => item.Active)
            .ThenBy(item => item.Name)
            .ThenBy(item => item.Code)
            .ThenBy(item => item.Id)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        var items = rows.Take(query.Limit)
            .Select(item => new InventoryStorageLocationOption(
                item.Id,
                DisplayLocation(item.Name, item.Code, item.Id),
                item.Active))
            .ToArray();

        return new InventoryOperationOptionPage<InventoryStorageLocationOption>(
            items,
            hasMore ? query.Offset + query.Limit : null);
    }

    private static InventoryOperationIdentityOption ToIdentity(
        InventoryOrigin origin,
        Guid? procurementBatchId,
        Guid? outsourcedSupplyBatchId,
        DateOnly batchDate,
        InventoryObjectKind inventoryObjectKind,
        Guid? procurementProductId,
        Guid? processMaterialId,
        Guid? salesProductId,
        string? objectDisplayName,
        InventoryRawSourceKind? rawSourceKind,
        Guid? supplierId,
        string? rawSourceDisplayName) =>
        new(
            origin,
            procurementBatchId,
            outsourcedSupplyBatchId,
            batchDate,
            inventoryObjectKind,
            procurementProductId,
            processMaterialId,
            salesProductId,
            string.IsNullOrWhiteSpace(objectDisplayName)
                ? procurementProductId?.ToString() ?? processMaterialId?.ToString() ?? salesProductId?.ToString() ?? inventoryObjectKind.ToString()
                : objectDisplayName,
            rawSourceKind,
            supplierId,
            rawSourceKind == InventoryRawSourceKind.FARMERS_COMBINED
                ? "FARMERS_COMBINED"
                : rawSourceDisplayName);

    private static string DisplayLocation(string? name, string? code, Guid id)
    {
        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(code)) return $"{code} · {name}";
        if (!string.IsNullOrWhiteSpace(name)) return name;
        if (!string.IsNullOrWhiteSpace(code)) return code;
        return id.ToString();
    }

    private sealed record PositionProjection(
        Guid InventoryPositionId,
        InventoryOrigin Origin,
        Guid? ProcurementBatchId,
        Guid? OutsourcedSupplyBatchId,
        DateOnly BatchDate,
        InventoryObjectKind InventoryObjectKind,
        Guid? ProcurementProductId,
        Guid? ProcessMaterialId,
        Guid? SalesProductId,
        string? ObjectDisplayName,
        InventoryRawSourceKind? RawSourceKind,
        Guid? SupplierId,
        string? RawSourceDisplayName,
        Guid StorageLocationId,
        decimal BalanceQuantity);
}
