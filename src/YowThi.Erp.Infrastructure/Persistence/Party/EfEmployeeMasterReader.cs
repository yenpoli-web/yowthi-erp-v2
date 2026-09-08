using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Party;
using YowThi.Erp.Domain.Party;

namespace YowThi.Erp.Infrastructure.Persistence.Party;

internal sealed class EfEmployeeMasterReader : IEmployeeMasterReader
{
    private readonly ErpDbContext _dbContext;

    public EfEmployeeMasterReader(ErpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<EmployeeMasterPage> GetAsync(
        EmployeeMasterQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<Employee> employees = _dbContext.Set<Employee>().AsNoTracking();
        employees = query.Status switch
        {
            EmployeeMasterStatusFilter.Active => employees.Where(x => x.DeletedAt == null && x.Active),
            EmployeeMasterStatusFilter.Inactive => employees.Where(x => x.DeletedAt == null && !x.Active),
            EmployeeMasterStatusFilter.Deleted => employees.Where(x => x.DeletedAt != null),
            _ => employees,
        };

        var search = query.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            employees = employees.Where(x =>
                (x.NameZhTw != null && EF.Functions.ILike(x.NameZhTw, pattern))
                || (x.NameThTh != null && EF.Functions.ILike(x.NameThTh, pattern))
                || (x.Phone != null && EF.Functions.ILike(x.Phone, pattern))
                || (x.BankName != null && EF.Functions.ILike(x.BankName, pattern)));
        }

        var rows = await employees
            .OrderBy(x => x.NameZhTw ?? x.NameThTh ?? string.Empty)
            .ThenBy(x => x.Id)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .Select(x => new EmployeeMasterItem(
                x.Id,
                x.NameZhTw,
                x.NameThTh,
                x.BankName,
                x.BankAccount,
                x.Phone,
                x.Address,
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

        return new EmployeeMasterPage(
            rows,
            hasMore ? query.Offset + query.Limit : null);
    }
}
