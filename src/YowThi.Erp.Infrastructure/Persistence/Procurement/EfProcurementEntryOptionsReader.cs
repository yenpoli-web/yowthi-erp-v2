using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Procurement;
using YowThi.Erp.Domain.Infrastructure;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.Procurement;
using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Infrastructure.Persistence.Procurement;

internal sealed class EfProcurementEntryOptionsReader(ErpDbContext dbContext) : IProcurementEntryOptionsReader
{
    private const string LocaleZhTw = "zh-TW";
    private const string LocaleThTh = "th-TH";

    public async ValueTask<ProcurementEntryOptionPage<ProcurementProductOption>> GetProductsAsync(
        ProcurementEntryOptionsQuery query,
        CancellationToken cancellationToken)
    {
        var validated = Validate(query);
        var preferZhTw = validated.Locale == LocaleZhTw;

        var products = dbContext.Set<ProcurementProduct>()
            .AsNoTracking()
            .Where(product => product.Active && product.DeletedAt == null);

        if (validated.Search is not null)
        {
            var search = validated.Search;
            products = products.Where(product =>
                (product.NameZhTw != null && product.NameZhTw.Contains(search))
                || (product.NameThTh != null && product.NameThTh.Contains(search)));
        }

        var ordered = preferZhTw
            ? products
                .OrderBy(product => product.NameZhTw ?? product.NameThTh)
                .ThenBy(product => product.Id)
                .Select(product => new ProcurementProductOption(
                    product.Id,
                    product.NameZhTw ?? product.NameThTh!,
                    product.UnitCode))
            : products
                .OrderBy(product => product.NameThTh ?? product.NameZhTw)
                .ThenBy(product => product.Id)
                .Select(product => new ProcurementProductOption(
                    product.Id,
                    product.NameThTh ?? product.NameZhTw!,
                    product.UnitCode));

        return await MaterializePageAsync(ordered, validated, cancellationToken);
    }

    public async ValueTask<ProcurementEntryOptionPage<ProcurementSourceOption>> GetSourcesAsync(
        ProcurementSourceType sourceType,
        ProcurementEntryOptionsQuery query,
        CancellationToken cancellationToken)
    {
        var validated = Validate(query);

        return sourceType switch
        {
            ProcurementSourceType.SUPPLIER => await GetSupplierOptionsAsync(validated, cancellationToken),
            ProcurementSourceType.FARMER => await GetFarmerOptionsAsync(validated, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(sourceType), sourceType, "Unsupported Procurement source type."),
        };
    }

    public async ValueTask<ProcurementReceiptStorageLocationOptions?> GetReceiptStorageLocationsAsync(
        Guid procurementProductId,
        ProcurementEntryOptionsQuery query,
        CancellationToken cancellationToken)
    {
        if (procurementProductId == Guid.Empty)
        {
            throw new ArgumentException("Procurement Product ID cannot be empty.", nameof(procurementProductId));
        }

        var validated = Validate(query);
        var preferZhTw = validated.Locale == LocaleZhTw;

        var product = await dbContext.Set<ProcurementProduct>()
            .AsNoTracking()
            .Where(candidate =>
                candidate.Id == procurementProductId
                && candidate.Active
                && candidate.DeletedAt == null)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.DefaultStorageLocationId,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (product is null)
        {
            return null;
        }

        Guid? applicableDefaultStorageLocationId = null;
        if (product.DefaultStorageLocationId is Guid defaultStorageLocationId)
        {
            var defaultIsApplicable = await dbContext.Set<StorageLocation>()
                .AsNoTracking()
                .AnyAsync(location =>
                    location.Id == defaultStorageLocationId
                    && location.Active
                    && location.DeletedAt == null,
                    cancellationToken);

            if (defaultIsApplicable)
            {
                applicableDefaultStorageLocationId = defaultStorageLocationId;
            }
        }

        var locations = dbContext.Set<StorageLocation>()
            .AsNoTracking()
            .Where(location => location.Active && location.DeletedAt == null);

        if (validated.Search is not null)
        {
            var search = validated.Search;
            locations = locations.Where(location =>
                (location.NameZhTw != null && location.NameZhTw.Contains(search))
                || (location.NameThTh != null && location.NameThTh.Contains(search))
                || (location.Code != null && location.Code.Contains(search)));
        }

        var ordered = preferZhTw
            ? locations
                .OrderByDescending(location => location.Id == applicableDefaultStorageLocationId)
                .ThenBy(location => location.NameZhTw ?? location.NameThTh)
                .ThenBy(location => location.Id)
                .Select(location => new ProcurementReceiptStorageLocationOption(
                    location.Id,
                    location.NameZhTw ?? location.NameThTh!,
                    location.Code,
                    location.WarehouseId,
                    location.Id == applicableDefaultStorageLocationId))
            : locations
                .OrderByDescending(location => location.Id == applicableDefaultStorageLocationId)
                .ThenBy(location => location.NameThTh ?? location.NameZhTw)
                .ThenBy(location => location.Id)
                .Select(location => new ProcurementReceiptStorageLocationOption(
                    location.Id,
                    location.NameThTh ?? location.NameZhTw!,
                    location.Code,
                    location.WarehouseId,
                    location.Id == applicableDefaultStorageLocationId));

        var page = await MaterializePageAsync(ordered, validated, cancellationToken);
        return new ProcurementReceiptStorageLocationOptions(
            product.Id,
            applicableDefaultStorageLocationId,
            page);
    }

