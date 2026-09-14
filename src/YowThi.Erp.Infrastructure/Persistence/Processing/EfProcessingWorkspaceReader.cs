using Microsoft.EntityFrameworkCore;
using YowThi.Erp.Application.Processing;
using YowThi.Erp.Domain.Infrastructure;
using YowThi.Erp.Domain.Inventory;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.Processing;
using YowThi.Erp.Domain.ProcessingConfiguration;
using YowThi.Erp.Domain.Procurement;
using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Infrastructure.Persistence.Processing;

internal sealed class EfProcessingWorkspaceReader(ErpDbContext dbContext) : IProcessingWorkspaceReader
{
    private const string LocaleZhTw = "zh-TW";
    private const string LocaleThTh = "th-TH";

    public async ValueTask<ProcessingWorkspaceListPage> GetExecutionsAsync(
        ProcessingWorkspaceListQuery query,
        CancellationToken cancellationToken)
    {
        var locale = ValidateLocale(query.Locale);
        if (query.Offset < 0 || query.Limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(query));
        }

        var source =
            from execution in dbContext.Set<ProcessingExecution>().AsNoTracking()
            join employee in dbContext.Set<Employee>().AsNoTracking() on execution.EmployeeId equals employee.Id
            join batch in dbContext.Set<ProcurementBatch>().AsNoTracking() on execution.ProcurementBatchId equals batch.Id
            join product in dbContext.Set<ProcurementProduct>().AsNoTracking() on batch.ProcurementProductId equals product.Id
            join module in dbContext.Set<ProcessingModule>().AsNoTracking() on execution.ProcessingModuleId equals module.Id
            select new
            {
                execution.Id,
                execution.WorkDate,
                execution.EmployeeId,
                execution.ProcurementBatchId,
                batch.ProcurementDate,
                batch.ProcurementProductId,
                execution.ProcessingModuleId,
                execution.ExecutionModeSnapshot,
                execution.RowVersion,
                execution.RecordedAt,
                execution.DeletedAt,
                EmployeeNameZhTw = employee.NameZhTw,
                EmployeeNameThTh = employee.NameThTh,
                ProductNameZhTw = product.NameZhTw,
                ProductNameThTh = product.NameThTh,
                ModuleNameZhTw = module.NameZhTw,
                ModuleNameThTh = module.NameThTh,
            };

