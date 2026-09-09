using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Sales;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.Product;
using YowThi.Erp.Domain.Sales;

namespace YowThi.Erp.Infrastructure.Persistence.Sales;

internal sealed class EfSalesWorkspaceReader(ErpDbContext dbContext) : ISalesWorkspaceReader
{
    private const string LocaleZhTw = "zh-TW";
    private const string LocaleThTh = "th-TH";

    public async ValueTask<SalesWorkspaceListPage> GetSalesAsync(
        SalesWorkspaceListQuery query,
        CancellationToken cancellationToken)
    {
        var locale = ValidateLocale(query.Locale);
        if (query.Offset < 0 || query.Limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(query));

        var sales =
            from sale in dbContext.Set<Sale>().AsNoTracking()
            join customer in dbContext.Set<Customer>().AsNoTracking() on sale.CustomerId equals customer.Id
            select new
            {
                sale.Id,
                sale.SalesDate,
                sale.CustomerId,
                sale.Status,
                sale.RowVersion,
                sale.CreatedAt,
                sale.ConfirmedAt,
                sale.DeletedAt,
                CustomerNameZhTw = customer.NameZhTw,
                CustomerNameThTh = customer.NameThTh,
            };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            sales = sales.Where(item =>
                (item.CustomerNameZhTw != null && item.CustomerNameZhTw.Contains(search))
                || (item.CustomerNameThTh != null && item.CustomerNameThTh.Contains(search)));
        }

        var rows = await sales
            .OrderByDescending(item => item.SalesDate)
            .ThenBy(item => item.Id)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        var items = rows.Take(query.Limit)
            .Select(item => new SalesWorkspaceListItem(
                item.Id,
                item.SalesDate,
                item.CustomerId,
                DisplayName(item.CustomerNameZhTw, item.CustomerNameThTh, locale, item.CustomerId),
                item.Status.ToString(),
                item.RowVersion,
                item.CreatedAt,
                item.ConfirmedAt,
                item.DeletedAt))
            .ToArray();

        return new SalesWorkspaceListPage(items, hasMore ? query.Offset + query.Limit : null);
    }

    public async ValueTask<SalesWorkspace?> GetSaleAsync(
        Guid salesId,
        string locale,
        CancellationToken cancellationToken)
    {
        if (salesId == Guid.Empty) throw new ArgumentException("Sales ID cannot be empty.", nameof(salesId));
        locale = ValidateLocale(locale);

        var sale = await (
            from candidate in dbContext.Set<Sale>().AsNoTracking()
            join customer in dbContext.Set<Customer>().AsNoTracking() on candidate.CustomerId equals customer.Id
            where candidate.Id == salesId
            select new
            {
                candidate.Id,
                candidate.SalesDate,
                candidate.CustomerId,
                candidate.Status,
                candidate.RowVersion,
                candidate.CreatedAt,
                candidate.ConfirmedAt,
                candidate.DeletedAt,
                CustomerNameZhTw = customer.NameZhTw,
                CustomerNameThTh = customer.NameThTh,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (sale is null) return null;

        var details = await (
            from detail in dbContext.Set<SalesDetail>().AsNoTracking()
            join product in dbContext.Set<SalesProduct>().AsNoTracking() on detail.SalesProductId equals product.Id
            where detail.SalesId == salesId
            orderby detail.LineNumber, detail.Id
            select new
            {
                detail.Id,
                detail.LineNumber,
                detail.SalesProductId,
                detail.Quantity,
                detail.PricingBasisSnapshot,
                detail.SalesWeightSnapshot,
                detail.UnitPrice,
                detail.AmountThb,
                detail.RowVersion,
                detail.CreatedAt,
                detail.DeletedAt,
                ProductNameZhTw = product.NameZhTw,
                ProductNameThTh = product.NameThTh,
            })
            .ToListAsync(cancellationToken);

        return new SalesWorkspace(
            sale.Id,
            sale.SalesDate,
            sale.CustomerId,
            DisplayName(sale.CustomerNameZhTw, sale.CustomerNameThTh, locale, sale.CustomerId),
            sale.Status.ToString(),
            sale.RowVersion,
            sale.CreatedAt,
            sale.ConfirmedAt,
            sale.DeletedAt,
            details.Select(detail => new SalesWorkspaceDetailItem(
                detail.Id,
                detail.LineNumber,
                detail.SalesProductId,
                DisplayName(detail.ProductNameZhTw, detail.ProductNameThTh, locale, detail.SalesProductId),
                detail.Quantity,
                detail.PricingBasisSnapshot.ToString(),
                detail.SalesWeightSnapshot,
                detail.UnitPrice,
                detail.AmountThb,
                detail.RowVersion,
                detail.CreatedAt,
                detail.DeletedAt)).ToArray());
    }

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