    private async ValueTask<ProcurementEntryOptionPage<ProcurementSourceOption>> GetSupplierOptionsAsync(
        ValidatedQuery query,
        CancellationToken cancellationToken)
    {
        var preferZhTw = query.Locale == LocaleZhTw;
        var suppliers = dbContext.Set<Supplier>()
            .AsNoTracking()
            .Where(supplier => supplier.Active && supplier.DeletedAt == null);

        if (query.Search is not null)
        {
            var search = query.Search;
            suppliers = suppliers.Where(supplier =>
                (supplier.NameZhTw != null && supplier.NameZhTw.Contains(search))
                || (supplier.NameThTh != null && supplier.NameThTh.Contains(search)));
        }

        var ordered = preferZhTw
            ? suppliers
                .OrderBy(supplier => supplier.NameZhTw ?? supplier.NameThTh)
                .ThenBy(supplier => supplier.Id)
                .Select(supplier => new ProcurementSourceOption(
                    supplier.Id,
                    supplier.NameZhTw ?? supplier.NameThTh!))
            : suppliers
                .OrderBy(supplier => supplier.NameThTh ?? supplier.NameZhTw)
                .ThenBy(supplier => supplier.Id)
                .Select(supplier => new ProcurementSourceOption(
                    supplier.Id,
                    supplier.NameThTh ?? supplier.NameZhTw!));

        return await MaterializePageAsync(ordered, query, cancellationToken);
    }

    private async ValueTask<ProcurementEntryOptionPage<ProcurementSourceOption>> GetFarmerOptionsAsync(
        ValidatedQuery query,
        CancellationToken cancellationToken)
    {
        var preferZhTw = query.Locale == LocaleZhTw;
        var farmers = dbContext.Set<Farmer>()
            .AsNoTracking()
            .Where(farmer => farmer.Active && farmer.DeletedAt == null);

        if (query.Search is not null)
        {
            var search = query.Search;
            farmers = farmers.Where(farmer =>
                (farmer.NameZhTw != null && farmer.NameZhTw.Contains(search))
                || (farmer.NameThTh != null && farmer.NameThTh.Contains(search)));
        }

        var ordered = preferZhTw
            ? farmers
                .OrderBy(farmer => farmer.NameZhTw ?? farmer.NameThTh)
                .ThenBy(farmer => farmer.Id)
                .Select(farmer => new ProcurementSourceOption(
                    farmer.Id,
                    farmer.NameZhTw ?? farmer.NameThTh!))
            : farmers
                .OrderBy(farmer => farmer.NameThTh ?? farmer.NameZhTw)
                .ThenBy(farmer => farmer.Id)
                .Select(farmer => new ProcurementSourceOption(
                    farmer.Id,
                    farmer.NameThTh ?? farmer.NameZhTw!));

        return await MaterializePageAsync(ordered, query, cancellationToken);
    }

    private static async ValueTask<ProcurementEntryOptionPage<T>> MaterializePageAsync<T>(
        IQueryable<T> orderedQuery,
        ValidatedQuery query,
        CancellationToken cancellationToken)
    {
        var rows = await orderedQuery
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return new ProcurementEntryOptionPage<T>(
            rows,
            hasMore ? checked(query.Offset + query.Limit) : null);
    }

    private static ValidatedQuery Validate(ProcurementEntryOptionsQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Locale is not (LocaleZhTw or LocaleThTh))
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.Locale, "Unsupported operational locale.");
        }

        if (query.Offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.Offset, "Query offset cannot be negative.");
        }

        if (query.Limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.Limit, "Query limit must be between 1 and 100.");
        }

        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        return new ValidatedQuery(query.Locale, search, query.Offset, query.Limit);
    }

    private sealed record ValidatedQuery(
        string Locale,
        string? Search,
        int Offset,
        int Limit);
}
