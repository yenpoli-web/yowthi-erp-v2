using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Product;
using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Infrastructure.Persistence.Product;

internal sealed class EfSalesProductMasterOptionsReader : ISalesProductMasterOptionsReader
{
    private readonly ErpDbContext _dbContext;

    public EfSalesProductMasterOptionsReader(ErpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<SalesProductMasterGroupOptions> GetGroupsAsync(
        string locale,
        string? search,
        int limit,
        CancellationToken cancellationToken)
    {
        var source = _dbContext.Set<SalesProductGroup>()
            .AsNoTracking()
            .Where(x => x.Active && x.DeletedAt == null);

        var normalized = search?.Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            var pattern = $"%{normalized}%";
            source = source.Where(x =>
                (x.NameZhTw != null && EF.Functions.ILike(x.NameZhTw, pattern))
                || (x.NameThTh != null && EF.Functions.ILike(x.NameThTh, pattern)));
        }

        var rows = await source
            .OrderBy(x => locale == "th-TH" ? x.NameThTh ?? x.NameZhTw : x.NameZhTw ?? x.NameThTh)
            .ThenBy(x => x.Id)
            .Take(limit)
            .Select(x => new SalesProductMasterGroupOption(
                x.Id,
                locale == "th-TH"
                    ? x.NameThTh ?? x.NameZhTw ?? x.Id.ToString()
                    : x.NameZhTw ?? x.NameThTh ?? x.Id.ToString()))
            .ToListAsync(cancellationToken);

        return new SalesProductMasterGroupOptions(rows);
    }
}
