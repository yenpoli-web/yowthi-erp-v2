using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Party;
using YowThi.Erp.Domain.Party;

namespace YowThi.Erp.Infrastructure.Persistence.Party;

internal sealed class EfSupplierMasterReader : ISupplierMasterReader
{
    private readonly ErpDbContext _dbContext;

    public EfSupplierMasterReader(ErpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<SupplierMasterPage> GetAsync(
        SupplierMasterQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<Supplier> suppliers = _dbContext.Set<Supplier>().AsNoTracking();
        suppliers = query.Status switch
        {
            SupplierMasterStatusFilter.Active => suppliers.Where(x => x.DeletedAt == null && x.Active),
            SupplierMasterStatusFilter.Inactive => suppliers.Where(x => x.DeletedAt == null && !x.Active),
            SupplierMasterStatusFilter.Deleted => suppliers.Where(x => x.DeletedAt != null),
            _ => suppliers,
        };

        var search = query.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            suppliers = suppliers.Where(x =>
                (x.Code != null && EF.Functions.ILike(x.Code, pattern))
                || (x.NameZhTw != null && EF.Functions.ILike(x.NameZhTw, pattern))
                || (x.NameThTh != null && EF.Functions.ILike(x.NameThTh, pattern))
                || (x.Phone != null && EF.Functions.ILike(x.Phone, pattern))
                || (x.BankName != null && EF.Functions.ILike(x.BankName, pattern)));
        }

        var rows = await suppliers
            .OrderBy(x => x.NameZhTw ?? x.NameThTh ?? string.Empty)
            .ThenBy(x => x.Id)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .Select(x => new SupplierMasterItem(
                x.Id,
                x.Code,
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

        return new SupplierMasterPage(
            rows,
            hasMore ? query.Offset + query.Limit : null);
    }
}
