using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Outsourced;
using YowThi.Erp.Domain.Infrastructure;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Infrastructure.Persistence.Outsourced;

internal sealed class EfOutsourcedSupplyDetailOptionsReader(ErpDbContext dbContext) : IOutsourcedSupplyDetailOptionsReader
{
    private const string LocaleZhTw = "zh-TW";
    private const string LocaleThTh = "th-TH";

    public async ValueTask<OutsourcedSupplyDetailOptionPage<OutsourcedVendorOption>> GetVendorsAsync(
        OutsourcedSupplyDetailOptionsQuery query,
        CancellationToken cancellationToken)
    {
        var validated = Validate(query);
        var preferZhTw = validated.Locale == LocaleZhTw;

        var vendors = dbContext.Set<OutsourcedVendor>()
            .AsNoTracking()
            .Where(vendor => vendor.Active && vendor.DeletedAt == null);

        if (validated.Search is not null)
        {
            var search = validated.Search;
            vendors = vendors.Where(vendor =>
                (vendor.NameZhTw != null && vendor.NameZhTw.Contains(search))
                || (vendor.NameThTh != null && vendor.NameThTh.Contains(search)));
        }

        var ordered = preferZhTw
            ? vendors
                .OrderBy(vendor => vendor.NameZhTw ?? vendor.NameThTh)
                .ThenBy(vendor => vendor.Id)
                .Select(vendor => new OutsourcedVendorOption(
                    vendor.Id,
                    vendor.NameZhTw ?? vendor.NameThTh!))
            : vendors
                .OrderBy(vendor => vendor.NameThTh ?? vendor.NameZhTw)
                .ThenBy(vendor => vendor.Id)
                .Select(vendor => new OutsourcedVendorOption(
                    vendor.Id,
                    vendor.NameThTh ?? vendor.NameZhTw!));

        return await MaterializePageAsync(ordered, validated, cancellationToken);
    }

    public async ValueTask<OutsourcedSupplyDetailOptionPage<OutsourcedSalesProductOption>> GetProductsAsync(
        OutsourcedSupplyDetailOptionsQuery query,
        CancellationToken cancellationToken)
    {
        var validated = Validate(query);
        var preferZhTw = validated.Locale == LocaleZhTw;

        var products = dbContext.Set<SalesProduct>()
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
                .Select(product => new OutsourcedSalesProductOption(
                    product.Id,
                    product.NameZhTw ?? product.NameThTh!,
                    product.PricingBasis))
            : products
                .OrderBy(product => product.NameThTh ?? product.NameZhTw)
                .ThenBy(product => product.Id)
                .Select(product => new OutsourcedSalesProductOption(
                    product.Id,
                    product.NameThTh ?? product.NameZhTw!,
                    product.PricingBasis));

        return await MaterializePageAsync(ordered, validated, cancellationToken);
    }

    public async ValueTask<OutsourcedReceiptStorageLocationOptions?> GetReceiptStorageLocationsAsync(
        Guid salesProductId,
        OutsourcedSupplyDetailOptionsQuery query,
        CancellationToken cancellationToken)
    {
        if (salesProductId == Guid.Empty)
        {
            throw new ArgumentException("Sales Product ID cannot be empty.", nameof(salesProductId));
        }

        var validated = Validate(query);
        var preferZhTw = validated.Locale == LocaleZhTw;

        var product = await dbContext.Set<SalesProduct>()
            .AsNoTracking()
            .Where(candidate =>
                candidate.Id == salesProductId
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
                .Select(location => new OutsourcedReceiptStorageLocationOption(
                    location.Id,
                    location.NameZhTw ?? location.NameThTh!,
                    location.Code,
                    location.WarehouseId,
                    location.Id == applicableDefaultStorageLocationId))
            : locations
                .OrderByDescending(location => location.Id == applicableDefaultStorageLocationId)
                .ThenBy(location => location.NameThTh ?? location.NameZhTw)
                .ThenBy(location => location.Id)
                .Select(location => new OutsourcedReceiptStorageLocationOption(
                    location.Id,
                    location.NameThTh ?? location.NameZhTw!,
                    location.Code,
                    location.WarehouseId,
                    location.Id == applicableDefaultStorageLocationId));

        var page = await MaterializePageAsync(ordered, validated, cancellationToken);
        return new OutsourcedReceiptStorageLocationOptions(
            product.Id,
            applicableDefaultStorageLocationId,
            page);
    }

    private static async ValueTask<OutsourcedSupplyDetailOptionPage<T>> MaterializePageAsync<T>(
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

        return new OutsourcedSupplyDetailOptionPage<T>(
            rows,
            hasMore ? checked(query.Offset + query.Limit) : null);
    }

    private static ValidatedQuery Validate(OutsourcedSupplyDetailOptionsQuery query)
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
