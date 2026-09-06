using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Infrastructure;
using YowThi.Erp.Domain.Infrastructure;

namespace YowThi.Erp.Infrastructure.Persistence.Infrastructure;

internal sealed class EfInfrastructureLifecycleOptionsReader(ErpDbContext dbContext) : IInfrastructureLifecycleOptionsReader
{
    public async ValueTask<InfrastructureLifecycleOptionPage<ContainerLifecycleOption>> GetContainersAsync(
        InfrastructureLifecycleOptionsQuery query,
        CancellationToken cancellationToken)
    {
        var source = dbContext.Set<Container>()
            .AsNoTracking()
            .Select(item => new
            {
                item.Id,
                Name = query.Locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh,
                item.TareWeight,
                item.Active,
                item.RowVersion,
                item.DeletedAt,
            });

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(item => item.Name != null && item.Name.Contains(search));
        }

        var rows = await source
            .OrderBy(item => item.DeletedAt != null)
            .ThenByDescending(item => item.Active)
            .ThenBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        var items = rows.Take(query.Limit)
            .Select(item => new ContainerLifecycleOption(
                item.Id,
                string.IsNullOrWhiteSpace(item.Name) ? item.Id.ToString() : item.Name,
                item.TareWeight,
                item.Active,
                item.RowVersion,
                item.DeletedAt is not null,
                item.DeletedAt))
            .ToArray();

        return new InfrastructureLifecycleOptionPage<ContainerLifecycleOption>(
            items,
            hasMore ? query.Offset + query.Limit : null);
    }

    public async ValueTask<InfrastructureLifecycleOptionPage<WarehouseLifecycleOption>> GetWarehousesAsync(
        InfrastructureLifecycleOptionsQuery query,
        CancellationToken cancellationToken)
    {
        var source = dbContext.Set<Warehouse>()
            .AsNoTracking()
            .Select(item => new
            {
                item.Id,
                Name = query.Locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh,
                item.Code,
                item.Active,
                item.RowVersion,
                item.DeletedAt,
            });

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(item =>
                (item.Name != null && item.Name.Contains(search))
                || (item.Code != null && item.Code.Contains(search)));
        }

        var rows = await source
            .OrderBy(item => item.DeletedAt != null)
            .ThenByDescending(item => item.Active)
            .ThenBy(item => item.Code)
            .ThenBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        var items = rows.Take(query.Limit)
            .Select(item => new WarehouseLifecycleOption(
                item.Id,
                DisplayWarehouse(item.Code, item.Name, item.Id),
                item.Code,
                item.Active,
                item.RowVersion,
                item.DeletedAt is not null,
                item.DeletedAt))
            .ToArray();

        return new InfrastructureLifecycleOptionPage<WarehouseLifecycleOption>(
            items,
            hasMore ? query.Offset + query.Limit : null);
    }

    private static string DisplayWarehouse(string? code, string? name, Guid id)
    {
        if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(name)) return $"{code} · {name}";
        if (!string.IsNullOrWhiteSpace(name)) return name;
        if (!string.IsNullOrWhiteSpace(code)) return code;
        return id.ToString();
    }
}
