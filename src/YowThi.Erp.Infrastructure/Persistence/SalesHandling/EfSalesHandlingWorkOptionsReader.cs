using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.SalesHandling;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.Sales;
using YowThi.Erp.Domain.SalesHandling;

namespace YowThi.Erp.Infrastructure.Persistence.SalesHandling;

internal sealed class EfSalesHandlingWorkOptionsReader(ErpDbContext dbContext) : ISalesHandlingWorkOptionsReader
{
    public async ValueTask<SalesHandlingWorkOptionPage<SalesHandlingSaleOption>> GetSalesAsync(
        SalesHandlingWorkOptionsQuery query,
        CancellationToken cancellationToken)
    {
        var source =
            from sale in dbContext.Set<Sale>().AsNoTracking()
            join customer in dbContext.Set<Customer>().AsNoTracking() on sale.CustomerId equals customer.Id
            where sale.DeletedAt == null
                && (sale.Status == SalesStatus.DRAFT || sale.Status == SalesStatus.CONFIRMED)
            select new
            {
                sale.Id,
                sale.SalesDate,
                sale.Status,
                CustomerId = customer.Id,
                CustomerName = query.Locale == "th-TH"
                    ? customer.NameThTh ?? customer.NameZhTw
                    : customer.NameZhTw ?? customer.NameThTh,
            };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(item => item.CustomerName != null && item.CustomerName.Contains(search));
        }

        var rows = await source
            .OrderByDescending(item => item.SalesDate)
            .ThenBy(item => item.Id)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        var items = rows
            .Take(query.Limit)
            .Select(item => new SalesHandlingSaleOption(
                item.Id,
                item.SalesDate,
                string.IsNullOrWhiteSpace(item.CustomerName) ? item.CustomerId.ToString() : item.CustomerName,
                item.Status.ToString()))
            .ToArray();

        return new SalesHandlingWorkOptionPage<SalesHandlingSaleOption>(
            items,
            hasMore ? query.Offset + query.Limit : null);
    }

    public async ValueTask<SalesHandlingWorkOptionPage<SalesHandlingEmployeeOption>> GetEmployeesAsync(
        SalesHandlingWorkOptionsQuery query,
        CancellationToken cancellationToken)
    {
        var source = dbContext.Set<Employee>()
            .AsNoTracking()
            .Where(employee => employee.Active && employee.DeletedAt == null)
            .Select(employee => new
            {
                employee.Id,
                DisplayName = query.Locale == "th-TH"
                    ? employee.NameThTh ?? employee.NameZhTw
                    : employee.NameZhTw ?? employee.NameThTh,
            });

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(item => item.DisplayName != null && item.DisplayName.Contains(search));
        }

        var rows = await source
            .OrderBy(item => item.DisplayName)
            .ThenBy(item => item.Id)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        var items = rows
            .Take(query.Limit)
            .Select(item => new SalesHandlingEmployeeOption(
                item.Id,
                string.IsNullOrWhiteSpace(item.DisplayName) ? item.Id.ToString() : item.DisplayName))
            .ToArray();

        return new SalesHandlingWorkOptionPage<SalesHandlingEmployeeOption>(
            items,
            hasMore ? query.Offset + query.Limit : null);
    }

    public async ValueTask<SalesHandlingWorkOptionPage<SalesHandlingPackagingItemOption>> GetPackagingItemsAsync(
        SalesHandlingWorkOptionsQuery query,
        CancellationToken cancellationToken)
    {
        var source = dbContext.Set<SalesPackagingItem>()
            .AsNoTracking()
            .Where(item => item.Active && item.DeletedAt == null)
            .Select(item => new
            {
                item.Id,
                DisplayName = query.Locale == "th-TH"
                    ? item.NameThTh ?? item.NameZhTw
                    : item.NameZhTw ?? item.NameThTh,
            });

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(item => item.DisplayName != null && item.DisplayName.Contains(search));
        }

        var rows = await source
            .OrderBy(item => item.DisplayName)
            .ThenBy(item => item.Id)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        var items = rows
            .Take(query.Limit)
            .Select(item => new SalesHandlingPackagingItemOption(
                item.Id,
                string.IsNullOrWhiteSpace(item.DisplayName) ? item.Id.ToString() : item.DisplayName))
            .ToArray();

        return new SalesHandlingWorkOptionPage<SalesHandlingPackagingItemOption>(
            items,
            hasMore ? query.Offset + query.Limit : null);
    }
}
