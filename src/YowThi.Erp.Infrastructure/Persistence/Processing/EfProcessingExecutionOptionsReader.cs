using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Processing;
using YowThi.Erp.Domain.Infrastructure;
using YowThi.Erp.Domain.Inventory;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.ProcessingConfiguration;
using YowThi.Erp.Domain.Procurement;
using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Infrastructure.Persistence.Processing;

internal sealed class EfProcessingExecutionOptionsReader(ErpDbContext dbContext) : IProcessingExecutionOptionsReader
{
    private const string LocaleZhTw = "zh-TW";
    private const string LocaleThTh = "th-TH";

    public async ValueTask<ProcessingExecutionOptionPage<ProcessingEmployeeOption>> GetEmployeesAsync(
        ProcessingExecutionOptionsQuery query,
        CancellationToken cancellationToken)
    {
        var validated = Validate(query);
        var preferZhTw = validated.Locale == LocaleZhTw;
        var employees = dbContext.Set<Employee>()
            .AsNoTracking()
            .Where(employee => employee.Active && employee.DeletedAt == null);

        if (validated.Search is not null)
        {
            var search = validated.Search;
            employees = employees.Where(employee =>
                (employee.NameZhTw != null && employee.NameZhTw.Contains(search))
                || (employee.NameThTh != null && employee.NameThTh.Contains(search)));
        }

        var ordered = preferZhTw
            ? employees
                .OrderBy(employee => employee.NameZhTw ?? employee.NameThTh)
                .ThenBy(employee => employee.Id)
                .Select(employee => new ProcessingEmployeeOption(employee.Id, employee.NameZhTw ?? employee.NameThTh!))
            : employees
                .OrderBy(employee => employee.NameThTh ?? employee.NameZhTw)
                .ThenBy(employee => employee.Id)
                .Select(employee => new ProcessingEmployeeOption(employee.Id, employee.NameThTh ?? employee.NameZhTw!));

        return await MaterializePageAsync(ordered, validated, cancellationToken);
    }

    public async ValueTask<ProcessingExecutionOptionPage<ProcessingBatchOption>> GetBatchesAsync(
        ProcessingExecutionOptionsQuery query,
        CancellationToken cancellationToken)
    {
        var validated = Validate(query);
        var preferZhTw = validated.Locale == LocaleZhTw;

        var batches =
            from batch in dbContext.Set<ProcurementBatch>().AsNoTracking()
            join product in dbContext.Set<ProcurementProduct>().AsNoTracking()
                on batch.ProcurementProductId equals product.Id
            where batch.LifecycleStatus == ProcurementBatchLifecycleStatus.ACTIVE
                  && batch.DeletedAt == null
                  && batch.ProcessingRouteVersionId != null
            select new
            {
                batch.Id,
                batch.ProcurementDate,
                batch.ProcurementProductId,
                ProcessingRouteVersionId = batch.ProcessingRouteVersionId!.Value,
                product.NameZhTw,
                product.NameThTh,
            };

        if (validated.Search is not null)
        {
            var search = validated.Search;
            batches = batches.Where(row =>
                (row.NameZhTw != null && row.NameZhTw.Contains(search))
                || (row.NameThTh != null && row.NameThTh.Contains(search)));
        }

        var ordered = preferZhTw
            ? batches
                .OrderByDescending(row => row.ProcurementDate)
                .ThenBy(row => row.NameZhTw ?? row.NameThTh)
                .ThenBy(row => row.Id)
                .Select(row => new ProcessingBatchOption(
                    row.Id,
                    row.ProcurementDate,
                    row.ProcurementProductId,
                    row.NameZhTw ?? row.NameThTh!,
                    row.ProcessingRouteVersionId))
            : batches
                .OrderByDescending(row => row.ProcurementDate)
                .ThenBy(row => row.NameThTh ?? row.NameZhTw)
                .ThenBy(row => row.Id)
                .Select(row => new ProcessingBatchOption(
                    row.Id,
                    row.ProcurementDate,
                    row.ProcurementProductId,
                    row.NameThTh ?? row.NameZhTw!,
                    row.ProcessingRouteVersionId));

        return await MaterializePageAsync(ordered, validated, cancellationToken);
    }

