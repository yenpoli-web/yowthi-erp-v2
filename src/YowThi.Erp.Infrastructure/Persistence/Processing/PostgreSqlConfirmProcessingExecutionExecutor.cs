using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Processing;
using YowThi.Erp.Domain.Infrastructure;
using YowThi.Erp.Domain.Inventory;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.Processing;
using YowThi.Erp.Domain.ProcessingConfiguration;
using YowThi.Erp.Domain.Procurement;
using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Infrastructure.Persistence.Processing;

internal sealed class PostgreSqlConfirmProcessingExecutionExecutor : IConfirmProcessingExecutionExecutor
{
    private const string CommandType = "ConfirmProcessingExecution";
    private const string OutboxMessageType = "processing.execution.confirmed";
    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlConfirmProcessingExecutionExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public async ValueTask<ApplicationResult<ConfirmProcessingExecutionResult>> ExecuteAsync(
        ConfirmProcessingExecutionExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        var validationError = ConfirmProcessingExecutionValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ApplicationResult<ConfirmProcessingExecutionResult>.Failure(validationError);
        }

        return await _transactionRunner.ExecuteAsync(async ct =>
        {
            var now = _timeProvider.GetUtcNow();
            var acquisition = await AcquireCommandAsync(execution, now, ct);
            if (acquisition.Kind == CommandAcquisitionKind.Replay)
            {
                return CommandTransactionDecision<ApplicationResult<ConfirmProcessingExecutionResult>>.Rollback(
                    ApplicationResult<ConfirmProcessingExecutionResult>.Success(acquisition.ReplayResult!));
            }
            if (acquisition.Kind == CommandAcquisitionKind.Conflict)
            {
                return RollbackFailure(ApplicationErrorKind.Conflict, ProcessingApplicationErrorCodes.IdempotencyKeyReused);
            }

            var command = execution.Command;
            var employee = await _dbContext.Set<Employee>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == command.EmployeeId, ct);
            if (employee is null) return RollbackFailure(ApplicationErrorKind.NotFound, ProcessingApplicationErrorCodes.EmployeeNotFound);
            if (!employee.Active || employee.DeletedAt is not null) return RollbackFailure(ApplicationErrorKind.Conflict, ProcessingApplicationErrorCodes.EmployeeInactive);

            var batch = await _dbContext.Set<ProcurementBatch>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == command.ProcurementBatchId, ct);
            if (batch is null) return RollbackFailure(ApplicationErrorKind.NotFound, ProcessingApplicationErrorCodes.BatchNotFound);
            if (batch.LifecycleStatus != ProcurementBatchLifecycleStatus.ACTIVE || batch.DeletedAt is not null)
                return RollbackFailure(ApplicationErrorKind.Conflict, ProcessingApplicationErrorCodes.BatchUnavailable);
            if (batch.ProcessingRouteVersionId is not Guid batchRouteVersionId)
                return RollbackFailure(ApplicationErrorKind.Validation, ProcessingApplicationErrorCodes.BatchRouteRequired);

