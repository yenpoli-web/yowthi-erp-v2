using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.DataProtection;
using YowThi.Erp.Domain.Infrastructure;
using YowThi.Erp.Domain.Outsourced;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.Processing;
using YowThi.Erp.Domain.Procurement;
using YowThi.Erp.Domain.Sales;
using YowThi.Erp.Domain.SalesHandling;

namespace YowThi.Erp.Infrastructure.Persistence.DataProtection;

internal sealed class EfHardDeleteOptionsReader(ErpDbContext dbContext) : IHardDeleteOptionsReader
{
    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetSuppliersAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<Supplier>().AsNoTracking().Select(item => new Projection
            {
                Id = item.Id,
                DisplayName = query.Locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh,
                Active = item.Active,
                RowVersion = item.RowVersion,
                DeletedAt = item.DeletedAt,
            }),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetCustomersAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<Customer>().AsNoTracking().Select(item => new Projection
            {
                Id = item.Id,
                DisplayName = query.Locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh,
                Active = item.Active,
                RowVersion = item.RowVersion,
                DeletedAt = item.DeletedAt,
            }),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetOutsourcedVendorsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<OutsourcedVendor>().AsNoTracking().Select(item => new Projection
            {
                Id = item.Id,
                DisplayName = query.Locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh,
                Active = item.Active,
                RowVersion = item.RowVersion,
                DeletedAt = item.DeletedAt,
            }),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetFarmersAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<Farmer>().AsNoTracking().Select(item => new Projection
            {
                Id = item.Id,
                DisplayName = query.Locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh,
                Active = item.Active,
                RowVersion = item.RowVersion,
                DeletedAt = item.DeletedAt,
            }),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetEmployeesAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<Employee>().AsNoTracking().Select(item => new Projection
            {
                Id = item.Id,
                DisplayName = query.Locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh,
                Active = item.Active,
                RowVersion = item.RowVersion,
                DeletedAt = item.DeletedAt,
            }),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetSalesPackagingItemsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<SalesPackagingItem>().AsNoTracking().Select(item => new Projection
            {
                Id = item.Id,
                DisplayName = query.Locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh,
                Active = item.Active,
                RowVersion = item.RowVersion,
                DeletedAt = item.DeletedAt,
            }),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetWarehousesAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<Warehouse>().AsNoTracking().Select(item => new Projection
            {
                Id = item.Id,
                DisplayName = query.Locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh,
                Active = item.Active,
                RowVersion = item.RowVersion,
                DeletedAt = item.DeletedAt,
            }),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetStorageLocationsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<StorageLocation>().AsNoTracking().Select(item => new Projection
            {
                Id = item.Id,
                DisplayName = query.Locale == "th-TH" ? item.NameThTh ?? item.NameZhTw ?? item.Code : item.NameZhTw ?? item.NameThTh ?? item.Code,
                Active = item.Active,
                RowVersion = item.RowVersion,
                DeletedAt = item.DeletedAt,
            }),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetContainersAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<Container>().AsNoTracking().Select(item => new Projection
            {
                Id = item.Id,
                DisplayName = query.Locale == "th-TH" ? item.NameThTh ?? item.NameZhTw : item.NameZhTw ?? item.NameThTh,
                Active = item.Active,
                RowVersion = item.RowVersion,
                DeletedAt = item.DeletedAt,
            }),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetOutsourcedSupplyBatchesAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<OutsourcedSupplyBatch>().AsNoTracking().Select(item => new Projection
            {
                Id = item.Id,
                DisplayName = item.Id.ToString(),
                Active = true,
                RowVersion = item.RowVersion,
                DeletedAt = item.DeletedAt,
            }),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetOutsourcedSupplyDetailsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<OutsourcedSupplyDetail>().AsNoTracking().Select(item => new Projection
            {
                Id = item.Id,
                DisplayName = item.Id.ToString(),
                Active = true,
                RowVersion = item.RowVersion,
                DeletedAt = item.DeletedAt,
            }),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetProcurementBatchesAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<ProcurementBatch>().AsNoTracking().Select(item => new Projection
            {
                Id = item.Id,
                DisplayName = item.Id.ToString(),
                Active = true,
                RowVersion = item.RowVersion,
                DeletedAt = item.DeletedAt,
            }),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetProcurementEntriesAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<ProcurementEntry>().AsNoTracking().Select(item => new Projection
            {
                Id = item.Id,
                DisplayName = item.Id.ToString(),
                Active = true,
                RowVersion = item.RowVersion,
                DeletedAt = item.DeletedAt,
            }),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetSalesAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<Sale>().AsNoTracking().Select(item => new Projection
            {
                Id = item.Id,
                DisplayName = item.Id.ToString(),
                Active = true,
                RowVersion = item.RowVersion,
                DeletedAt = item.DeletedAt,
            }),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetSalesDetailsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<SalesDetail>().AsNoTracking().Select(item => new Projection
            {
                Id = item.Id,
                DisplayName = item.Id.ToString(),
                Active = true,
                RowVersion = item.RowVersion,
                DeletedAt = item.DeletedAt,
            }),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetProcessingExecutionsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<ProcessingExecution>().AsNoTracking().Select(item => new Projection
            {
                Id = item.Id,
                DisplayName = item.Id.ToString(),
                Active = true,
                RowVersion = item.RowVersion,
                DeletedAt = item.DeletedAt,
            }),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetProcessingExecutionInputsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<ProcessingExecutionInput>().AsNoTracking().Select(item => new Projection
            {
                Id = item.ProcessingExecutionId,
                DisplayName = item.ProcessingExecutionId.ToString(),
                Active = true,
                RowVersion = item.RowVersion,
                DeletedAt = item.DeletedAt,
            }),
            query,
            cancellationToken);

    public ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetProcessingExecutionOutputsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken) =>
        GetPageAsync(
            dbContext.Set<ProcessingExecutionOutput>().AsNoTracking().Select(item => new Projection
            {
                Id = item.Id,
                DisplayName = item.Id.ToString(),
                Active = true,
                RowVersion = item.RowVersion,
                DeletedAt = item.DeletedAt,
            }),
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

    private sealed class Projection
    {
        public Guid Id { get; init; }
        public string? DisplayName { get; init; }
        public bool Active { get; init; }
        public long RowVersion { get; init; }
        public DateTimeOffset? DeletedAt { get; init; }
    }
}