    public async ValueTask<ProcessingBatchModuleOptions?> GetModulesAsync(
        Guid procurementBatchId,
        ProcessingExecutionOptionsQuery query,
        CancellationToken cancellationToken)
    {
        EnsureNonEmpty(procurementBatchId, nameof(procurementBatchId));
        var validated = Validate(query);
        var routeVersionId = await GetExecutableBatchRouteVersionIdAsync(procurementBatchId, cancellationToken);
        if (routeVersionId is null)
        {
            return null;
        }

        var routeInput = await dbContext.Set<RouteInputConfig>()
            .AsNoTracking()
            .SingleOrDefaultAsync(config => config.ProcessingRouteVersionId == routeVersionId.Value, cancellationToken);

        var preferZhTw = validated.Locale == LocaleZhTw;
        var modules = dbContext.Set<ProcessingModule>()
            .AsNoTracking()
            .Where(module => module.ProcessingRouteVersionId == routeVersionId.Value);

        if (validated.Search is not null)
        {
            var search = validated.Search;
            modules = modules.Where(module =>
                (module.NameZhTw != null && module.NameZhTw.Contains(search))
                || (module.NameThTh != null && module.NameThTh.Contains(search)));
        }

        var inputUsesContainer = routeInput is null ? (bool?)null : routeInput.UsesContainer;
        var inputContainerId = routeInput?.ContainerId;
        var defaultInputContainerCount = routeInput?.DefaultContainerCount;

        var ordered = preferZhTw
            ? modules
                .OrderBy(module => module.NameZhTw ?? module.NameThTh)
                .ThenBy(module => module.Id)
                .Select(module => new ProcessingModuleOption(
                    module.Id,
                    module.NameZhTw ?? module.NameThTh!,
                    module.ExecutionMode,
                    module.InputProcessMaterialId,
                    inputUsesContainer,
                    inputContainerId,
                    defaultInputContainerCount))
            : modules
                .OrderBy(module => module.NameThTh ?? module.NameZhTw)
                .ThenBy(module => module.Id)
                .Select(module => new ProcessingModuleOption(
                    module.Id,
                    module.NameThTh ?? module.NameZhTw!,
                    module.ExecutionMode,
                    module.InputProcessMaterialId,
                    inputUsesContainer,
                    inputContainerId,
                    defaultInputContainerCount));

        return new ProcessingBatchModuleOptions(
            procurementBatchId,
            routeVersionId.Value,
            await MaterializePageAsync(ordered, validated, cancellationToken));
    }

    public async ValueTask<ProcessingExecutionOptionPage<ProcessingSupplierOption>> GetSuppliersAsync(
        ProcessingExecutionOptionsQuery query,
        CancellationToken cancellationToken)
    {
        var validated = Validate(query);
        var preferZhTw = validated.Locale == LocaleZhTw;
        var suppliers = dbContext.Set<Supplier>()
            .AsNoTracking()
            .Where(supplier => supplier.Active && supplier.DeletedAt == null);

        if (validated.Search is not null)
        {
            var search = validated.Search;
            suppliers = suppliers.Where(supplier =>
                (supplier.NameZhTw != null && supplier.NameZhTw.Contains(search))
                || (supplier.NameThTh != null && supplier.NameThTh.Contains(search)));
        }

        var ordered = preferZhTw
            ? suppliers
                .OrderBy(supplier => supplier.NameZhTw ?? supplier.NameThTh)
                .ThenBy(supplier => supplier.Id)
                .Select(supplier => new ProcessingSupplierOption(supplier.Id, supplier.NameZhTw ?? supplier.NameThTh!))
            : suppliers
                .OrderBy(supplier => supplier.NameThTh ?? supplier.NameZhTw)
                .ThenBy(supplier => supplier.Id)
                .Select(supplier => new ProcessingSupplierOption(supplier.Id, supplier.NameThTh ?? supplier.NameZhTw!));

        return await MaterializePageAsync(ordered, validated, cancellationToken);
    }