            var module = await _dbContext.Set<ProcessingModule>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == command.ProcessingModuleId, ct);
            if (module is null) return RollbackFailure(ApplicationErrorKind.NotFound, ProcessingApplicationErrorCodes.ModuleNotFound);
            if (module.ProcessingRouteVersionId != batchRouteVersionId)
                return RollbackFailure(ApplicationErrorKind.Conflict, ProcessingApplicationErrorCodes.ModuleRouteMismatch);

            var sourceError = await ValidateSourceAsync(module.ExecutionMode, command.Source, ct);
            if (sourceError is not null)
                return CommandTransactionDecision<ApplicationResult<ConfirmProcessingExecutionResult>>.Rollback(ApplicationResult<ConfirmProcessingExecutionResult>.Failure(sourceError));

            var moduleOutputs = await _dbContext.Set<ProcessingModuleOutput>()
                .AsNoTracking().Where(x => x.ProcessingModuleId == module.Id).OrderBy(x => x.OutputSequence).ToListAsync(ct);
            if (moduleOutputs.Count == 0 || moduleOutputs.Count != command.Outputs.Count)
                return RollbackFailure(ApplicationErrorKind.Validation, ProcessingApplicationErrorCodes.OutputDefinitionInvalid);

            var outputById = moduleOutputs.ToDictionary(x => x.Id);
            if (command.Outputs.Any(x => !outputById.ContainsKey(x.ProcessingModuleOutputId)))
                return RollbackFailure(ApplicationErrorKind.Validation, ProcessingApplicationErrorCodes.OutputDefinitionInvalid);

            var inputLocation = await ResolveInputLocationAsync(batch.Id, batch.ProcurementProductId, module, command, ct);
            if (inputLocation.Error is not null)
                return CommandTransactionDecision<ApplicationResult<ConfirmProcessingExecutionResult>>.Rollback(ApplicationResult<ConfirmProcessingExecutionResult>.Failure(inputLocation.Error));

            var processingExecutionId = Guid.CreateVersion7();
            var sourceKind = module.ExecutionMode == ProcessingExecutionMode.SOURCE_TRACKED ? command.Source!.SourceKind : (ProcessingSourceKind?)null;
            var supplierId = sourceKind == ProcessingSourceKind.SUPPLIER ? command.Source!.SupplierId : null;
            var processingExecution = Domain.Processing.ProcessingExecution.Create(
                processingExecutionId, command.WorkDate, command.EmployeeId, batch.Id, batchRouteVersionId, module.Id,
                module.ExecutionMode, module.NegativeInventoryPolicy, sourceKind, supplierId, now, execution.ActorAccountId.Value);

            var derivedOutputs = new List<DerivedOutput>(command.Outputs.Count);
            foreach (var requested in command.Outputs)
            {
                var definition = outputById[requested.ProcessingModuleOutputId];
                var resolved = await ResolveOutputAsync(processingExecutionId, definition, requested, ct);
                if (resolved.Error is not null)
                    return CommandTransactionDecision<ApplicationResult<ConfirmProcessingExecutionResult>>.Rollback(ApplicationResult<ConfirmProcessingExecutionResult>.Failure(resolved.Error));
                derivedOutputs.Add(resolved.Output!);
            }

            var input = await BuildInputAsync(processingExecutionId, module, command, derivedOutputs, ct);
            if (input.Error is not null)
                return CommandTransactionDecision<ApplicationResult<ConfirmProcessingExecutionResult>>.Rollback(ApplicationResult<ConfirmProcessingExecutionResult>.Failure(input.Error));

            if (module.ExecutionMode != ProcessingExecutionMode.FINAL_PACKAGING)
            {
                var available = await ReadSourceBalanceAsync(batch.Id, module, sourceKind, supplierId, inputLocation.StorageLocationId, ct);
                if (available < input.Input!.ConsumedQuantity)
                    return RollbackFailure(ApplicationErrorKind.Conflict, ProcessingApplicationErrorCodes.InsufficientInventory);
            }

            var inventoryOperationId = Guid.CreateVersion7();
            var inventoryOperation = InventoryOperation.CreateProcessing(inventoryOperationId, processingExecutionId, now, execution.ActorAccountId.Value);
            _dbContext.Add(processingExecution);
            _dbContext.Add(input.Input!);
            _dbContext.AddRange(derivedOutputs.Select(x => x.Entity));
            _dbContext.Add(inventoryOperation);
            await _dbContext.SaveChangesAsync(ct);

            var movementSequence = 1;
            await InsertConsumeMovementAsync(inventoryOperationId, movementSequence++, batch.Id, module, sourceKind, supplierId, inputLocation.StorageLocationId, input.Input!.ConsumedQuantity, now, ct);
            await ApplyPositionDeltaAsync(batch.Id, module, sourceKind, supplierId, inputLocation.StorageLocationId, -input.Input.ConsumedQuantity, module.ExecutionMode == ProcessingExecutionMode.FINAL_PACKAGING, ct);

            foreach (var output in derivedOutputs)
            {
                await InsertProduceMovementAsync(inventoryOperationId, movementSequence++, batch.Id, output, now, module.ExecutionMode == ProcessingExecutionMode.FINAL_PACKAGING, ct);
                await ApplyOutputPositionDeltaAsync(batch.Id, output, ct);
            }

            var result = new ConfirmProcessingExecutionResult(processingExecutionId, inventoryOperationId, processingExecution.RowVersion);
            var resultJson = JsonSerializer.Serialize(result, StoredJsonOptions);
            await AppendAuditAsync(execution, processingExecutionId, processingExecution.RowVersion, now, ct);
            await EnqueueOutboxAsync(execution, resultJson, now, ct);
            await MarkCommandSucceededAsync(execution.CommandId.Value, resultJson, now, ct);

            return CommandTransactionDecision<ApplicationResult<ConfirmProcessingExecutionResult>>.Commit(
                ApplicationResult<ConfirmProcessingExecutionResult>.Success(result));
        }, cancellationToken);
    }

    private async ValueTask<ApplicationError?> ValidateSourceAsync(ProcessingExecutionMode mode, ProcessingSourceSelection? source, CancellationToken ct)
    {
        if (mode != ProcessingExecutionMode.SOURCE_TRACKED)
            return source is null ? null : Error(ApplicationErrorKind.Validation, ProcessingApplicationErrorCodes.InvalidSourceShape);
        if (source is null) return Error(ApplicationErrorKind.Validation, ProcessingApplicationErrorCodes.InvalidSourceShape);
        if (source.SourceKind == ProcessingSourceKind.SUPPLIER)
        {
            if (source.SupplierId is not Guid supplierId) return Error(ApplicationErrorKind.Validation, ProcessingApplicationErrorCodes.InvalidSourceShape);
            var supplier = await _dbContext.Set<Supplier>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == supplierId, ct);
            if (supplier is null) return Error(ApplicationErrorKind.NotFound, ProcessingApplicationErrorCodes.SupplierNotFound);
            if (!supplier.Active || supplier.DeletedAt is not null) return Error(ApplicationErrorKind.Conflict, ProcessingApplicationErrorCodes.SupplierInactive);
        }
        else if (source.SupplierId is not null)
            return Error(ApplicationErrorKind.Validation, ProcessingApplicationErrorCodes.InvalidSourceShape);
        return null;
    }

    private async ValueTask<LocationResolution> ResolveInputLocationAsync(Guid batchId, Guid procurementProductId, ProcessingModule module, ConfirmProcessingExecutionCommand command, CancellationToken ct)
    {
        if (command.InputStorageLocationId is Guid explicitId)
            return await ValidateLocationAsync(explicitId, ProcessingApplicationErrorCodes.InputLocationNotFound, ProcessingApplicationErrorCodes.InputLocationInactive, ct);

        var candidates = await _dbContext.Set<InventoryPosition>().AsNoTracking()
            .Where(x => x.Origin == InventoryOrigin.IN_HOUSE && x.ProcurementBatchId == batchId && x.BalanceQuantity != 0)
            .Where(x => module.InputProcessMaterialId != null ? x.ProcessMaterialId == module.InputProcessMaterialId : x.ProcurementProductId == procurementProductId)
            .Select(x => x.StorageLocationId).Distinct().Take(2).ToListAsync(ct);
        return candidates.Count == 1
            ? new LocationResolution(candidates[0], null)
            : new LocationResolution(Guid.Empty, Error(ApplicationErrorKind.Validation, ProcessingApplicationErrorCodes.InputLocationRequired));
    }

    private async ValueTask<LocationResolution> ValidateLocationAsync(Guid id, string notFound, string inactive, CancellationToken ct)
    {
        var location = await _dbContext.Set<StorageLocation>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (location is null) return new(Guid.Empty, Error(ApplicationErrorKind.NotFound, notFound));
        if (!location.Active || location.DeletedAt is not null) return new(Guid.Empty, Error(ApplicationErrorKind.Conflict, inactive));
        return new(id, null);
    }

    private async ValueTask<OutputResolution> ResolveOutputAsync(Guid executionId, ProcessingModuleOutput definition, ProcessingOutputMeasurement requested, CancellationToken ct)
    {
        Guid? defaultLocationId;
        ProcessingExecutionOutput entity;
        decimal quantity;
        if (definition.OutputKind == ProcessingOutputKind.PROCESS_MATERIAL && definition.ProcessMaterialId is Guid materialId)
        {
            if (requested.ObservedScaleReading is not decimal scale || requested.CompletedQuantity is not null)
                return OutputResolution.Failed(Error(ApplicationErrorKind.Validation, ProcessingApplicationErrorCodes.OutputMeasurementInvalid));
            var material = await _dbContext.Set<ProcessMaterial>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == materialId, ct);
            if (material is null || !material.Active || material.DeletedAt is not null)
                return OutputResolution.Failed(Error(ApplicationErrorKind.Validation, ProcessingApplicationErrorCodes.OutputDefinitionInvalid));
            var tare = await ResolveTareAsync(material.UsesContainer, material.ContainerId, requested.ActualContainerCount, ct);
            if (tare.Error is not null) return OutputResolution.Failed(tare.Error);
            entity = ProcessingExecutionOutput.CreateProcessMaterial(Guid.CreateVersion7(), executionId, definition.Id, definition.DefaultWageRate, scale, requested.ActualContainerCount, tare.TareWeight);
            quantity = entity.DerivedNetQuantity!.Value;
            defaultLocationId = material.DefaultStorageLocationId;
            var location = await ResolveOutputLocationAsync(requested.OutputStorageLocationId, defaultLocationId, ct);
            if (location.Error is not null) return OutputResolution.Failed(location.Error);
            return OutputResolution.Resolved(new DerivedOutput(entity, InventoryObjectKind.PROCESS_MATERIAL, materialId, null, location.StorageLocationId, quantity));
        }

        if (definition.OutputKind == ProcessingOutputKind.SALES_PRODUCT && definition.SalesProductId is Guid salesProductId)
        {
            if (requested.CompletedQuantity is not decimal completed || requested.ObservedScaleReading is not null)
                return OutputResolution.Failed(Error(ApplicationErrorKind.Validation, ProcessingApplicationErrorCodes.OutputMeasurementInvalid));
            var product = await _dbContext.Set<SalesProduct>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == salesProductId, ct);
            if (product is null || !product.Active || product.DeletedAt is not null || product.PackagingWeight is null)
                return OutputResolution.Failed(Error(ApplicationErrorKind.Validation, ProcessingApplicationErrorCodes.OutputDefinitionInvalid));
            entity = ProcessingExecutionOutput.CreateSalesProduct(Guid.CreateVersion7(), executionId, definition.Id, definition.DefaultWageRate, completed, product.PackagingWeight.Value);
            defaultLocationId = product.DefaultStorageLocationId;
            var location = await ResolveOutputLocationAsync(requested.OutputStorageLocationId, defaultLocationId, ct);
            if (location.Error is not null) return OutputResolution.Failed(location.Error);
            return OutputResolution.Resolved(new DerivedOutput(entity, InventoryObjectKind.SALES_PRODUCT, null, salesProductId, location.StorageLocationId, completed));
        }
        return OutputResolution.Failed(Error(ApplicationErrorKind.Validation, ProcessingApplicationErrorCodes.OutputDefinitionInvalid));
    }

    private async ValueTask<TareResolution> ResolveTareAsync(bool usesContainer, Guid? containerId, int? actualCount, CancellationToken ct)
    {
        if (!usesContainer)
            return actualCount is null ? new(null, null) : new(null, Error(ApplicationErrorKind.Validation, ProcessingApplicationErrorCodes.OutputMeasurementInvalid));
        if (containerId is not Guid id || actualCount is null)
            return new(null, Error(ApplicationErrorKind.Validation, ProcessingApplicationErrorCodes.OutputMeasurementInvalid));
        var container = await _dbContext.Set<Container>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (container is null || !container.Active || container.DeletedAt is not null)
            return new(null, Error(ApplicationErrorKind.Validation, ProcessingApplicationErrorCodes.OutputMeasurementInvalid));
        return new(container.TareWeight, null);
    }

    private async ValueTask<LocationResolution> ResolveOutputLocationAsync(Guid? requested, Guid? defaultId, CancellationToken ct)
    {
        if (requested is Guid id)
            return await ValidateLocationAsync(id, ProcessingApplicationErrorCodes.OutputLocationNotFound, ProcessingApplicationErrorCodes.OutputLocationInactive, ct);
        if (defaultId is not Guid fallback)
            return new(Guid.Empty, Error(ApplicationErrorKind.Validation, ProcessingApplicationErrorCodes.OutputLocationRequired));
        var result = await ValidateLocationAsync(fallback, ProcessingApplicationErrorCodes.OutputLocationNotFound, ProcessingApplicationErrorCodes.OutputLocationInactive, ct);
        return result.Error is null ? result : new(Guid.Empty, Error(ApplicationErrorKind.Validation, ProcessingApplicationErrorCodes.OutputLocationRequired));
    }

    private async ValueTask<InputResolution> BuildInputAsync(Guid executionId, ProcessingModule module, ConfirmProcessingExecutionCommand command, IReadOnlyList<DerivedOutput> outputs, CancellationToken ct)
    {
        if (module.ExecutionMode == ProcessingExecutionMode.SOURCE_TRACKED)
        {
            if (command.InputScale is not { } scale) return InputResolution.Failed(Error(ApplicationErrorKind.Validation, ProcessingApplicationErrorCodes.InvalidInputScale));
            var routeInput = await _dbContext.Set<RouteInputConfig>().AsNoTracking().SingleOrDefaultAsync(x => x.ProcessingRouteVersionId == module.ProcessingRouteVersionId, ct);
            if (routeInput is null) return InputResolution.Failed(Error(ApplicationErrorKind.Validation, ProcessingApplicationErrorCodes.InvalidInputScale));
            var tare = await ResolveTareAsync(routeInput.UsesContainer, routeInput.ContainerId, scale.ActualContainerCount, ct);
            if (tare.Error is not null) return InputResolution.Failed(tare.Error);
            return InputResolution.Resolved(ProcessingExecutionInput.FromScale(executionId, scale.ObservedScaleReading, scale.ActualContainerCount, tare.TareWeight));
        }
        if (command.InputScale is not null) return InputResolution.Failed(Error(ApplicationErrorKind.Validation, ProcessingApplicationErrorCodes.InvalidInputScale));
        var consumption = module.ExecutionMode == ProcessingExecutionMode.FINAL_PACKAGING
            ? outputs.Sum(x => x.Entity.SourceConsumptionQuantity ?? 0m)
            : outputs.Sum(x => x.Quantity);
        return InputResolution.Resolved(module.ExecutionMode == ProcessingExecutionMode.FINAL_PACKAGING
            ? ProcessingExecutionInput.FromPackagingWeight(executionId, consumption)
            : ProcessingExecutionInput.FromOutputQuantity(executionId, consumption));
    }

    private async ValueTask<decimal> ReadSourceBalanceAsync(Guid batchId, ProcessingModule module, ProcessingSourceKind? sourceKind, Guid? supplierId, Guid locationId, CancellationToken ct)
    {
        var query = _dbContext.Set<InventoryPosition>().AsNoTracking().Where(x => x.Origin == InventoryOrigin.IN_HOUSE && x.ProcurementBatchId == batchId && x.StorageLocationId == locationId);
        query = module.InputProcessMaterialId is Guid materialId ? query.Where(x => x.ProcessMaterialId == materialId) : query.Where(x => x.InventoryObjectKind == InventoryObjectKind.PROCUREMENT_PRODUCT);
        if (sourceKind == ProcessingSourceKind.SUPPLIER) query = query.Where(x => x.RawSourceKind == InventoryRawSourceKind.SUPPLIER && x.SupplierId == supplierId);
        else if (sourceKind == ProcessingSourceKind.FARMERS_COMBINED) query = query.Where(x => x.RawSourceKind == InventoryRawSourceKind.FARMERS_COMBINED && x.SupplierId == null);
        else query = query.Where(x => x.RawSourceKind == null && x.SupplierId == null);
        return await query.SumAsync(x => x.BalanceQuantity, ct);
    }

    private async ValueTask InsertConsumeMovementAsync(Guid operationId, int sequence, Guid batchId, ProcessingModule module, ProcessingSourceKind? sourceKind, Guid? supplierId, Guid locationId, decimal quantity, DateTimeOffset now, CancellationToken ct)
    {
        var movementType = module.ExecutionMode == ProcessingExecutionMode.FINAL_PACKAGING ? "FINAL_PACKAGE_CONSUME" : "PROCESS_CONSUME";
        var objectKind = module.InputProcessMaterialId is null ? "PROCUREMENT_PRODUCT" : "PROCESS_MATERIAL";
        await using var cmd = CreateSqlCommand("""
            INSERT INTO inventory.inventory_movements
              (id, inventory_operation_id, sequence, movement_type, origin, procurement_batch_id, outsourced_supply_batch_id,
               inventory_object_kind, procurement_product_id, process_material_id, sales_product_id, storage_location_id,
               raw_source_kind, supplier_id, quantity_delta, sales_allocation_revision_item_id, recorded_at)
            SELECT @id,@op,@seq,@type,'IN_HOUSE',@batch,NULL,@kind,
                   CASE WHEN @kind='PROCUREMENT_PRODUCT' THEN procurement_product_id ELSE NULL END,
                   @material,NULL,@location,@raw,@supplier,-@quantity,NULL,@now
            FROM procurement.procurement_batches WHERE id=@batch;
            """);
        cmd.Parameters.AddWithValue("id", Guid.CreateVersion7()); cmd.Parameters.AddWithValue("op", operationId); cmd.Parameters.AddWithValue("seq", sequence);
        cmd.Parameters.AddWithValue("type", movementType); cmd.Parameters.AddWithValue("batch", batchId); cmd.Parameters.AddWithValue("kind", objectKind);
        cmd.Parameters.AddWithValue("material", (object?)module.InputProcessMaterialId ?? DBNull.Value); cmd.Parameters.AddWithValue("location", locationId);
        cmd.Parameters.AddWithValue("raw", (object?)(sourceKind?.ToString()) ?? DBNull.Value); cmd.Parameters.AddWithValue("supplier", (object?)supplierId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("quantity", quantity); cmd.Parameters.AddWithValue("now", now); await cmd.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask InsertProduceMovementAsync(Guid operationId, int sequence, Guid batchId, DerivedOutput output, DateTimeOffset now, bool finalPackaging, CancellationToken ct)
    {
        await using var cmd = CreateSqlCommand("""
            INSERT INTO inventory.inventory_movements
              (id, inventory_operation_id, sequence, movement_type, origin, procurement_batch_id, outsourced_supply_batch_id,
               inventory_object_kind, procurement_product_id, process_material_id, sales_product_id, storage_location_id,
               raw_source_kind, supplier_id, quantity_delta, sales_allocation_revision_item_id, recorded_at)
            VALUES (@id,@op,@seq,@type,'IN_HOUSE',@batch,NULL,@kind,NULL,@material,@sales,@location,NULL,NULL,@quantity,NULL,@now);
            """);
        cmd.Parameters.AddWithValue("id", Guid.CreateVersion7()); cmd.Parameters.AddWithValue("op", operationId); cmd.Parameters.AddWithValue("seq", sequence);
        cmd.Parameters.AddWithValue("type", finalPackaging ? "FINAL_PACKAGE_PRODUCE" : "PROCESS_PRODUCE"); cmd.Parameters.AddWithValue("batch", batchId);
        cmd.Parameters.AddWithValue("kind", output.ObjectKind.ToString()); cmd.Parameters.AddWithValue("material", (object?)output.ProcessMaterialId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("sales", (object?)output.SalesProductId ?? DBNull.Value); cmd.Parameters.AddWithValue("location", output.StorageLocationId);
        cmd.Parameters.AddWithValue("quantity", output.Quantity); cmd.Parameters.AddWithValue("now", now); await cmd.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask ApplyPositionDeltaAsync(Guid batchId, ProcessingModule module, ProcessingSourceKind? sourceKind, Guid? supplierId, Guid locationId, decimal delta, bool allowNegative, CancellationToken ct)
    {
        var objectKind = module.InputProcessMaterialId is null ? "PROCUREMENT_PRODUCT" : "PROCESS_MATERIAL";
        await using var cmd = CreateSqlCommand("""
            UPDATE inventory.inventory_positions p
            SET balance_quantity = p.balance_quantity + @delta, row_version = p.row_version + 1
            WHERE p.id = (
              SELECT id FROM inventory.inventory_positions
              WHERE origin='IN_HOUSE' AND procurement_batch_id=@batch AND storage_location_id=@location
                AND inventory_object_kind=@kind
                AND ((@material IS NULL AND process_material_id IS NULL) OR process_material_id=@material)
                AND ((@raw IS NULL AND raw_source_kind IS NULL) OR raw_source_kind=@raw)
                AND ((@supplier IS NULL AND supplier_id IS NULL) OR supplier_id=@supplier)
              LIMIT 1 FOR UPDATE)
              AND (@allow_negative OR p.balance_quantity + @delta >= 0);
            """);
        cmd.Parameters.AddWithValue("delta", delta); cmd.Parameters.AddWithValue("batch", batchId); cmd.Parameters.AddWithValue("location", locationId);
        cmd.Parameters.AddWithValue("kind", objectKind); cmd.Parameters.AddWithValue("material", (object?)module.InputProcessMaterialId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("raw", (object?)(sourceKind?.ToString()) ?? DBNull.Value); cmd.Parameters.AddWithValue("supplier", (object?)supplierId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("allow_negative", allowNegative);
        if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new InvalidOperationException("Processing source Inventory Position could not be updated atomically.");
    }

    private async ValueTask ApplyOutputPositionDeltaAsync(Guid batchId, DerivedOutput output, CancellationToken ct)
    {
        await using var cmd = CreateSqlCommand("""
            INSERT INTO inventory.inventory_positions
              (id, origin, procurement_batch_id, outsourced_supply_batch_id, inventory_object_kind, procurement_product_id,
               process_material_id, sales_product_id, storage_location_id, raw_source_kind, supplier_id, balance_quantity, row_version)
            VALUES (@id,'IN_HOUSE',@batch,NULL,@kind,NULL,@material,@sales,@location,NULL,NULL,@quantity,1)
            ON CONFLICT (origin, procurement_batch_id, outsourced_supply_batch_id, inventory_object_kind, procurement_product_id,
                         process_material_id, sales_product_id, storage_location_id, raw_source_kind, supplier_id)
            DO UPDATE SET balance_quantity=inventory.inventory_positions.balance_quantity+EXCLUDED.balance_quantity,
                          row_version=inventory.inventory_positions.row_version+1;
            """);
        cmd.Parameters.AddWithValue("id", Guid.CreateVersion7()); cmd.Parameters.AddWithValue("batch", batchId); cmd.Parameters.AddWithValue("kind", output.ObjectKind.ToString());
        cmd.Parameters.AddWithValue("material", (object?)output.ProcessMaterialId ?? DBNull.Value); cmd.Parameters.AddWithValue("sales", (object?)output.SalesProductId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("location", output.StorageLocationId); cmd.Parameters.AddWithValue("quantity", output.Quantity); await cmd.ExecuteNonQueryAsync(ct);
    }

    private async ValueTask<CommandAcquisition> AcquireCommandAsync(ConfirmProcessingExecutionExecution execution, DateTimeOffset now, CancellationToken ct)
    {
        await using (var insert = CreateSqlCommand("""
            INSERT INTO system.command_executions(command_id,command_type,request_hash,status,result_payload,actor_account_id,started_at,executed_at)
            VALUES(@id,@type,@hash,'IN_PROGRESS',NULL,@actor,@now,NULL) ON CONFLICT(command_id) DO NOTHING;
            """))
        { insert.Parameters.AddWithValue("id", execution.CommandId.Value); insert.Parameters.AddWithValue("type", CommandType); insert.Parameters.AddWithValue("hash", execution.RequestHash.Bytes.ToArray()); insert.Parameters.AddWithValue("actor", execution.ActorAccountId.Value); insert.Parameters.AddWithValue("now", now); if (await insert.ExecuteNonQueryAsync(ct)==1) return CommandAcquisition.Acquired(); }
        await using var select = CreateSqlCommand("SELECT command_type,request_hash,status,actor_account_id,result_payload::text FROM system.command_executions WHERE command_id=@id;");
        select.Parameters.AddWithValue("id", execution.CommandId.Value); await using var reader = await select.ExecuteReaderAsync(ct); if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("CommandExecution disappeared.");
        if (reader.GetString(0)!=CommandType || reader.GetGuid(3)!=execution.ActorAccountId.Value || !execution.RequestHash.Bytes.Span.SequenceEqual(reader.GetFieldValue<byte[]>(1))) return CommandAcquisition.Conflict();
        if (reader.GetString(2)!="SUCCEEDED" || reader.IsDBNull(4)) throw new InvalidOperationException("Existing processing command is not replayable.");
        return CommandAcquisition.Replay(JsonSerializer.Deserialize<ConfirmProcessingExecutionResult>(reader.GetString(4), StoredJsonOptions)!);
    }

    private async ValueTask AppendAuditAsync(ConfirmProcessingExecutionExecution execution, Guid id, long rowVersion, DateTimeOffset now, CancellationToken ct)
    {
        var auditId=Guid.CreateVersion7(); var key=JsonSerializer.Serialize(new { id }, StoredJsonOptions);
        await using var a=CreateSqlCommand("INSERT INTO audit.audit_events(id,command_id,command_type,event_kind,actor_account_id,occurred_at,reason_text) VALUES(@id,@cmd,@type,'BUSINESS_COMMAND',@actor,@now,NULL);");
        a.Parameters.AddWithValue("id",auditId);a.Parameters.AddWithValue("cmd",execution.CommandId.Value);a.Parameters.AddWithValue("type",CommandType);a.Parameters.AddWithValue("actor",execution.ActorAccountId.Value);a.Parameters.AddWithValue("now",now);await a.ExecuteNonQueryAsync(ct);
        await using var s=CreateSqlCommand("INSERT INTO audit.audit_event_subjects(audit_event_id,sequence,subject_kind,subject_key,change_kind,before_row_version,after_row_version,change_summary) VALUES(@id,1,'processing.execution',CAST(@key AS jsonb),'CREATE',NULL,@rv,NULL);");
        s.Parameters.AddWithValue("id",auditId);s.Parameters.AddWithValue("key",key);s.Parameters.AddWithValue("rv",rowVersion);await s.ExecuteNonQueryAsync(ct);
    }
    private async ValueTask EnqueueOutboxAsync(ConfirmProcessingExecutionExecution execution,string payload,DateTimeOffset now,CancellationToken ct){await using var c=CreateSqlCommand("INSERT INTO system.outbox_messages(id,message_type,message_version,payload,command_id,occurred_at,available_at,published_at,delivery_attempt_count,next_attempt_at,locked_until,lock_token,last_error_summary) VALUES(@id,@type,1,CAST(@payload AS jsonb),@cmd,@now,@now,NULL,0,NULL,NULL,NULL,NULL);");c.Parameters.AddWithValue("id",Guid.CreateVersion7());c.Parameters.AddWithValue("type",OutboxMessageType);c.Parameters.AddWithValue("payload",payload);c.Parameters.AddWithValue("cmd",execution.CommandId.Value);c.Parameters.AddWithValue("now",now);await c.ExecuteNonQueryAsync(ct);}
    private async ValueTask MarkCommandSucceededAsync(Guid id,string payload,DateTimeOffset now,CancellationToken ct){await using var c=CreateSqlCommand("UPDATE system.command_executions SET status='SUCCEEDED',result_payload=CAST(@payload AS jsonb),executed_at=@now WHERE command_id=@id AND status='IN_PROGRESS';");c.Parameters.AddWithValue("payload",payload);c.Parameters.AddWithValue("now",now);c.Parameters.AddWithValue("id",id);if(await c.ExecuteNonQueryAsync(ct)!=1)throw new InvalidOperationException("CommandExecution success transition failed.");}
    private NpgsqlCommand CreateSqlCommand(string sql){var tx=_dbContext.Database.CurrentTransaction??throw new InvalidOperationException("Processing SQL requires active transaction.");return new NpgsqlCommand(sql,(NpgsqlConnection)_dbContext.Database.GetDbConnection(),(NpgsqlTransaction)tx.GetDbTransaction());}
    private static CommandTransactionDecision<ApplicationResult<ConfirmProcessingExecutionResult>> RollbackFailure(ApplicationErrorKind kind,string code)=>CommandTransactionDecision<ApplicationResult<ConfirmProcessingExecutionResult>>.Rollback(ApplicationResult<ConfirmProcessingExecutionResult>.Failure(Error(kind,code)));
    private static ApplicationError Error(ApplicationErrorKind kind,string code)=>ApplicationError.Create(kind,code);

    private sealed record LocationResolution(Guid StorageLocationId, ApplicationError? Error);
    private sealed record TareResolution(decimal? TareWeight, ApplicationError? Error);
    private sealed record DerivedOutput(ProcessingExecutionOutput Entity, InventoryObjectKind ObjectKind, Guid? ProcessMaterialId, Guid? SalesProductId, Guid StorageLocationId, decimal Quantity);
    private sealed record OutputResolution(DerivedOutput? Output, ApplicationError? Error){public static OutputResolution Resolved(DerivedOutput output)=>new(output,null);public static OutputResolution Failed(ApplicationError error)=>new(null,error);}
    private sealed record InputResolution(ProcessingExecutionInput? Input, ApplicationError? Error){public static InputResolution Resolved(ProcessingExecutionInput input)=>new(input,null);public static InputResolution Failed(ApplicationError error)=>new(null,error);}
    private enum CommandAcquisitionKind{Acquired,Replay,Conflict}
    private sealed record CommandAcquisition(CommandAcquisitionKind Kind,ConfirmProcessingExecutionResult? ReplayResult){public static CommandAcquisition Acquired()=>new(CommandAcquisitionKind.Acquired,null);public static CommandAcquisition Replay(ConfirmProcessingExecutionResult result)=>new(CommandAcquisitionKind.Replay,result);public static CommandAcquisition Conflict()=>new(CommandAcquisitionKind.Conflict,null);}
}
