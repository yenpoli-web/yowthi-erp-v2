using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Party;
using YowThi.Erp.Domain.Party;

namespace YowThi.Erp.Infrastructure.Persistence.Party;

internal sealed class EfCustomerMasterReader : ICustomerMasterReader
{
    private readonly ErpDbContext _dbContext;

    public EfCustomerMasterReader(ErpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<CustomerMasterPage> GetAsync(
        CustomerMasterQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<Customer> customers = _dbContext.Set<Customer>().AsNoTracking();
        customers = query.Status switch
        {
            CustomerMasterStatusFilter.Active => customers.Where(x => x.DeletedAt == null && x.Active),
            CustomerMasterStatusFilter.Inactive => customers.Where(x => x.DeletedAt == null && !x.Active),
            CustomerMasterStatusFilter.Deleted => customers.Where(x => x.DeletedAt != null),
            _ => customers,
        };

        var search = query.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            customers = customers.Where(x =>
                (x.NameZhTw != null && EF.Functions.ILike(x.NameZhTw, pattern))
                || (x.NameThTh != null && EF.Functions.ILike(x.NameThTh, pattern))
                || (x.Phone != null && EF.Functions.ILike(x.Phone, pattern)));
        }

        var rows = await customers
            .OrderBy(x => x.NameZhTw ?? x.NameThTh ?? string.Empty)
            .ThenBy(x => x.Id)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .Select(x => new CustomerMasterItem(
                x.Id,
                x.NameZhTw,
                x.NameThTh,
                x.Phone,
                x.Active,
                x.RowVersion,
                x.CreatedAt,
                x.DeletedAt))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return new CustomerMasterPage(
            rows,
            hasMore ? query.Offset + query.Limit : null);
    }
}
