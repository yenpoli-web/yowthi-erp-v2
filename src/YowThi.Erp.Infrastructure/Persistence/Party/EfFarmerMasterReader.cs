using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Party;
using YowThi.Erp.Domain.Party;

namespace YowThi.Erp.Infrastructure.Persistence.Party;

internal sealed class EfFarmerMasterReader : IFarmerMasterReader
{
    private readonly ErpDbContext _dbContext;

    public EfFarmerMasterReader(ErpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<FarmerMasterPage> GetAsync(
        FarmerMasterQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<Farmer> farmers = _dbContext.Set<Farmer>().AsNoTracking();
        farmers = query.Status switch
        {
            FarmerMasterStatusFilter.Active => farmers.Where(x => x.DeletedAt == null && x.Active),
            FarmerMasterStatusFilter.Inactive => farmers.Where(x => x.DeletedAt == null && !x.Active),
            FarmerMasterStatusFilter.Deleted => farmers.Where(x => x.DeletedAt != null),
            _ => farmers,
        };

        var search = query.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            farmers = farmers.Where(x =>
                (x.NameZhTw != null && EF.Functions.ILike(x.NameZhTw, pattern))
                || (x.NameThTh != null && EF.Functions.ILike(x.NameThTh, pattern))
                || (x.Phone != null && EF.Functions.ILike(x.Phone, pattern))
                || (x.BankName != null && EF.Functions.ILike(x.BankName, pattern)));
        }

        var rows = await farmers
            .OrderBy(x => x.NameZhTw ?? x.NameThTh ?? string.Empty)
            .ThenBy(x => x.Id)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .Select(x => new FarmerMasterItem(
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

        return new FarmerMasterPage(
            rows,
            hasMore ? query.Offset + query.Limit : null);
    }
}