    public async ValueTask<ProcessingInputStorageLocationOptions?> GetInputStorageLocationsAsync(
        Guid procurementBatchId,
        Guid processingModuleId,
        ProcessingExecutionOptionsQuery query,
        CancellationToken cancellationToken)
    {
        EnsureNonEmpty(procurementBatchId, nameof(procurementBatchId));
        EnsureNonEmpty(processingModuleId, nameof(processingModuleId));
        var validated = Validate(query);
        var context = await GetBatchModuleContextAsync(procurementBatchId, processingModuleId, cancellationToken);
        if (context is null)
        {
            return null;
        }

        var positions = dbContext.Set<InventoryPosition>()
            .AsNoTracking()
            .Where(position =>
                position.Origin == InventoryOrigin.IN_HOUSE
                && position.ProcurementBatchId == procurementBatchId
                && position.BalanceQuantity != 0);

        positions = context.Module.InputProcessMaterialId is Guid materialId
            ? positions.Where(position => position.ProcessMaterialId == materialId)
            : positions.Where(position => position.ProcurementProductId == context.ProcurementProductId);

        var candidateIds = await positions
            .Select(position => position.StorageLocationId)
            .Distinct()
            .ToListAsync(cancellationToken);

        Guid? autoSelectionLocationId = null;
        if (candidateIds.Count == 1)
        {
            var candidateId = candidateIds[0];
            var selectable = await dbContext.Set<StorageLocation>()
                .AsNoTracking()
                .AnyAsync(location =>
                    location.Id == candidateId
                    && location.Active
                    && location.DeletedAt == null,
                    cancellationToken);
            if (selectable)
            {
                autoSelectionLocationId = candidateId;
            }
        }

        var locations = dbContext.Set<StorageLocation>()
            .AsNoTracking()
            .Where(location =>
                candidateIds.Contains(location.Id)
                && location.Active
                && location.DeletedAt == null);

        var page = await MaterializeLocationPageAsync(locations, validated, cancellationToken);
        return new ProcessingInputStorageLocationOptions(
            procurementBatchId,
            processingModuleId,
            autoSelectionLocationId,
            page);
    }

