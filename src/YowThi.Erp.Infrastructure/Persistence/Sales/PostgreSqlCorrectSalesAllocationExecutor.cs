using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Sales;
using YowThi.Erp.Domain.Inventory;
using YowThi.Erp.Domain.Sales;
using YowThi.Erp.Infrastructure.Persistence.Inventory;

namespace YowThi.Erp.Infrastructure.Persistence.Sales;

internal sealed class PostgreSqlCorrectSalesAllocationExecutor : ICorrectSalesAllocationExecutor
{
    private const string CommandType = "CorrectSalesAllocation";
    private const string OutboxMessageType = "sales.allocation-revised";
    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlCorrectSalesAllocationExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public async ValueTask<ApplicationResult<CorrectSalesAllocationResult>> ExecuteAsync(
        CorrectSalesAllocationExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);

        var validationError = CorrectSalesAllocationValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ApplicationResult<CorrectSalesAllocationResult>.Failure(validationError);
        }

        return await _transactionRunner.ExecuteAsync(
            async ct =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await AcquireCommandAsync(execution, now, ct);
                if (acquisition.Kind == CommandAcquisitionKind.Replay)
                {
                    return CommandTransactionDecision<ApplicationResult<CorrectSalesAllocationResult>>.Rollback(
                        ApplicationResult<CorrectSalesAllocationResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == CommandAcquisitionKind.Conflict)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, SalesApplicationErrorCodes.IdempotencyKeyReused);
                }

                var command = execution.Command;
                var sale = await LockSaleAsync(command.SalesId, ct);
                if (sale is null)
                {
                    return RollbackFailure(ApplicationErrorKind.NotFound, SalesApplicationErrorCodes.SalesNotFound);
                }

                if (!string.Equals(sale.Status, "CONFIRMED", StringComparison.Ordinal))
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        SalesAllocationCorrectionErrorCodes.InvalidState);
                }

                if (sale.RowVersion != command.ExpectedRowVersion)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, SalesApplicationErrorCodes.StaleRowVersion);
                }

                var details = await _dbContext.Set<SalesDetail>()
                    .AsNoTracking()
                    .Where(x => x.SalesId == command.SalesId && x.DeletedAt == null)
                    .OrderBy(x => x.LineNumber)
                    .ToListAsync(ct);
                if (details.Count == 0)
                {
                    return RollbackFailure(ApplicationErrorKind.Validation, SalesApplicationErrorCodes.DetailInvalid);
                }

                var detailById = details.ToDictionary(x => x.Id);
                if (command.Allocations.Any(x => !detailById.ContainsKey(x.SalesDetailId)))
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Validation,
                        SalesAllocationCorrectionErrorCodes.InvalidInput);
                }

                var currentAllocations = await ReadCurrentAllocationsAsync(command.SalesId, ct);
                if (currentAllocations.Count == 0)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        SalesAllocationCorrectionErrorCodes.CurrentAllocationMissing);
                }

                foreach (var detail in details)
                {
                    decimal currentTotal;
                    try
                    {
                        currentTotal = currentAllocations
                            .Where(x => x.SalesDetailId == detail.Id)
                            .Sum(x => x.Quantity);
                    }
                    catch (OverflowException)
                    {
                        return RollbackFailure(
                            ApplicationErrorKind.Conflict,
                            SalesAllocationCorrectionErrorCodes.CurrentAllocationMissing);
                    }

                    if (currentTotal != detail.Quantity)
                    {
                        return RollbackFailure(
                            ApplicationErrorKind.Conflict,
                            SalesAllocationCorrectionErrorCodes.CurrentAllocationMissing);
                    }
                }

                var positions = await ReadPositionsAsync(
                    details.Select(x => x.SalesProductId).Distinct().ToArray(),
                    ct);
                var positionByIdentity = positions.ToDictionary(x => x.IdentityKey);

                foreach (var current in currentAllocations)
                {
                    if (current.StorageLocationId is null)
                    {
                        return RollbackFailure(
                            ApplicationErrorKind.Conflict,
                            SalesAllocationCorrectionErrorCodes.CurrentAllocationMissing);
                    }

                    var key = new PositionIdentityKey(
                        current.SalesProductId,
                        current.Origin,
                        current.Origin == InventoryOrigin.IN_HOUSE
                            ? current.ProcurementBatchId!.Value
                            : current.OutsourcedSupplyBatchId!.Value,
                        current.StorageLocationId.Value);
                    if (!positionByIdentity.TryGetValue(key, out var position))
                    {
                        return RollbackFailure(
                            ApplicationErrorKind.Conflict,
                            SalesAllocationCorrectionErrorCodes.CurrentAllocationMissing);
                    }

                    current.PositionId = position.Id;
                    position.EffectiveBalance += current.Quantity;
                }

                var plan = BuildAllocationPlan(details, command, positions);
                if (plan.Error is not null)
                {
                    return CommandTransactionDecision<ApplicationResult<CorrectSalesAllocationResult>>.Rollback(
                        ApplicationResult<CorrectSalesAllocationResult>.Failure(plan.Error));
                }

                var revisionNumber = await NextRevisionNumberAsync(command.SalesId, ct);
                if (revisionNumber <= 0)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        SalesAllocationCorrectionErrorCodes.CurrentAllocationMissing);
                }

                var revisionId = Guid.CreateVersion7();
                var operationId = Guid.CreateVersion7();
                var revision = SalesAllocationRevision.CreateCorrection(
                    revisionId,
                    command.SalesId,
                    revisionNumber,
                    now,
                    execution.ActorAccountId.Value);
                _dbContext.Add(revision);

                var currentPointers = await _dbContext.Set<SalesAllocation>()
                    .Where(x => detailById.Keys.Contains(x.SalesDetailId))
                    .ToListAsync(ct);
                if (currentPointers.Count != currentAllocations.Count)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        SalesAllocationCorrectionErrorCodes.CurrentAllocationMissing);
                }

                _dbContext.RemoveRange(currentPointers);

                var newAllocations = new List<RevisionAllocation>();
                foreach (var detail in details)
                {
                    var sequence = 1;
                    foreach (var planned in plan.Allocations.Where(x => x.SalesDetailId == detail.Id))
                    {
                        var itemId = Guid.CreateVersion7();
                        var item = SalesAllocationRevisionItem.Create(
                            itemId,
                            revisionId,
                            command.SalesId,
                            planned.SalesDetailId,
                            sequence,
                            planned.Origin,
                            planned.ProcurementBatchId,
                            planned.OutsourcedSupplyBatchId,
                            planned.Quantity,
                            planned.ManualOverride);
                        var pointer = SalesAllocation.Create(planned.SalesDetailId, sequence, itemId);
                        _dbContext.AddRange(item, pointer);
                        newAllocations.Add(new RevisionAllocation(planned, itemId, sequence));
                        sequence++;
                    }
                }

                var deltas = BuildAllocationDeltas(currentAllocations, newAllocations, positions);
                foreach (var batch in deltas
                             .Where(x => x.QuantityDelta != 0)
                             .Select(x => x.Position)
                             .Select(x => new BatchKey(x.Origin, x.BatchId))
                             .Distinct())
                {
                    var lifecycle = batch.Origin switch
                    {
                        InventoryOrigin.IN_HOUSE => await InventoryBatchLifecycleCommandLock.AcquireProcurementAsync(
                            _dbContext,
                            batch.BatchId,
                            ct),
                        InventoryOrigin.OUTSOURCED => await InventoryBatchLifecycleCommandLock.AcquireOutsourcedAsync(
                            _dbContext,
                            batch.BatchId,
                            ct),
                        _ => BatchLifecycleLockState.Missing(),
                    };

                    if (!lifecycle.Exists || !lifecycle.Active || lifecycle.Deleted)
                    {
                        return RollbackFailure(
                            ApplicationErrorKind.Conflict,
                            SalesAllocationCorrectionErrorCodes.LifecycleBlocked);
                    }
                }

                try
                {
                    await _dbContext.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, SalesApplicationErrorCodes.StaleRowVersion);
                }

                if (!await ApplyPositionDeltasAsync(deltas, ct))
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        SalesApplicationErrorCodes.ConcurrentInventoryChange);
                }

                await InsertInventoryOperationAsync(
                    operationId,
                    revisionId,
                    execution.ActorAccountId.Value,
                    now,
                    ct);
                await InsertAdjustmentMovementsAsync(operationId, deltas, now, ct);

                var newSalesRowVersion = await BumpSalesRowVersionAsync(
                    command.SalesId,
                    command.ExpectedRowVersion,
                    ct);
                if (newSalesRowVersion is null)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, SalesApplicationErrorCodes.StaleRowVersion);
                }

                var result = new CorrectSalesAllocationResult(
                    command.SalesId,
                    newSalesRowVersion.Value,
                    revisionId,
                    revisionNumber,
                    operationId);
                var storedResultJson = JsonSerializer.Serialize(result, StoredJsonOptions);

                await AppendCorrectionAuditAsync(
                    execution,
                    command.ExpectedRowVersion,
                    newSalesRowVersion.Value,
                    now,
                    ct);
                await EnqueueOutboxAsync(execution.CommandId.Value, storedResultJson, now, ct);
                await MarkCommandSucceededAsync(execution.CommandId.Value, storedResultJson, now, ct);

                return CommandTransactionDecision<ApplicationResult<CorrectSalesAllocationResult>>.Commit(
                    ApplicationResult<CorrectSalesAllocationResult>.Success(result));
            },
            cancellationToken);
    }

    private AllocationPlanResult BuildAllocationPlan(
        IReadOnlyList<SalesDetail> details,
        CorrectSalesAllocationCommand command,
        IReadOnlyList<AvailablePosition> positions)
    {
        var groups = positions
            .Where(x => x.EffectiveBalance > 0)
            .GroupBy(x => new SourceKey(x.SalesProductId, x.Origin, x.BatchId))
            .Select(group => new SourceGroup(
                group.Key,
                group.Min(x => x.SourceDate),
                group.OrderBy(x => x.StorageLocationId).ToArray()))
            .ToArray();
        var byKey = groups.ToDictionary(x => x.Key);
        var byProduct = groups
            .GroupBy(x => x.Key.SalesProductId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(x => x.Key.Origin == InventoryOrigin.OUTSOURCED ? 0 : 1)
                    .ThenBy(x => x.SourceDate)
                    .ThenBy(x => x.Key.BatchId)
                    .ToArray());
        var inputsByDetail = command.Allocations
            .GroupBy(x => x.SalesDetailId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var allocations = new List<PlannedAllocation>();

        foreach (var detail in details)
        {
            var remaining = detail.Quantity;
            var inputs = inputsByDetail.GetValueOrDefault(detail.Id) ?? Array.Empty<SalesAllocationCorrectionInput>();
            decimal inputTotal;
            try
            {
                inputTotal = inputs.Sum(x => x.AllocatedQuantity);
            }
            catch (OverflowException)
            {
                return AllocationPlanResult.Failed(ValidationError());
            }

            if (command.Mode == SalesAllocationCorrectionMode.COMPLETE_REPLACEMENT)
            {
                if (inputTotal != detail.Quantity)
                {
                    return AllocationPlanResult.Failed(ValidationError());
                }
            }
            else if (inputTotal > detail.Quantity)
            {
                return AllocationPlanResult.Failed(ValidationError());
            }

            foreach (var input in inputs)
            {
                if (input.AllocatedQuantity == 0)
                {
                    continue;
                }

                var sourceKey = new SourceKey(
                    detail.SalesProductId,
                    input.Origin,
                    input.Origin == InventoryOrigin.IN_HOUSE
                        ? input.ProcurementBatchId!.Value
                        : input.OutsourcedSupplyBatchId!.Value);
                if (!byKey.TryGetValue(sourceKey, out var source))
                {
                    return AllocationPlanResult.Failed(ConflictError(SalesApplicationErrorCodes.InsufficientStock));
                }

                var reservation = Reserve(
                    detail.Id,
                    detail.SalesProductId,
                    source,
                    input.AllocatedQuantity,
                    manualOverride: true);
                if (reservation.Error is not null)
                {
                    return AllocationPlanResult.Failed(reservation.Error);
                }

                allocations.Add(reservation.Allocation!);
                remaining -= input.AllocatedQuantity;
            }

            if (command.Mode == SalesAllocationCorrectionMode.COMPLETE_REPLACEMENT)
            {
                if (remaining != 0)
                {
                    return AllocationPlanResult.Failed(ValidationError());
                }

                continue;
            }

            if (remaining == 0)
            {
                continue;
            }

            if (!byProduct.TryGetValue(detail.SalesProductId, out var candidates))
            {
                return AllocationPlanResult.Failed(ConflictError(SalesApplicationErrorCodes.InsufficientStock));
            }

            foreach (var source in candidates)
            {
                if (remaining == 0)
                {
                    break;
                }

                var available = source.Positions.Sum(x => x.EffectiveBalance);
                if (available <= 0)
                {
                    continue;
                }

                var quantity = Math.Min(remaining, available);
                var reservation = Reserve(
                    detail.Id,
                    detail.SalesProductId,
                    source,
                    quantity,
                    manualOverride: false);
                if (reservation.Error is not null)
                {
                    return AllocationPlanResult.Failed(reservation.Error);
                }

                allocations.Add(reservation.Allocation!);
                remaining -= quantity;
            }

            if (remaining != 0)
            {
                return AllocationPlanResult.Failed(ConflictError(SalesApplicationErrorCodes.InsufficientStock));
            }
        }

        return AllocationPlanResult.Resolved(allocations);
    }

    private AllocationReservation Reserve(
        Guid salesDetailId,
        Guid salesProductId,
        SourceGroup source,
        decimal quantity,
        bool manualOverride)
    {
        var positive = source.Positions.Where(x => x.EffectiveBalance > 0).ToArray();
        if (positive.Length == 0)
        {
            return AllocationReservation.Failed(ConflictError(SalesApplicationErrorCodes.InsufficientStock));
        }

        if (positive.Select(x => x.StorageLocationId).Distinct().Count() != 1)
        {
            return AllocationReservation.Failed(
                ApplicationError.Create(ApplicationErrorKind.Validation, SalesApplicationErrorCodes.IssueLocationRequired));
        }

        if (positive.Length != 1)
        {
            throw new InvalidOperationException(
                "A Sales Product source batch has multiple current inventory positions for one storage location.");
        }

        var position = positive[0];
        if (position.EffectiveBalance < quantity)
        {
            return AllocationReservation.Failed(ConflictError(SalesApplicationErrorCodes.InsufficientStock));
        }

        position.EffectiveBalance -= quantity;
        return AllocationReservation.Resolved(new PlannedAllocation(
            salesDetailId,
            salesProductId,
            position.Origin,
            position.ProcurementBatchId,
            position.OutsourcedSupplyBatchId,
            position.StorageLocationId,
            position.Id,
            quantity,
            manualOverride));
    }

    private static IReadOnlyList<AllocationDelta> BuildAllocationDeltas(
        IReadOnlyList<CurrentAllocation> current,
        IReadOnlyList<RevisionAllocation> next,
        IReadOnlyList<AvailablePosition> positions)
    {
        var positionById = positions.ToDictionary(x => x.Id);
        var oldGroups = current
            .GroupBy(x => new AllocationDeltaKey(x.SalesDetailId, x.PositionId))
            .ToDictionary(
                x => x.Key,
                x => new AllocationSide(x.Sum(y => y.Quantity), x.First().RevisionItemId));
        var newGroups = next
            .GroupBy(x => new AllocationDeltaKey(x.Planned.SalesDetailId, x.Planned.PositionId))
            .ToDictionary(
                x => x.Key,
                x => new AllocationSide(x.Sum(y => y.Planned.Quantity), x.First().RevisionItemId));
        var keys = oldGroups.Keys.Concat(newGroups.Keys).Distinct().ToArray();
        var deltas = new List<AllocationDelta>();

        foreach (var key in keys)
        {
            var oldSide = oldGroups.GetValueOrDefault(key);
            var newSide = newGroups.GetValueOrDefault(key);
            var delta = (oldSide?.Quantity ?? 0) - (newSide?.Quantity ?? 0);
            if (delta == 0)
            {
                continue;
            }

            var lineageItemId = delta > 0
                ? oldSide!.RevisionItemId
                : newSide!.RevisionItemId;
            deltas.Add(new AllocationDelta(
                key.SalesDetailId,
                positionById[key.PositionId],
                delta,
                lineageItemId));
        }

        return deltas;
    }

    private async ValueTask<bool> ApplyPositionDeltasAsync(
        IReadOnlyList<AllocationDelta> deltas,
        CancellationToken cancellationToken)
    {
        foreach (var change in deltas
                     .GroupBy(x => x.Position.Id)
                     .Select(group => new
                     {
                         Position = group.First().Position,
                         Delta = group.Sum(x => x.QuantityDelta),
                     })
                     .Where(x => x.Delta != 0))
        {
            await using var command = CreateSqlCommand(
                """
                UPDATE inventory.inventory_positions
                SET balance_quantity = balance_quantity + @delta,
                    row_version = row_version + 1
                WHERE id = @position_id
                  AND row_version = @expected_row_version
                  AND (@delta > 0 OR balance_quantity >= -@delta);
                """);
            command.Parameters.AddWithValue("delta", change.Delta);
            command.Parameters.AddWithValue("position_id", change.Position.Id);
            command.Parameters.AddWithValue("expected_row_version", change.Position.RowVersion);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                return false;
            }
        }

        return true;
    }

    private async ValueTask<SaleState?> LockSaleAsync(Guid salesId, CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            SELECT status, row_version
            FROM sales.sales
            WHERE id = @sales_id
              AND deleted_at IS NULL
            FOR UPDATE;
            """);
        command.Parameters.AddWithValue("sales_id", salesId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SaleState(reader.GetString(0), reader.GetInt64(1));
    }

    private async ValueTask<IReadOnlyList<CurrentAllocation>> ReadCurrentAllocationsAsync(
        Guid salesId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            SELECT
                a.sales_detail_id,
                d.sales_product_id,
                i.origin,
                i.procurement_batch_id,
                i.outsourced_supply_batch_id,
                i.id,
                i.allocated_quantity,
                location.storage_location_id
            FROM sales.sales_allocations a
            JOIN sales.sales_allocation_revision_items i
              ON i.id = a.sales_allocation_revision_item_id
            JOIN sales.sales_details d
              ON d.id = a.sales_detail_id
            LEFT JOIN LATERAL (
                SELECT m.storage_location_id
                FROM inventory.inventory_movements m
                JOIN sales.sales_allocation_revision_items history_item
                  ON history_item.id = m.sales_allocation_revision_item_id
                JOIN sales.sales_allocation_revisions history_revision
                  ON history_revision.id = history_item.sales_allocation_revision_id
                WHERE history_revision.sales_id = @sales_id
                  AND history_item.sales_detail_id = i.sales_detail_id
                  AND history_item.origin = i.origin
                  AND history_item.procurement_batch_id IS NOT DISTINCT FROM i.procurement_batch_id
                  AND history_item.outsourced_supply_batch_id IS NOT DISTINCT FROM i.outsourced_supply_batch_id
                  AND m.movement_type IN ('SALES_ISSUE', 'SALES_ALLOCATION_ADJUSTMENT')
                ORDER BY m.recorded_at DESC, m.id DESC
                LIMIT 1
            ) location ON TRUE
            WHERE d.sales_id = @sales_id
              AND d.deleted_at IS NULL
            ORDER BY d.line_number, a.sequence;
            """);
        command.Parameters.AddWithValue("sales_id", salesId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<CurrentAllocation>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new CurrentAllocation(
                reader.GetGuid(0),
                reader.GetGuid(1),
                Enum.Parse<InventoryOrigin>(reader.GetString(2), ignoreCase: false),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.GetGuid(5),
                reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7)));
        }

        return result;
    }

    private async ValueTask<IReadOnlyList<AvailablePosition>> ReadPositionsAsync(
        IReadOnlyCollection<Guid> salesProductIds,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            SELECT
                p.id,
                p.sales_product_id,
                p.origin,
                p.procurement_batch_id,
                p.outsourced_supply_batch_id,
                p.storage_location_id,
                p.balance_quantity,
                p.row_version,
                CASE WHEN p.origin = 'OUTSOURCED' THEN ob.supply_date ELSE pb.procurement_date END AS source_date
            FROM inventory.inventory_positions p
            LEFT JOIN outsourced.outsourced_supply_batches ob
              ON ob.id = p.outsourced_supply_batch_id
            LEFT JOIN procurement.procurement_batches pb
              ON pb.id = p.procurement_batch_id
            WHERE p.inventory_object_kind = 'SALES_PRODUCT'
              AND p.sales_product_id = ANY(@sales_product_ids)
              AND p.raw_source_kind IS NULL
              AND p.supplier_id IS NULL
            ORDER BY p.sales_product_id, p.origin, source_date, p.storage_location_id;
            """);
        command.Parameters.AddWithValue("sales_product_ids", salesProductIds.ToArray());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<AvailablePosition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new AvailablePosition(
                reader.GetGuid(0),
                reader.GetGuid(1),
                Enum.Parse<InventoryOrigin>(reader.GetString(2), ignoreCase: false),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.GetGuid(5),
                reader.GetDecimal(6),
                reader.GetInt64(7),
                reader.GetFieldValue<DateOnly>(8)));
        }

        return result;
    }

    private async ValueTask<int> NextRevisionNumberAsync(Guid salesId, CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            "SELECT COALESCE(MAX(revision_number), -1) + 1 FROM sales.sales_allocation_revisions WHERE sales_id = @sales_id;");
        command.Parameters.AddWithValue("sales_id", salesId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async ValueTask InsertInventoryOperationAsync(
        Guid operationId,
        Guid revisionId,
        Guid actorAccountId,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            INSERT INTO inventory.inventory_operations
                (id, operation_type, procurement_entry_id, processing_execution_id,
                 outsourced_supply_detail_id, sales_id, sales_allocation_revision_id,
                 procurement_batch_id, recorded_at, recorded_by_account_id)
            VALUES
                (@id, 'SALES_ALLOCATION_REVISION', NULL, NULL,
                 NULL, NULL, @revision_id,
                 NULL, @recorded_at, @actor_id);
            """);
        command.Parameters.AddWithValue("id", operationId);
        command.Parameters.AddWithValue("revision_id", revisionId);
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.AddWithValue("actor_id", actorAccountId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask InsertAdjustmentMovementsAsync(
        Guid operationId,
        IReadOnlyList<AllocationDelta> deltas,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken)
    {
        var sequence = 1;
        foreach (var delta in deltas.OrderBy(x => x.SalesDetailId).ThenBy(x => x.Position.Id))
        {
            await using var command = CreateSqlCommand(
                """
                INSERT INTO inventory.inventory_movements
                    (id, inventory_operation_id, sequence, movement_type, origin,
                     procurement_batch_id, outsourced_supply_batch_id,
                     inventory_object_kind, procurement_product_id, process_material_id,
                     sales_product_id, storage_location_id, raw_source_kind, supplier_id,
                     quantity_delta, sales_allocation_revision_item_id, recorded_at)
                VALUES
                    (@id, @operation_id, @sequence, 'SALES_ALLOCATION_ADJUSTMENT', @origin,
                     @procurement_batch_id, @outsourced_supply_batch_id,
                     'SALES_PRODUCT', NULL, NULL,
                     @sales_product_id, @storage_location_id, NULL, NULL,
                     @quantity_delta, @revision_item_id, @recorded_at);
                """);
            command.Parameters.AddWithValue("id", Guid.CreateVersion7());
            command.Parameters.AddWithValue("operation_id", operationId);
            command.Parameters.AddWithValue("sequence", sequence);
            command.Parameters.AddWithValue("origin", delta.Position.Origin.ToString());
            command.Parameters.AddWithValue("procurement_batch_id", (object?)delta.Position.ProcurementBatchId ?? DBNull.Value);
            command.Parameters.AddWithValue("outsourced_supply_batch_id", (object?)delta.Position.OutsourcedSupplyBatchId ?? DBNull.Value);
            command.Parameters.AddWithValue("sales_product_id", delta.Position.SalesProductId);
            command.Parameters.AddWithValue("storage_location_id", delta.Position.StorageLocationId);
            command.Parameters.AddWithValue("quantity_delta", delta.QuantityDelta);
            command.Parameters.AddWithValue("revision_item_id", delta.LineageRevisionItemId);
            command.Parameters.AddWithValue("recorded_at", recordedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
            sequence++;
        }
    }

    private async ValueTask<long?> BumpSalesRowVersionAsync(
        Guid salesId,
        long expectedRowVersion,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            UPDATE sales.sales
            SET row_version = row_version + 1
            WHERE id = @sales_id
              AND row_version = @expected_row_version
              AND status = 'CONFIRMED'
              AND deleted_at IS NULL
            RETURNING row_version;
            """);
        command.Parameters.AddWithValue("sales_id", salesId);
        command.Parameters.AddWithValue("expected_row_version", expectedRowVersion);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null ? null : Convert.ToInt64(result);
    }

    private async ValueTask AppendCorrectionAuditAsync(
        CorrectSalesAllocationExecution execution,
        long beforeRowVersion,
        long afterRowVersion,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var correctedAuditEventId = await FindLatestAllocationAuditEventAsync(execution.Command.SalesId, cancellationToken)
            ?? throw new InvalidOperationException("Confirmed Sales allocation has no audit event to correct.");
        var correctionAuditEventId = Guid.CreateVersion7();
        var subjectKeyJson = JsonSerializer.Serialize(new { id = execution.Command.SalesId }, StoredJsonOptions);

        await using (var auditEvent = CreateSqlCommand(
            """
            INSERT INTO audit.audit_events
                (id, command_id, command_type, event_kind, actor_account_id, occurred_at, reason_text)
            VALUES
                (@id, @command_id, @command_type, 'CORRECTION', @actor_account_id, @occurred_at, NULL);
            """))
        {
            auditEvent.Parameters.AddWithValue("id", correctionAuditEventId);
            auditEvent.Parameters.AddWithValue("command_id", execution.CommandId.Value);
            auditEvent.Parameters.AddWithValue("command_type", CommandType);
            auditEvent.Parameters.AddWithValue("actor_account_id", execution.ActorAccountId.Value);
            auditEvent.Parameters.AddWithValue("occurred_at", occurredAt);
            await auditEvent.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var subject = CreateSqlCommand(
            """
            INSERT INTO audit.audit_event_subjects
                (audit_event_id, sequence, subject_kind, subject_key, change_kind,
                 before_row_version, after_row_version, change_summary)
            VALUES
                (@audit_event_id, 1, 'sales.sale', CAST(@subject_key AS jsonb), 'UPDATE',
                 @before_row_version, @after_row_version, NULL);
            """))
        {
            subject.Parameters.AddWithValue("audit_event_id", correctionAuditEventId);
            subject.Parameters.AddWithValue("subject_key", subjectKeyJson);
            subject.Parameters.AddWithValue("before_row_version", beforeRowVersion);
            subject.Parameters.AddWithValue("after_row_version", afterRowVersion);
            await subject.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var link = CreateSqlCommand(
            """
            INSERT INTO audit.correction_links
                (correction_audit_event_id, corrected_audit_event_id, correction_mode)
            VALUES
                (@correction_id, @corrected_id, 'COMPENSATION');
            """);
        link.Parameters.AddWithValue("correction_id", correctionAuditEventId);
        link.Parameters.AddWithValue("corrected_id", correctedAuditEventId);
        await link.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask<Guid?> FindLatestAllocationAuditEventAsync(
        Guid salesId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            SELECT e.id
            FROM audit.audit_events e
            JOIN audit.audit_event_subjects s ON s.audit_event_id = e.id
            WHERE e.command_type IN ('ConfirmSales', 'CorrectSalesAllocation')
              AND s.subject_kind = 'sales.sale'
              AND s.subject_key ->> 'id' = @sales_id
            ORDER BY e.occurred_at DESC, e.id DESC
            LIMIT 1;
            """);
        command.Parameters.AddWithValue("sales_id", salesId.ToString());
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null ? null : (Guid)result;
    }

    private async ValueTask<CommandAcquisition> AcquireCommandAsync(
        CorrectSalesAllocationExecution execution,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        await using (var insert = CreateSqlCommand(
            """
            INSERT INTO system.command_executions
                (command_id, command_type, request_hash, status, result_payload,
                 actor_account_id, started_at, executed_at)
            VALUES
                (@command_id, @command_type, @request_hash, 'IN_PROGRESS', NULL,
                 @actor_account_id, @started_at, NULL)
            ON CONFLICT (command_id) DO NOTHING;
            """))
        {
            insert.Parameters.AddWithValue("command_id", execution.CommandId.Value);
            insert.Parameters.AddWithValue("command_type", CommandType);
            insert.Parameters.AddWithValue("request_hash", execution.RequestHash.Bytes.ToArray());
            insert.Parameters.AddWithValue("actor_account_id", execution.ActorAccountId.Value);
            insert.Parameters.AddWithValue("started_at", startedAt);
            if (await insert.ExecuteNonQueryAsync(cancellationToken) == 1)
            {
                return CommandAcquisition.Acquired();
            }
        }

        await using var select = CreateSqlCommand(
            """
            SELECT command_type, request_hash, status, actor_account_id, result_payload::text
            FROM system.command_executions
            WHERE command_id = @command_id;
            """);
        select.Parameters.AddWithValue("command_id", execution.CommandId.Value);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Allocation correction CommandExecution conflict could not be loaded.");
        }

        if (!string.Equals(reader.GetString(0), CommandType, StringComparison.Ordinal)
            || reader.GetGuid(3) != execution.ActorAccountId.Value
            || !execution.RequestHash.Bytes.Span.SequenceEqual(reader.GetFieldValue<byte[]>(1)))
        {
            return CommandAcquisition.Conflict();
        }

        if (!string.Equals(reader.GetString(2), "SUCCEEDED", StringComparison.Ordinal) || reader.IsDBNull(4))
        {
            throw new InvalidOperationException("A committed allocation correction with the same identity is not replayable.");
        }

        var replay = JsonSerializer.Deserialize<CorrectSalesAllocationResult>(reader.GetString(4), StoredJsonOptions)
            ?? throw new InvalidOperationException("Stored allocation correction result could not be deserialized.");
        return CommandAcquisition.Replay(replay);
    }

    private async ValueTask EnqueueOutboxAsync(
        Guid commandId,
        string resultJson,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            INSERT INTO system.outbox_messages
                (id, message_type, message_version, payload, command_id,
                 occurred_at, available_at, published_at, delivery_attempt_count,
                 next_attempt_at, locked_until, lock_token, last_error_summary)
            VALUES
                (@id, @message_type, 1, CAST(@payload AS jsonb), @command_id,
                 @occurred_at, @occurred_at, NULL, 0,
                 NULL, NULL, NULL, NULL);
            """);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("message_type", OutboxMessageType);
        command.Parameters.AddWithValue("payload", resultJson);
        command.Parameters.AddWithValue("command_id", commandId);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask MarkCommandSucceededAsync(
        Guid commandId,
        string resultJson,
        DateTimeOffset executedAt,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            UPDATE system.command_executions
            SET status = 'SUCCEEDED',
                result_payload = CAST(@result_payload AS jsonb),
                executed_at = @executed_at
            WHERE command_id = @command_id
              AND status = 'IN_PROGRESS';
            """);
        command.Parameters.AddWithValue("result_payload", resultJson);
        command.Parameters.AddWithValue("executed_at", executedAt);
        command.Parameters.AddWithValue("command_id", commandId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Allocation correction CommandExecution could not transition to SUCCEEDED.");
        }
    }

    private NpgsqlCommand CreateSqlCommand(string sql)
    {
        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("CorrectSalesAllocation SQL requires an active command transaction.");
        return new NpgsqlCommand(
            sql,
            (NpgsqlConnection)_dbContext.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction());
    }

    private static CommandTransactionDecision<ApplicationResult<CorrectSalesAllocationResult>> RollbackFailure(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<CorrectSalesAllocationResult>>.Rollback(
            ApplicationResult<CorrectSalesAllocationResult>.Failure(ApplicationError.Create(kind, code)));

    private static ApplicationError ValidationError() =>
        ApplicationError.Create(ApplicationErrorKind.Validation, SalesAllocationCorrectionErrorCodes.InvalidInput);

    private static ApplicationError ConflictError(string code) =>
        ApplicationError.Create(ApplicationErrorKind.Conflict, code);

    private sealed record SaleState(string Status, long RowVersion);
    private sealed record SourceKey(Guid SalesProductId, InventoryOrigin Origin, Guid BatchId);
    private sealed record SourceGroup(SourceKey Key, DateOnly SourceDate, IReadOnlyList<AvailablePosition> Positions);
    private sealed record BatchKey(InventoryOrigin Origin, Guid BatchId);
    private sealed record PositionIdentityKey(Guid SalesProductId, InventoryOrigin Origin, Guid BatchId, Guid StorageLocationId);
    private sealed record AllocationDeltaKey(Guid SalesDetailId, Guid PositionId);
    private sealed record AllocationSide(decimal Quantity, Guid RevisionItemId);

    private sealed class CurrentAllocation
    {
        public CurrentAllocation(
            Guid salesDetailId,
            Guid salesProductId,
            InventoryOrigin origin,
            Guid? procurementBatchId,
            Guid? outsourcedSupplyBatchId,
            Guid revisionItemId,
            decimal quantity,
            Guid? storageLocationId)
        {
            SalesDetailId = salesDetailId;
            SalesProductId = salesProductId;
            Origin = origin;
            ProcurementBatchId = procurementBatchId;
            OutsourcedSupplyBatchId = outsourcedSupplyBatchId;
            RevisionItemId = revisionItemId;
            Quantity = quantity;
            StorageLocationId = storageLocationId;
        }

        public Guid SalesDetailId { get; }
        public Guid SalesProductId { get; }
        public InventoryOrigin Origin { get; }
        public Guid? ProcurementBatchId { get; }
        public Guid? OutsourcedSupplyBatchId { get; }
        public Guid RevisionItemId { get; }
        public decimal Quantity { get; }
        public Guid? StorageLocationId { get; }
        public Guid PositionId { get; set; }
    }

    private sealed class AvailablePosition
    {
        public AvailablePosition(
            Guid id,
            Guid salesProductId,
            InventoryOrigin origin,
            Guid? procurementBatchId,
            Guid? outsourcedSupplyBatchId,
            Guid storageLocationId,
            decimal balance,
            long rowVersion,
            DateOnly sourceDate)
        {
            Id = id;
            SalesProductId = salesProductId;
            Origin = origin;
            ProcurementBatchId = procurementBatchId;
            OutsourcedSupplyBatchId = outsourcedSupplyBatchId;
            StorageLocationId = storageLocationId;
            Balance = balance;
            EffectiveBalance = balance;
            RowVersion = rowVersion;
            SourceDate = sourceDate;
        }

        public Guid Id { get; }
        public Guid SalesProductId { get; }
        public InventoryOrigin Origin { get; }
        public Guid? ProcurementBatchId { get; }
        public Guid? OutsourcedSupplyBatchId { get; }
        public Guid StorageLocationId { get; }
        public decimal Balance { get; }
        public decimal EffectiveBalance { get; set; }
        public long RowVersion { get; }
        public DateOnly SourceDate { get; }
        public Guid BatchId => Origin == InventoryOrigin.IN_HOUSE
            ? ProcurementBatchId!.Value
            : OutsourcedSupplyBatchId!.Value;
        public PositionIdentityKey IdentityKey => new(SalesProductId, Origin, BatchId, StorageLocationId);
    }

    private sealed record PlannedAllocation(
        Guid SalesDetailId,
        Guid SalesProductId,
        InventoryOrigin Origin,
        Guid? ProcurementBatchId,
        Guid? OutsourcedSupplyBatchId,
        Guid StorageLocationId,
        Guid PositionId,
        decimal Quantity,
        bool ManualOverride);

    private sealed record RevisionAllocation(PlannedAllocation Planned, Guid RevisionItemId, int Sequence);
    private sealed record AllocationDelta(
        Guid SalesDetailId,
        AvailablePosition Position,
        decimal QuantityDelta,
        Guid LineageRevisionItemId);

    private sealed record AllocationPlanResult(
        IReadOnlyList<PlannedAllocation> Allocations,
        ApplicationError? Error)
    {
        public static AllocationPlanResult Resolved(IReadOnlyList<PlannedAllocation> allocations) => new(allocations, null);
        public static AllocationPlanResult Failed(ApplicationError error) => new(Array.Empty<PlannedAllocation>(), error);
    }

    private sealed record AllocationReservation(PlannedAllocation? Allocation, ApplicationError? Error)
    {
        public static AllocationReservation Resolved(PlannedAllocation allocation) => new(allocation, null);
        public static AllocationReservation Failed(ApplicationError error) => new(null, error);
    }

    private enum CommandAcquisitionKind
    {
        Acquired,
        Replay,
        Conflict,
    }

    private sealed record CommandAcquisition(CommandAcquisitionKind Kind, CorrectSalesAllocationResult? ReplayResult)
    {
        public static CommandAcquisition Acquired() => new(CommandAcquisitionKind.Acquired, null);
        public static CommandAcquisition Replay(CorrectSalesAllocationResult result) => new(CommandAcquisitionKind.Replay, result);
        public static CommandAcquisition Conflict() => new(CommandAcquisitionKind.Conflict, null);
    }
}
