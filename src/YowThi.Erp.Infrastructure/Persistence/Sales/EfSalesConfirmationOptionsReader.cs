using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Sales;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.Product;
using YowThi.Erp.Domain.Sales;

namespace YowThi.Erp.Infrastructure.Persistence.Sales;

internal sealed class EfSalesConfirmationOptionsReader(ErpDbContext dbContext) : ISalesConfirmationOptionsReader
{
    public async ValueTask<SalesConfirmationOptionPage<SalesConfirmationSaleOption>> GetSalesAsync(
        SalesConfirmationOptionsQuery query,
        CancellationToken cancellationToken)
    {
        var sales =
            from sale in dbContext.Set<Sale>().AsNoTracking()
            join customer in dbContext.Set<Customer>().AsNoTracking() on sale.CustomerId equals customer.Id
            where sale.DeletedAt == null
                && sale.Status == SalesStatus.DRAFT
                && sale.ConfirmedAt == null
                && sale.ConfirmedByAccountId == null
            select new
            {
                sale.Id,
                sale.SalesDate,
                sale.RowVersion,
                CustomerId = customer.Id,
                CustomerName = query.Locale == "th-TH"
                    ? customer.NameThTh ?? customer.NameZhTw
                    : customer.NameZhTw ?? customer.NameThTh,
            };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            sales = sales.Where(item => item.CustomerName != null && item.CustomerName.Contains(search));
        }

        var rows = await sales
            .OrderByDescending(item => item.SalesDate)
            .ThenBy(item => item.Id)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        var items = rows
            .Take(query.Limit)
            .Select(item => new SalesConfirmationSaleOption(
                item.Id,
                item.SalesDate,
                string.IsNullOrWhiteSpace(item.CustomerName) ? item.CustomerId.ToString() : item.CustomerName,
                item.RowVersion))
            .ToArray();

        return new SalesConfirmationOptionPage<SalesConfirmationSaleOption>(
            items,
            hasMore ? query.Offset + query.Limit : null);
    }

    public async ValueTask<SalesConfirmationWorkspace?> GetWorkspaceAsync(
        Guid salesId,
        string locale,
        CancellationToken cancellationToken)
    {
        var sale = await (
            from candidate in dbContext.Set<Sale>().AsNoTracking()
            join customer in dbContext.Set<Customer>().AsNoTracking() on candidate.CustomerId equals customer.Id
            where candidate.Id == salesId
                && candidate.DeletedAt == null
                && candidate.Status == SalesStatus.DRAFT
                && candidate.ConfirmedAt == null
                && candidate.ConfirmedByAccountId == null
            select new
            {
                candidate.Id,
                candidate.SalesDate,
                candidate.CustomerId,
                candidate.RowVersion,
                CustomerName = locale == "th-TH"
                    ? customer.NameThTh ?? customer.NameZhTw
                    : customer.NameZhTw ?? customer.NameThTh,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (sale is null)
        {
            return null;
        }

        var detailRows = await (
            from detail in dbContext.Set<SalesDetail>().AsNoTracking()
            join product in dbContext.Set<SalesProduct>().AsNoTracking() on detail.SalesProductId equals product.Id
            where detail.SalesId == salesId && detail.DeletedAt == null
            orderby detail.LineNumber
            select new
            {
                detail.Id,
                detail.LineNumber,
                detail.SalesProductId,
                ProductName = locale == "th-TH"
                    ? product.NameThTh ?? product.NameZhTw
                    : product.NameZhTw ?? product.NameThTh,
                detail.Quantity,
                detail.PricingBasisSnapshot,
                detail.SalesWeightSnapshot,
                detail.UnitPrice,
                detail.AmountThb,
            })
            .ToListAsync(cancellationToken);

        var details = detailRows
            .Select(detail => new SalesConfirmationDetailOption(
                detail.Id,
                detail.LineNumber,
                detail.SalesProductId,
                string.IsNullOrWhiteSpace(detail.ProductName) ? detail.SalesProductId.ToString() : detail.ProductName,
                detail.Quantity,
                detail.PricingBasisSnapshot.ToString(),
                detail.SalesWeightSnapshot,
                detail.UnitPrice,
                detail.AmountThb))
            .ToArray();

        return new SalesConfirmationWorkspace(
            sale.Id,
            sale.SalesDate,
            sale.CustomerId,
            string.IsNullOrWhiteSpace(sale.CustomerName) ? sale.CustomerId.ToString() : sale.CustomerName,
            sale.RowVersion,
            details);
    }
}