    public async ValueTask<ProcessingModuleOutputOptions?> GetModuleOutputsAsync(
        Guid procurementBatchId,
        Guid processingModuleId,
        string locale,
        CancellationToken cancellationToken)
    {
        EnsureNonEmpty(procurementBatchId, nameof(procurementBatchId));
        EnsureNonEmpty(processingModuleId, nameof(processingModuleId));
        ValidateLocale(locale);

        var context = await GetBatchModuleContextAsync(procurementBatchId, processingModuleId, cancellationToken);
        if (context is null)
        {
            return null;
        }

        var definitions = await dbContext.Set<ProcessingModuleOutput>()
            .AsNoTracking()
            .Where(output => output.ProcessingModuleId == processingModuleId)
            .OrderBy(output => output.OutputSequence)
            .ThenBy(output => output.Id)
            .ToListAsync(cancellationToken);

        var materialIds = definitions
            .Where(output => output.ProcessMaterialId != null)
            .Select(output => output.ProcessMaterialId!.Value)
            .Distinct()
            .ToArray();
        var salesProductIds = definitions
            .Where(output => output.SalesProductId != null)
            .Select(output => output.SalesProductId!.Value)
            .Distinct()
            .ToArray();

        var materials = await dbContext.Set<ProcessMaterial>()
            .AsNoTracking()
            .Where(material => materialIds.Contains(material.Id))
            .ToDictionaryAsync(material => material.Id, cancellationToken);
        var salesProducts = await dbContext.Set<SalesProduct>()
            .AsNoTracking()
            .Where(product => salesProductIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        var defaultLocationIds = materials.Values
            .Select(material => material.DefaultStorageLocationId)
            .Concat(salesProducts.Values.Select(product => product.DefaultStorageLocationId))
            .Where(id => id != null)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        var activeDefaultLocationIds = await dbContext.Set<StorageLocation>()
            .AsNoTracking()
            .Where(location =>
                defaultLocationIds.Contains(location.Id)
                && location.Active
                && location.DeletedAt == null)
            .Select(location => location.Id)
            .ToHashSetAsync(cancellationToken);

        var preferZhTw = locale == LocaleZhTw;
        var outputs = new List<ProcessingModuleOutputOption>(definitions.Count);
        foreach (var definition in definitions)
        {
            if (definition.OutputKind == ProcessingOutputKind.PROCESS_MATERIAL)
            {
                materials.TryGetValue(definition.ProcessMaterialId ?? Guid.Empty, out var material);
                var displayName = material is null
                    ? null
                    : preferZhTw ? material.NameZhTw ?? material.NameThTh : material.NameThTh ?? material.NameZhTw;
                var defaultLocationId = material?.DefaultStorageLocationId;
                outputs.Add(new ProcessingModuleOutputOption(
                    definition.Id,
                    definition.OutputSequence,
                    definition.OutputKind,
                    definition.ProcessMaterialId,
                    displayName,
                    material is { Active: true, DeletedAt: null },
                    material?.UsesContainer ?? false,
                    material?.ContainerId,
                    material?.DefaultContainerCount,
                    null,
                    defaultLocationId,
                    defaultLocationId is Guid locationId && activeDefaultLocationIds.Contains(locationId),
                    definition.DefaultWageRate));
                continue;
            }

            if (definition.OutputKind == ProcessingOutputKind.SALES_PRODUCT)
            {
                salesProducts.TryGetValue(definition.SalesProductId ?? Guid.Empty, out var product);
                var displayName = product is null
                    ? null
                    : preferZhTw ? product.NameZhTw ?? product.NameThTh : product.NameThTh ?? product.NameZhTw;
                var defaultLocationId = product?.DefaultStorageLocationId;
                outputs.Add(new ProcessingModuleOutputOption(
                    definition.Id,
                    definition.OutputSequence,
                    definition.OutputKind,
                    definition.SalesProductId,
                    displayName,
                    product is { Active: true, DeletedAt: null, PackagingWeight: not null },
                    false,
                    null,
                    null,
                    product?.PackagingWeight,
                    defaultLocationId,
                    defaultLocationId is Guid locationId && activeDefaultLocationIds.Contains(locationId),
                    definition.DefaultWageRate));
                continue;
            }

            outputs.Add(new ProcessingModuleOutputOption(
                definition.Id,
                definition.OutputSequence,
                definition.OutputKind,
                null,
                null,
                false,
                false,
                null,
                null,
                null,
                null,
                false,
                definition.DefaultWageRate));
        }

        return new ProcessingModuleOutputOptions(procurementBatchId, processingModuleId, outputs);
    }

    public async ValueTask<ProcessingExecutionOptionPage<ProcessingStorageLocationOption>> GetStorageLocationsAsync(
        ProcessingExecutionOptionsQuery query,
        CancellationToken cancellationToken)
    {
        var validated = Validate(query);
        var locations = dbContext.Set<StorageLocation>()
            .AsNoTracking()
            .Where(location => location.Active && location.DeletedAt == null);
        return await MaterializeLocationPageAsync(locations, validated, cancellationToken);
    }

    private async ValueTask<ProcessingExecutionOptionPage<ProcessingStorageLocationOption>> MaterializeLocationPageAsync(
        IQueryable<StorageLocation> locations,
        ValidatedQuery query,
        CancellationToken cancellationToken)
    {
        if (query.Search is not null)
        {
            var search = query.Search;
            locations = locations.Where(location =>
                (location.NameZhTw != null && location.NameZhTw.Contains(search))
                || (location.NameThTh != null && location.NameThTh.Contains(search))
                || (location.Code != null && location.Code.Contains(search)));
        }

        var ordered = query.Locale == LocaleZhTw
            ? locations
                .OrderBy(location => location.NameZhTw ?? location.NameThTh)
                .ThenBy(location => location.Code)
                .ThenBy(location => location.Id)
                .Select(location => new ProcessingStorageLocationOption(
                    location.Id,
                    location.NameZhTw ?? location.NameThTh!,
                    location.Code,
                    location.WarehouseId))
            : locations
                .OrderBy(location => location.NameThTh ?? location.NameZhTw)
                .ThenBy(location => location.Code)
                .ThenBy(location => location.Id)
                .Select(location => new ProcessingStorageLocationOption(
                    location.Id,
                    location.NameThTh ?? location.NameZhTw!,
                    location.Code,
                    location.WarehouseId));

        return await MaterializePageAsync(ordered, query, cancellationToken);
    }

    private async ValueTask<Guid?> GetExecutableBatchRouteVersionIdAsync(
        Guid procurementBatchId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Set<ProcurementBatch>()
            .AsNoTracking()
            .Where(batch =>
                batch.Id == procurementBatchId
                && batch.LifecycleStatus == ProcurementBatchLifecycleStatus.ACTIVE
                && batch.DeletedAt == null
                && batch.ProcessingRouteVersionId != null)
            .Select(batch => batch.ProcessingRouteVersionId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async ValueTask<BatchModuleContext?> GetBatchModuleContextAsync(
        Guid procurementBatchId,
        Guid processingModuleId,
        CancellationToken cancellationToken)
    {
        var batch = await dbContext.Set<ProcurementBatch>()
            .AsNoTracking()
            .Where(candidate =>
                candidate.Id == procurementBatchId
                && candidate.LifecycleStatus == ProcurementBatchLifecycleStatus.ACTIVE
                && candidate.DeletedAt == null
                && candidate.ProcessingRouteVersionId != null)
            .Select(candidate => new
            {
                candidate.ProcurementProductId,
                ProcessingRouteVersionId = candidate.ProcessingRouteVersionId!.Value,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (batch is null)
        {
            return null;
        }

        var module = await dbContext.Set<ProcessingModule>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.Id == processingModuleId
                && candidate.ProcessingRouteVersionId == batch.ProcessingRouteVersionId,
                cancellationToken);
        return module is null
            ? null
            : new BatchModuleContext(batch.ProcurementProductId, batch.ProcessingRouteVersionId, module);
    }

    private static async ValueTask<ProcessingExecutionOptionPage<T>> MaterializePageAsync<T>(
        IQueryable<T> orderedQuery,
        ValidatedQuery query,
        CancellationToken cancellationToken)
    {
        var rows = await orderedQuery
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return new ProcessingExecutionOptionPage<T>(
            rows,
            hasMore ? checked(query.Offset + query.Limit) : null);
    }

    private static ValidatedQuery Validate(ProcessingExecutionOptionsQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateLocale(query.Locale);
        if (query.Offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.Offset, "Query offset cannot be negative.");
        }
        if (query.Limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.Limit, "Query limit must be between 1 and 100.");
        }

        return new ValidatedQuery(
            query.Locale,
            string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            query.Offset,
            query.Limit);
    }

    private static void ValidateLocale(string locale)
    {
        if (locale is not (LocaleZhTw or LocaleThTh))
        {
            throw new ArgumentOutOfRangeException(nameof(locale), locale, "Unsupported operational locale.");
        }
    }

    private static void EnsureNonEmpty(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("ID cannot be empty.", parameterName);
        }
    }

    private sealed record BatchModuleContext(
        Guid ProcurementProductId,
        Guid ProcessingRouteVersionId,
        ProcessingModule Module);

    private sealed record ValidatedQuery(
        string Locale,
        string? Search,
        int Offset,
        int Limit);
}
