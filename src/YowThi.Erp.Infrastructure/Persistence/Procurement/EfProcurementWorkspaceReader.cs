using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Procurement;
using YowThi.Erp.Domain.Infrastructure;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.Procurement;
using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Infrastructure.Persistence.Procurement;

internal sealed class EfProcurementWorkspaceReader(ErpDbContext dbContext) : IProcurementWorkspaceReader
{
    private const string LocaleZhTw = "zh-TW";
    private const string LocaleThTh = "th-TH";

    public async ValueTask<ProcurementBatchListPage> GetBatchesAsync(
        ProcurementBatchListQuery query,
        CancellationToken cancellationToken)
    {
        var validated = Validate(query);

        var batches =
            from batch in dbContext.Set<ProcurementBatch>().AsNoTracking()
            join product in dbContext.Set<ProcurementProduct>().AsNoTracking()
                on batch.ProcurementProductId equals product.Id
            select new
            {
                Batch = batch,
                Product = product,
            };

        if (validated.Search is not null)
        {
            var search = validated.Search;
            batches = batches.Where(item =>
                (item.Product.NameZhTw != null && item.Product.NameZhTw.Contains(search))
                || (item.Product.NameThTh != null && item.Product.NameThTh.Contains(search)));
        }

        var rows = await batches
            .OrderByDescending(item => item.Batch.ProcurementDate)
            .ThenBy(item => item.Product.NameZhTw ?? item.Product.NameThTh)
            .ThenBy(item => item.Batch.Id)
            .Skip(validated.Offset)
            .Take(validated.Limit + 1)
            .Select(item => new BatchProjection(
                item.Batch.Id,
                item.Batch.ProcurementDate,
                item.Batch.ProcurementProductId,
                item.Product.NameZhTw,
                item.Product.NameThTh,
                item.Product.UnitCode,
                item.Batch.ProcurementStatus,
                item.Batch.LifecycleStatus,
                item.Batch.RowVersion,
                item.Batch.CreatedAt,
                item.Batch.DeletedAt))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > validated.Limit;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return new ProcurementBatchListPage(
            rows.Select(row => ToListItem(row, validated.Locale)).ToArray(),
            hasMore ? checked(validated.Offset + validated.Limit) : null);
    }

