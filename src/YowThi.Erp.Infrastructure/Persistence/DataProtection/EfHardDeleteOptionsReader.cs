using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.DataProtection;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.Processing;
using YowThi.Erp.Domain.Procurement;
using YowThi.Erp.Domain.Sales;

namespace YowThi.Erp.Infrastructure.Persistence.DataProtection;

internal sealed class EfHardDeleteOptionsReader(ErpDbContext dbContext) : IHardDeleteOptionsReader
{
    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetSuppliersAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<Supplier>().AsNoTracking().Select(item => new Projection(
                item.Id,
                query.Locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh,
                item.Active,
                item.RowVersion,
                item.DeletedAt)),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetCustomersAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<Customer>().AsNoTracking().Select(item => new Projection(
                item.Id,
                query.Locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh,
                item.Active,
                item.RowVersion,
                item.DeletedAt)),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetOutsourcedVendorsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<OutsourcedVendor>().AsNoTracking().Select(item => new Projection(
                item.Id,
                query.Locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh,
                item.Active,
                item.RowVersion,
                item.DeletedAt)),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetFarmersAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<Farmer>().AsNoTracking().Select(item => new Projection(
                item.Id,
                query.Locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh,
                item.Active,
                item.RowVersion,
                item.DeletedAt)),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetProcurementBatchesAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<ProcurementBatch>().AsNoTracking().Select(item => new Projection(
                item.Id,
                item.Id.ToString(),
                true,
                item.RowVersion,
                item.DeletedAt)),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetProcurementEntriesAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<ProcurementEntry>().AsNoTracking().Select(item => new Projection(
                item.Id,
                item.Id.ToString(),
                true,
                item.RowVersion,
                item.DeletedAt)),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetSalesAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<Sale>().AsNoTracking().Select(item => new Projection(
                item.Id,
                item.Id.ToString(),
                true,
                item.RowVersion,
                item.DeletedAt)),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetSalesDetailsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<SalesDetail>().AsNoTracking().Select(item => new Projection(
                item.Id,
                item.Id.ToString(),
                true,
                item.RowVersion,
                item.DeletedAt)),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetProcessingExecutionsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<ProcessingExecution>().AsNoTracking().Select(item => new Projection(
                item.Id,
                item.Id.ToString(),
                true,
                item.RowVersion,
                item.DeletedAt)),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetProcessingExecutionInputsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<ProcessingExecutionInput>().AsNoTracking().Select(item => new Projection(
                item.ProcessingExecutionId,
                item.ProcessingExecutionId.ToString(),
                true,
                item.RowVersion,
                item.DeletedAt)),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetProcessingExecutionOutputsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<ProcessingExecutionOutput>().AsNoTracking().Select(item => new Projection(
                item.Id,
                item.Id.ToString(),
                true,
                item.RowVersion,
                item.DeletedAt)),
            query,
            cancellationToken);

    private static async ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetPageAsync(
        IQueryable<Projection> source,
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(item => item.DisplayName != null && item.DisplayName.Contains(search));
        }

        var rows = await source
            .OrderBy(item => item.DeletedAt == null)
            .ThenBy(item => item.Active)
            .ThenBy(item => item.DisplayName)
            .ThenBy(item => item.Id)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        var items = rows.Take(query.Limit)
            .Select(item => new HardDeleteOption(
                item.Id,
                string.IsNullOrWhiteSpace(item.DisplayName) ? item.Id.ToString() : item.DisplayName,
                item.Active,
                item.RowVersion,
                item.DeletedAt is not null,
                item.DeletedAt))
            .ToArray();

        return new HardDeleteOptionPage<HardDeleteOption>(
            items,
            hasMore ? query.Offset + query.Limit : null);
    }

    private sealed record Projection(
        Guid Id,
        string? DisplayName,
        bool Active,
        long RowVersion,
        DateTimeOffset? DeletedAt);
}