        source = query.Status switch
        {
            ProcessingWorkspaceStatusFilter.Active => source.Where(item => item.DeletedAt == null),
            ProcessingWorkspaceStatusFilter.Deleted => source.Where(item => item.DeletedAt != null),
            ProcessingWorkspaceStatusFilter.All => source,
            _ => throw new ArgumentOutOfRangeException(nameof(query)),
        };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(item =>
                (item.EmployeeNameZhTw != null && item.EmployeeNameZhTw.Contains(search))
                || (item.EmployeeNameThTh != null && item.EmployeeNameThTh.Contains(search))
                || (item.ProductNameZhTw != null && item.ProductNameZhTw.Contains(search))
                || (item.ProductNameThTh != null && item.ProductNameThTh.Contains(search))
                || (item.ModuleNameZhTw != null && item.ModuleNameZhTw.Contains(search))
                || (item.ModuleNameThTh != null && item.ModuleNameThTh.Contains(search)));
        }

        var rows = await source
            .OrderByDescending(item => item.WorkDate)
            .ThenByDescending(item => item.RecordedAt)
            .ThenBy(item => item.Id)
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > query.Limit;
        var items = rows.Take(query.Limit)
            .Select(item => new ProcessingWorkspaceListItem(
                item.Id,
                item.WorkDate,
                item.EmployeeId,
                DisplayName(item.EmployeeNameZhTw, item.EmployeeNameThTh, locale, item.EmployeeId),
                item.ProcurementBatchId,
                item.ProcurementDate,
                item.ProcurementProductId,
                DisplayName(item.ProductNameZhTw, item.ProductNameThTh, locale, item.ProcurementProductId),
                item.ProcessingModuleId,
                DisplayName(item.ModuleNameZhTw, item.ModuleNameThTh, locale, item.ProcessingModuleId),
                item.ExecutionModeSnapshot.ToString(),
                item.RowVersion,
                item.RecordedAt,
                item.DeletedAt))
            .ToArray();

        return new ProcessingWorkspaceListPage(
            items,
            hasMore ? checked(query.Offset + query.Limit) : null);
    }

    public async ValueTask<ProcessingWorkspace?> GetExecutionAsync(
        Guid processingExecutionId,
        string locale,
        CancellationToken cancellationToken)
    {
        if (processingExecutionId == Guid.Empty)
        {
            throw new ArgumentException("Processing Execution ID cannot be empty.", nameof(processingExecutionId));
        }

        locale = ValidateLocale(locale);

        var header = await (
            from execution in dbContext.Set<ProcessingExecution>().AsNoTracking()
            join employee in dbContext.Set<Employee>().AsNoTracking() on execution.EmployeeId equals employee.Id
            join batch in dbContext.Set<ProcurementBatch>().AsNoTracking() on execution.ProcurementBatchId equals batch.Id
            join product in dbContext.Set<ProcurementProduct>().AsNoTracking() on batch.ProcurementProductId equals product.Id
            join module in dbContext.Set<ProcessingModule>().AsNoTracking() on execution.ProcessingModuleId equals module.Id
            where execution.Id == processingExecutionId
            select new
            {
                execution.Id,
                execution.WorkDate,
                execution.EmployeeId,
                execution.ProcurementBatchId,
                batch.ProcurementDate,
                batch.ProcurementProductId,
                execution.ProcessingRouteVersionId,
                execution.ProcessingModuleId,
                execution.ExecutionModeSnapshot,
                execution.ProcessingSourceKind,
                execution.SupplierId,
                execution.RowVersion,
                execution.RecordedAt,
                execution.DeletedAt,
                EmployeeNameZhTw = employee.NameZhTw,
                EmployeeNameThTh = employee.NameThTh,
                ProductNameZhTw = product.NameZhTw,
                ProductNameThTh = product.NameThTh,
                ModuleNameZhTw = module.NameZhTw,
                ModuleNameThTh = module.NameThTh,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (header is null)
        {
            return null;
        }

        var employeeDisplayName = DisplayName(
            header.EmployeeNameZhTw,
            header.EmployeeNameThTh,
            locale,
            header.EmployeeId);
        var procurementProductDisplayName = DisplayName(
            header.ProductNameZhTw,
            header.ProductNameThTh,
            locale,
            header.ProcurementProductId);
        var processingModuleDisplayName = DisplayName(
            header.ModuleNameZhTw,
            header.ModuleNameThTh,
            locale,
            header.ProcessingModuleId);

        string? supplierDisplayName = null;
        if (header.SupplierId is Guid supplierId)
        {
            var supplier = await dbContext.Set<Supplier>()
                .AsNoTracking()
                .Where(candidate => candidate.Id == supplierId)
                .Select(candidate => new { candidate.NameZhTw, candidate.NameThTh })
                .SingleOrDefaultAsync(cancellationToken);
            supplierDisplayName = supplier is null
                ? supplierId.ToString()
                : DisplayName(supplier.NameZhTw, supplier.NameThTh, locale, supplierId);
        }

        var inputEntity = await dbContext.Set<ProcessingExecutionInput>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                input => input.ProcessingExecutionId == processingExecutionId,
                cancellationToken);

        var outputEntities = await dbContext.Set<ProcessingExecutionOutput>()
            .AsNoTracking()
            .Where(output => output.ProcessingExecutionId == processingExecutionId)
            .OrderBy(output => output.Id)
            .ToListAsync(cancellationToken);

        var operationId = await dbContext.Set<InventoryOperation>()
            .AsNoTracking()
            .Where(operation =>
                operation.ProcessingExecutionId == processingExecutionId
                && operation.OperationType == InventoryOperationType.PROCESSING)
            .Select(operation => (Guid?)operation.Id)
            .SingleOrDefaultAsync(cancellationToken);

        var movements = operationId is not Guid inventoryOperationId
            ? []
            : await dbContext.Set<InventoryMovement>()
                .AsNoTracking()
                .Where(movement => movement.InventoryOperationId == inventoryOperationId)
                .OrderBy(movement => movement.Sequence)
                .ToListAsync(cancellationToken);

        var locationIds = movements
            .Select(movement => movement.StorageLocationId)
            .Distinct()
            .ToArray();
        var locations = await dbContext.Set<StorageLocation>()
            .AsNoTracking()
            .Where(location => locationIds.Contains(location.Id))
            .ToDictionaryAsync(location => location.Id, cancellationToken);

        var definitionIds = outputEntities
            .Select(output => output.ProcessingModuleOutputId)
            .Distinct()
            .ToArray();
        var definitions = await dbContext.Set<ProcessingModuleOutput>()
            .AsNoTracking()
            .Where(definition => definitionIds.Contains(definition.Id))
            .ToDictionaryAsync(definition => definition.Id, cancellationToken);

        var definitionMaterialIds = definitions.Values
            .Where(definition => definition.ProcessMaterialId != null)
            .Select(definition => definition.ProcessMaterialId ?? Guid.Empty)
            .ToArray();
        var definitionSalesProductIds = definitions.Values
            .Where(definition => definition.SalesProductId != null)
            .Select(definition => definition.SalesProductId ?? Guid.Empty)
            .ToArray();
        var movementMaterialIds = movements
            .Where(movement => movement.ProcessMaterialId != null)
            .Select(movement => movement.ProcessMaterialId ?? Guid.Empty)
            .ToArray();
        var movementSalesProductIds = movements
            .Where(movement => movement.SalesProductId != null)
            .Select(movement => movement.SalesProductId ?? Guid.Empty)
            .ToArray();

        var allMaterialIds = definitionMaterialIds
            .Concat(movementMaterialIds)
            .Distinct()
            .ToArray();
        var allSalesProductIds = definitionSalesProductIds
            .Concat(movementSalesProductIds)
            .Distinct()
            .ToArray();

        var materials = await dbContext.Set<ProcessMaterial>()
            .AsNoTracking()
            .Where(material => allMaterialIds.Contains(material.Id))
            .ToDictionaryAsync(material => material.Id, cancellationToken);
        var salesProducts = await dbContext.Set<SalesProduct>()
            .AsNoTracking()
            .Where(product => allSalesProductIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        var inputMovementCandidates = movements
            .Where(movement => movement.MovementType is InventoryMovementType.PROCESS_CONSUME
                or InventoryMovementType.FINAL_PACKAGE_CONSUME)
            .ToArray();
        var inputMovement = inputMovementCandidates.Length == 1
            ? inputMovementCandidates[0]
            : null;

        var input = inputEntity is null
            ? null
            : BuildInput(
                inputEntity,
                inputMovement,
                locations,
                materials,
                header.ProcurementProductId,
                procurementProductDisplayName,
                locale);

        var produceMovements = movements
            .Where(movement => movement.MovementType is InventoryMovementType.PROCESS_PRODUCE
                or InventoryMovementType.FINAL_PACKAGE_PRODUCE)
            .ToArray();
        var outputs = outputEntities
            .Select(output => BuildOutput(
                output,
                definitions,
                produceMovements,
                locations,
                materials,
                salesProducts,
                locale))
            .OrderBy(output => output.OutputSequence)
            .ThenBy(output => output.Id)
            .ToArray();

        return new ProcessingWorkspace(
            header.Id,
            header.WorkDate,
            header.EmployeeId,
            employeeDisplayName,
            header.ProcurementBatchId,
            header.ProcurementDate,
            header.ProcurementProductId,
            procurementProductDisplayName,
            header.ProcessingRouteVersionId,
            header.ProcessingModuleId,
            processingModuleDisplayName,
            header.ExecutionModeSnapshot.ToString(),
            header.ProcessingSourceKind?.ToString(),
            header.SupplierId,
            supplierDisplayName,
            header.RowVersion,
            header.RecordedAt,
            header.DeletedAt,
            input,
            outputs);
    }

    private static ProcessingWorkspaceInput BuildInput(
        ProcessingExecutionInput input,
        InventoryMovement? movement,
        IReadOnlyDictionary<Guid, StorageLocation> locations,
        IReadOnlyDictionary<Guid, ProcessMaterial> materials,
        Guid procurementProductId,
        string procurementProductDisplayName,
        string locale)
    {
        string? objectKind = null;
        Guid? objectId = null;
        string? objectDisplayName = null;
        Guid? locationId = null;
        string? locationDisplayName = null;

        if (movement is not null)
        {
            objectKind = movement.InventoryObjectKind.ToString();
            if (movement.InventoryObjectKind == InventoryObjectKind.PROCUREMENT_PRODUCT)
            {
                objectId = movement.ProcurementProductId;
                objectDisplayName = procurementProductId == objectId
                    ? procurementProductDisplayName
                    : objectId?.ToString();
            }
            else if (movement.InventoryObjectKind == InventoryObjectKind.PROCESS_MATERIAL
                && movement.ProcessMaterialId is Guid materialId)
            {
                objectId = materialId;
                objectDisplayName = materials.TryGetValue(materialId, out var material)
                    ? DisplayName(material.NameZhTw, material.NameThTh, locale, materialId)
                    : materialId.ToString();
            }

            locationId = movement.StorageLocationId;
            locationDisplayName = locations.TryGetValue(movement.StorageLocationId, out var location)
                ? DisplayName(location.NameZhTw, location.NameThTh, locale, location.Id)
                : movement.StorageLocationId.ToString();
        }

        return new ProcessingWorkspaceInput(
            input.ProcessingExecutionId,
            input.ConsumptionBasis.ToString(),
            input.ConsumedQuantity,
            input.ObservedScaleReading,
            input.ActualContainerCount,
            input.TareWeightSnapshot,
            input.DerivedNetQuantity,
            objectKind,
            objectId,
            objectDisplayName,
            locationId,
            locationDisplayName,
            input.RowVersion,
            input.DeletedAt);
    }

    private static ProcessingWorkspaceOutput BuildOutput(
        ProcessingExecutionOutput output,
        IReadOnlyDictionary<Guid, ProcessingModuleOutput> definitions,
        IReadOnlyList<InventoryMovement> produceMovements,
        IReadOnlyDictionary<Guid, StorageLocation> locations,
        IReadOnlyDictionary<Guid, ProcessMaterial> materials,
        IReadOnlyDictionary<Guid, SalesProduct> salesProducts,
        string locale)
    {
        definitions.TryGetValue(output.ProcessingModuleOutputId, out var definition);
        var outputSequence = definition?.OutputSequence ?? int.MaxValue;
        Guid? targetId = null;
        string? targetDisplayName = null;
        InventoryMovement[] movementCandidates = [];

        if (definition?.OutputKind == ProcessingOutputKind.PROCESS_MATERIAL
            && definition.ProcessMaterialId is Guid materialId)
        {
            targetId = materialId;
            targetDisplayName = materials.TryGetValue(materialId, out var material)
                ? DisplayName(material.NameZhTw, material.NameThTh, locale, materialId)
                : materialId.ToString();
            movementCandidates = produceMovements
                .Where(movement =>
                    movement.InventoryObjectKind == InventoryObjectKind.PROCESS_MATERIAL
                    && movement.ProcessMaterialId == materialId)
                .ToArray();
        }
        else if (definition?.OutputKind == ProcessingOutputKind.SALES_PRODUCT
            && definition.SalesProductId is Guid salesProductId)
        {
            targetId = salesProductId;
            targetDisplayName = salesProducts.TryGetValue(salesProductId, out var product)
                ? DisplayName(product.NameZhTw, product.NameThTh, locale, salesProductId)
                : salesProductId.ToString();
            movementCandidates = produceMovements
                .Where(movement =>
                    movement.InventoryObjectKind == InventoryObjectKind.SALES_PRODUCT
                    && movement.SalesProductId == salesProductId)
                .ToArray();
        }

        var movement = movementCandidates.Length == 1
            ? movementCandidates[0]
            : null;
        var storageLocationId = movement?.StorageLocationId;
        var storageLocationDisplayName = storageLocationId is Guid locationId
            ? locations.TryGetValue(locationId, out var location)
                ? DisplayName(location.NameZhTw, location.NameThTh, locale, locationId)
                : locationId.ToString()
            : null;

        return new ProcessingWorkspaceOutput(
            output.Id,
            output.ProcessingModuleOutputId,
            outputSequence,
            output.OutputKindSnapshot.ToString(),
            targetId,
            targetDisplayName,
            output.ConfiguredWageRateSnapshot,
            output.ObservedScaleReading,
            output.ActualContainerCount,
            output.TareWeightSnapshot,
            output.DerivedNetQuantity,
            output.CompletedQuantity,
            output.PackagingWeightSnapshot,
            output.SourceConsumptionQuantity,
            storageLocationId,
            storageLocationDisplayName,
            output.RowVersion,
            output.DeletedAt);
    }

    private static string DisplayName(
        string? zhTw,
        string? thTh,
        string locale,
        Guid fallbackId) =>
        locale == LocaleZhTw
            ? zhTw ?? thTh ?? fallbackId.ToString()
            : thTh ?? zhTw ?? fallbackId.ToString();

    private static string ValidateLocale(string locale) => locale switch
    {
        LocaleZhTw => LocaleZhTw,
        LocaleThTh => LocaleThTh,
        _ => throw new ArgumentOutOfRangeException(
            nameof(locale),
            locale,
            "Unsupported operational locale."),
    };
}
