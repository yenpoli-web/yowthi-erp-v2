using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Outsourced;
using YowThi.Erp.Domain.Infrastructure;
using YowThi.Erp.Domain.Inventory;
using YowThi.Erp.Domain.Outsourced;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Infrastructure.Persistence.Outsourced;

internal sealed class EfOutsourcedWorkspaceReader(ErpDbContext dbContext) : IOutsourcedWorkspaceReader
{
    private const string LocaleZhTw = "zh-TW";
    private const string LocaleThTh = "th-TH";

    public async ValueTask<OutsourcedWorkspaceListPage> GetBatchesAsync(
        OutsourcedWorkspaceListQuery query,
        CancellationToken cancellationToken)
    {
        var locale = ValidateLocale(query.Locale);
        if (query.Offset < 0 || query.Limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(query));

        var source =
            from batch in dbContext.Set<OutsourcedSupplyBatch>().AsNoTracking()
            join vendor in dbContext.Set<OutsourcedVendor>().AsNoTracking() on batch.OutsourcedVendorId equals vendor.Id
            select new
            {
                batch.Id,
                batch.SupplyDate,
                batch.OutsourcedVendorId,
                batch.LifecycleStatus,
                batch.RowVersion,
                batch.CreatedAt,
                batch.ClosedAt,
                batch.DeletedAt,
                VendorNameZhTw = vendor.NameZhTw,
                VendorNameThTh = vendor.NameThTh,
            };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(item =>
                (item.VendorNameZhTw != null && item.VendorNameZhTw.Contains(search))
                || (item.VendorNameThTh != null && item.VendorNameThTh.Contains(search)));
        }

        var rows = await source
            .OrderByDescending(item => item.SupplyDate)
            .ThenBy(item => item.Id)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        var items = rows.Take(query.Limit)
            .Select(item => new OutsourcedWorkspaceListItem(
                item.Id,
                item.SupplyDate,
                item.OutsourcedVendorId,
                DisplayName(item.VendorNameZhTw, item.VendorNameThTh, locale, item.OutsourcedVendorId),
                item.LifecycleStatus.ToString(),
                item.RowVersion,
                item.CreatedAt,
                item.ClosedAt,
                item.DeletedAt))
            .ToArray();

        return new OutsourcedWorkspaceListPage(items, hasMore ? query.Offset + query.Limit : null);
    }

    public async ValueTask<OutsourcedWorkspace?> GetBatchAsync(
        Guid outsourcedSupplyBatchId,
        string locale,
        CancellationToken cancellationToken)
    {
        if (outsourcedSupplyBatchId == Guid.Empty)
            throw new ArgumentException("Outsourced Supply Batch ID cannot be empty.", nameof(outsourcedSupplyBatchId));
        locale = ValidateLocale(locale);

        var batch = await (
            from candidate in dbContext.Set<OutsourcedSupplyBatch>().AsNoTracking()
            join vendor in dbContext.Set<OutsourcedVendor>().AsNoTracking() on candidate.OutsourcedVendorId equals vendor.Id
            where candidate.Id == outsourcedSupplyBatchId
            select new
            {
                candidate.Id,
                candidate.SupplyDate,
                candidate.OutsourcedVendorId,
                candidate.LifecycleStatus,
                candidate.RowVersion,
                candidate.CreatedAt,
                candidate.ClosedAt,
                candidate.DeletedAt,
                VendorNameZhTw = vendor.NameZhTw,
                VendorNameThTh = vendor.NameThTh,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (batch is null) return null;

        var details = await (
            from detail in dbContext.Set<OutsourcedSupplyDetail>().AsNoTracking()
            join product in dbContext.Set<SalesProduct>().AsNoTracking() on detail.SalesProductId equals product.Id
            where detail.OutsourcedSupplyBatchId == outsourcedSupplyBatchId
            orderby detail.RecordedAt, detail.Id
            select new
            {
                detail.Id,
                detail.SalesProductId,
                detail.Quantity,
                detail.PricingBasisSnapshot,
                detail.UnitPrice,
                detail.AmountThb,
                detail.RowVersion,
                detail.RecordedAt,
                detail.DeletedAt,
                ProductNameZhTw = product.NameZhTw,
                ProductNameThTh = product.NameThTh,
            })
            .ToListAsync(cancellationToken);

        var detailIds = details.Select(detail => detail.Id).ToArray();
        var receiptLocations = detailIds.Length == 0
            ? []
            : await (
                from operation in dbContext.Set<InventoryOperation>().AsNoTracking()
                join movement in dbContext.Set<InventoryMovement>().AsNoTracking()
                    on operation.Id equals movement.InventoryOperationId
                join location in dbContext.Set<StorageLocation>().AsNoTracking()
                    on movement.StorageLocationId equals location.Id
                where operation.OutsourcedSupplyDetailId.HasValue
                    && detailIds.Contains(operation.OutsourcedSupplyDetailId ?? Guid.Empty)
                    && movement.MovementType == InventoryMovementType.OUTSOURCED_RECEIPT
                select new ReceiptLocationRow(
                    operation.OutsourcedSupplyDetailId ?? Guid.Empty,
                    location.Id,
                    location.NameZhTw,
                    location.NameThTh))
                .ToListAsync(cancellationToken);

        var receiptLocationByDetailId = receiptLocations
            .GroupBy(location => location.DetailId)
            .ToDictionary(group => group.Key, group => group.Single());

        return new OutsourcedWorkspace(
            batch.Id,
            batch.SupplyDate,
            batch.OutsourcedVendorId,
            DisplayName(batch.VendorNameZhTw, batch.VendorNameThTh, locale, batch.OutsourcedVendorId),
            batch.LifecycleStatus.ToString(),
            batch.RowVersion,
            batch.CreatedAt,
            batch.ClosedAt,
            batch.DeletedAt,
            details.Select(detail =>
            {
                if (!receiptLocationByDetailId.TryGetValue(detail.Id, out var receiptLocation))
                {
                    throw new InvalidOperationException($"Outsourced Supply Detail {detail.Id} has no OUTSOURCED_RECEIPT location.");
                }

                return new OutsourcedWorkspaceDetailItem(
                    detail.Id,
                    detail.SalesProductId,
                    DisplayName(detail.ProductNameZhTw, detail.ProductNameThTh, locale, detail.SalesProductId),
                    detail.Quantity,
                    detail.PricingBasisSnapshot.ToString(),
                    detail.UnitPrice,
                    detail.AmountThb,
                    receiptLocation.StorageLocationId,
                    DisplayName(receiptLocation.NameZhTw, receiptLocation.NameThTh, locale, receiptLocation.StorageLocationId),
                    detail.RowVersion,
                    detail.RecordedAt,
                    detail.DeletedAt);
            }).ToArray());
    }

    private sealed record ReceiptLocationRow(
        Guid DetailId,
        Guid StorageLocationId,
        string? NameZhTw,
        string? NameThTh);

    private static string DisplayName(string? zhTw, string? thTh, string locale, Guid fallbackId) =>
        locale == LocaleZhTw
            ? zhTw ?? thTh ?? fallbackId.ToString()
            : thTh ?? zhTw ?? fallbackId.ToString();

    private static string ValidateLocale(string locale) => locale switch
    {
        LocaleZhTw => LocaleZhTw,
        LocaleThTh => LocaleThTh,
        _ => throw new ArgumentOutOfRangeException(nameof(locale), locale, "Unsupported operational locale."),
    };
}