    public async ValueTask<ProcurementBatchWorkspace?> GetBatchAsync(
        Guid procurementBatchId,
        string locale,
        CancellationToken cancellationToken)
    {
        if (procurementBatchId == Guid.Empty)
        {
            throw new ArgumentException("Procurement Batch ID cannot be empty.", nameof(procurementBatchId));
        }

        var validatedLocale = ValidateLocale(locale);
        var header = await (
            from batch in dbContext.Set<ProcurementBatch>().AsNoTracking()
            join product in dbContext.Set<ProcurementProduct>().AsNoTracking()
                on batch.ProcurementProductId equals product.Id
            where batch.Id == procurementBatchId
            select new WorkspaceHeaderProjection(
                batch.Id,
                batch.ProcurementDate,
                batch.ProcurementProductId,
                product.NameZhTw,
                product.NameThTh,
                product.UnitCode,
                batch.ReceiptStorageLocationId,
                batch.ProcurementStatus,
                batch.LifecycleStatus,
                batch.RowVersion,
                batch.CreatedAt,
                batch.CompletedAt,
                batch.ClosedAt,
                batch.DeletedAt))
            .SingleOrDefaultAsync(cancellationToken);

        if (header is null)
        {
            return null;
        }

        ReceiptDestinationProjection? destination = null;
        if (header.ReceiptStorageLocationId is Guid receiptStorageLocationId)
        {
            destination = await (
                from location in dbContext.Set<StorageLocation>().AsNoTracking()
                join warehouse in dbContext.Set<Warehouse>().AsNoTracking()
                    on location.WarehouseId equals warehouse.Id
                where location.Id == receiptStorageLocationId
                select new ReceiptDestinationProjection(
                    location.Id,
                    warehouse.Id,
                    warehouse.NameZhTw,
                    warehouse.NameThTh))
                .SingleOrDefaultAsync(cancellationToken);
        }

        var entryRows = await dbContext.Set<ProcurementEntry>()
            .AsNoTracking()
            .Where(entry => entry.ProcurementBatchId == procurementBatchId)
            .OrderBy(entry => entry.RecordedAt)
            .ThenBy(entry => entry.Id)
            .Select(entry => new EntryProjection(
                entry.Id,
                entry.SourceType,
                entry.SupplierId,
                entry.FarmerId,
                entry.NetQuantity,
                entry.UnitCodeSnapshot,
                entry.UnitPrice,
                entry.AmountThb,
                entry.CompanyPickup,
                entry.RowVersion,
                entry.RecordedAt,
                entry.DeletedAt))
            .ToListAsync(cancellationToken);

        var supplierIds = entryRows
            .Where(entry => entry.SourceType == ProcurementSourceType.SUPPLIER && entry.SupplierId.HasValue)
            .Select(entry => entry.SupplierId!.Value)
            .Distinct()
            .ToArray();
        var farmerIds = entryRows
            .Where(entry => entry.SourceType == ProcurementSourceType.FARMER && entry.FarmerId.HasValue)
            .Select(entry => entry.FarmerId!.Value)
            .Distinct()
            .ToArray();

        var suppliers = supplierIds.Length == 0
            ? []
            : await dbContext.Set<Supplier>()
                .AsNoTracking()
                .Where(item => supplierIds.Contains(item.Id))
                .Select(item => new NameProjection(item.Id, item.Code, item.NameZhTw, item.NameThTh))
                .ToArrayAsync(cancellationToken);
        var farmers = farmerIds.Length == 0
            ? []
            : await dbContext.Set<Farmer>()
                .AsNoTracking()
                .Where(item => farmerIds.Contains(item.Id))
                .Select(item => new NameProjection(item.Id, item.Code, item.NameZhTw, item.NameThTh))
                .ToArrayAsync(cancellationToken);

        var supplierSources = suppliers.ToDictionary(item => item.Id, item => new SourceProjection(
            item.Code,
            DisplayName(item.NameZhTw, item.NameThTh, validatedLocale)));
        var farmerSources = farmers.ToDictionary(item => item.Id, item => new SourceProjection(
            item.Code,
            DisplayName(item.NameZhTw, item.NameThTh, validatedLocale)));

        var entries = entryRows
            .Select(entry => ToEntryItem(entry, supplierSources, farmerSources))
            .ToArray();

        return new ProcurementBatchWorkspace(
            header.Id,
            header.ProcurementDate,
            header.ProcurementProductId,
            DisplayName(header.ProductNameZhTw, header.ProductNameThTh, validatedLocale),
            header.UnitCode,
            header.ReceiptStorageLocationId,
            destination?.WarehouseId,
            destination is null
                ? null
                : DisplayName(destination.WarehouseNameZhTw, destination.WarehouseNameThTh, validatedLocale),
            header.ProcurementStatus.ToString(),
            header.LifecycleStatus.ToString(),
            header.RowVersion,
            header.CreatedAt,
            header.CompletedAt,
            header.ClosedAt,
            header.DeletedAt,
            entries);
    }

    private static ProcurementBatchListItem ToListItem(BatchProjection row, string locale) =>
        new(
            row.Id,
            row.ProcurementDate,
            row.ProcurementProductId,
            DisplayName(row.ProductNameZhTw, row.ProductNameThTh, locale),
            row.UnitCode,
            row.ProcurementStatus.ToString(),
            row.LifecycleStatus.ToString(),
            row.RowVersion,
            row.CreatedAt,
            row.DeletedAt);

