using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Party;
using YowThi.Erp.Domain.Party;

namespace YowThi.Erp.Infrastructure.Persistence.Party;

internal sealed class EfOutsourcedVendorMasterReader : IOutsourcedVendorMasterReader
{
    private readonly ErpDbContext _dbContext;

    public EfOutsourcedVendorMasterReader(ErpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<OutsourcedVendorMasterPage> GetAsync(
        OutsourcedVendorMasterQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<OutsourcedVendor> outsourcedVendors = _dbContext.Set<OutsourcedVendor>().AsNoTracking();
        outsourcedVendors = query.Status switch
        {
            OutsourcedVendorMasterStatusFilter.Active => outsourcedVendors.Where(x => x.DeletedAt == null && x.Active),
            OutsourcedVendorMasterStatusFilter.Inactive => outsourcedVendors.Where(x => x.DeletedAt == null && !x.Active),
            OutsourcedVendorMasterStatusFilter.Deleted => outsourcedVendors.Where(x => x.DeletedAt != null),
            _ => outsourcedVendors,
        };

        var search = query.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            outsourcedVendors = outsourcedVendors.Where(x =>
                (x.NameZhTw != null && EF.Functions.ILike(x.NameZhTw, pattern))
                || (x.NameThTh != null && EF.Functions.ILike(x.NameThTh, pattern))
                || (x.Phone != null && EF.Functions.ILike(x.Phone, pattern))
                || (x.BankName != null && EF.Functions.ILike(x.BankName, pattern)));
        }

        var rows = await outsourcedVendors
            .OrderBy(x => x.NameZhTw ?? x.NameThTh ?? string.Empty)
            .ThenBy(x => x.Id)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .Select(x => new OutsourcedVendorMasterItem(
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

        return new OutsourcedVendorMasterPage(
            rows,
            hasMore ? query.Offset + query.Limit : null);
    }
}