    private static ProcurementBatchEntryItem ToEntryItem(
        EntryProjection entry,
        IReadOnlyDictionary<Guid, SourceProjection> supplierSources,
        IReadOnlyDictionary<Guid, SourceProjection> farmerSources)
    {
        var (sourceId, source) = entry.SourceType switch
        {
            ProcurementSourceType.SUPPLIER when entry.SupplierId is Guid supplierId
                && supplierSources.TryGetValue(supplierId, out var supplier) => (supplierId, supplier),
            ProcurementSourceType.FARMER when entry.FarmerId is Guid farmerId
                && farmerSources.TryGetValue(farmerId, out var farmer) => (farmerId, farmer),
            _ => throw new InvalidOperationException($"Procurement Entry {entry.Id} has an invalid or missing source reference."),
        };

        return new ProcurementBatchEntryItem(
            entry.Id,
            entry.SourceType.ToString(),
            sourceId,
            source.Code,
            source.DisplayName,
            entry.NetQuantity,
            entry.UnitCodeSnapshot,
            entry.UnitPrice,
            entry.AmountThb,
            entry.CompanyPickup,
            entry.RowVersion,
            entry.RecordedAt,
            entry.DeletedAt);
    }

    private static string DisplayName(string? nameZhTw, string? nameThTh, string locale) =>
        locale == LocaleZhTw
            ? nameZhTw ?? nameThTh ?? "—"
            : nameThTh ?? nameZhTw ?? "—";

    private static ValidatedQuery Validate(ProcurementBatchListQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var locale = ValidateLocale(query.Locale);
        if (query.Offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.Offset, "Query offset cannot be negative.");
        }
        if (query.Limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.Limit, "Query limit must be between 1 and 100.");
        }

        return new ValidatedQuery(
            locale,
            string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            query.Offset,
            query.Limit);
    }

    private static string ValidateLocale(string locale) => locale switch
    {
        LocaleZhTw => LocaleZhTw,
        LocaleThTh => LocaleThTh,
        _ => throw new ArgumentOutOfRangeException(nameof(locale), locale, "Unsupported operational locale."),
    };

    private sealed record ValidatedQuery(string Locale, string? Search, int Offset, int Limit);
    private sealed record NameProjection(Guid Id, string? Code, string? NameZhTw, string? NameThTh);
    private sealed record SourceProjection(string? Code, string DisplayName);
    private sealed record BatchProjection(
        Guid Id,
        DateOnly ProcurementDate,
        Guid ProcurementProductId,
        string? ProductNameZhTw,
        string? ProductNameThTh,
        string UnitCode,
        ProcurementStatus ProcurementStatus,
        ProcurementBatchLifecycleStatus LifecycleStatus,
        long RowVersion,
        DateTimeOffset CreatedAt,
        DateTimeOffset? DeletedAt);
    private sealed record WorkspaceHeaderProjection(
        Guid Id,
        DateOnly ProcurementDate,
        Guid ProcurementProductId,
        string? ProductNameZhTw,
        string? ProductNameThTh,
        string UnitCode,
        Guid? ReceiptStorageLocationId,
        ProcurementStatus ProcurementStatus,
        ProcurementBatchLifecycleStatus LifecycleStatus,
        long RowVersion,
        DateTimeOffset CreatedAt,
        DateTimeOffset? CompletedAt,
        DateTimeOffset? ClosedAt,
        DateTimeOffset? DeletedAt);
    private sealed record ReceiptDestinationProjection(
        Guid StorageLocationId,
        Guid WarehouseId,
        string? WarehouseNameZhTw,
        string? WarehouseNameThTh);
    private sealed record EntryProjection(
        Guid Id,
        ProcurementSourceType SourceType,
        Guid? SupplierId,
        Guid? FarmerId,
        decimal NetQuantity,
        string UnitCodeSnapshot,
        decimal UnitPrice,
        long AmountThb,
        bool CompanyPickup,
        long RowVersion,
        DateTimeOffset RecordedAt,
        DateTimeOffset? DeletedAt);
}
