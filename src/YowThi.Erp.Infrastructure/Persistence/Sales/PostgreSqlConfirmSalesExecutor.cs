using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Sales;
using YowThi.Erp.Domain.Finance;
using YowThi.Erp.Domain.Inventory;
using YowThi.Erp.Domain.Sales;

namespace YowThi.Erp.Infrastructure.Persistence.Sales;

internal sealed class PostgreSqlConfirmSalesExecutor : IConfirmSalesExecutor
{
    private const string CommandType = "ConfirmSales";
    private const string OutboxMessageType = "sales.confirmed";

    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlConfirmSalesExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public async ValueTask<ApplicationResult<ConfirmSalesResult>> ExecuteAsync(
        ConfirmSalesExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);

        var validationError = ConfirmSalesValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ApplicationResult<ConfirmSalesResult>.Failure(validationError);
        }

        return await _transactionRunner.ExecuteAsync(
            async operationCancellationToken =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await AcquireCommandAsync(execution, now, operationCancellationToken);

                if (acquisition.Kind == CommandAcquisitionKind.Replay)
                {
                    return CommandTransactionDecision<ApplicationResult<ConfirmSalesResult>>.Rollback(
                        ApplicationResult<ConfirmSalesResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == CommandAcquisitionKind.Conflict)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, SalesApplicationErrorCodes.IdempotencyKeyReused);
                }

                var command = execution.Command;
                var sale = await _dbContext.Set<Sale>()
                    .SingleOrDefaultAsync(
                        x => x.Id == command.SalesId && x.DeletedAt == null,
                        operationCancellationToken);

                if (sale is null)
                {
                    return RollbackFailure(ApplicationErrorKind.NotFound, SalesApplicationErrorCodes.SalesNotFound);
                }

                if (sale.Status != SalesStatus.DRAFT
                    || sale.ConfirmedAt is not null
                    || sale.ConfirmedByAccountId is not null)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, SalesApplicationErrorCodes.InvalidState);
                }

                if (sale.RowVersion != command.ExpectedRowVersion)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, SalesApplicationErrorCodes.StaleRowVersion);
                }

                var details = await _dbContext.Set<SalesDetail>()
                    .AsNoTracking()
                    .Where(x => x.SalesId == sale.Id && x.DeletedAt == null)
                    .OrderBy(x => x.LineNumber)
                    .ToListAsync(operationCancellationToken);

                if (details.Count == 0)
                {
                    return RollbackFailure(ApplicationErrorKind.Validation, SalesApplicationErrorCodes.DetailInvalid);
                }

                long totalReceivableThb = 0;
                foreach (var detail in details)
                {
                    if (!SalesDetailAmount.TryCalculate(
                            detail.Quantity,
                            detail.PricingBasisSnapshot,
                            detail.SalesWeightSnapshot,
                            detail.UnitPrice,
                            out var calculatedAmount)
                        || calculatedAmount != detail.AmountThb)
                    {
                        return RollbackFailure(ApplicationErrorKind.Validation, SalesApplicationErrorCodes.PricingInvalid);
                    }

                    try
                    {
                        totalReceivableThb = checked(totalReceivableThb + detail.AmountThb);
                    }
                    catch (OverflowException)
                    {
                        return RollbackFailure(ApplicationErrorKind.Validation, SalesApplicationErrorCodes.PricingInvalid);
                    }
                }

                var detailById = details.ToDictionary(x => x.Id);
                if (command.ManualAllocationOverrides.Any(x => !detailById.ContainsKey(x.SalesDetailId)))
                {
                    return RollbackFailure(ApplicationErrorKind.Validation, SalesApplicationErrorCodes.AllocationInvalid);
                }

                var inventoryPositions = await ReadSellablePositionsAsync(
                    details.Select(x => x.SalesProductId).Distinct().ToArray(),
                    operationCancellationToken);

                var allocationPlan = BuildAllocationPlan(details, command.ManualAllocationOverrides, inventoryPositions);
                if (allocationPlan.Error is not null)
                {
                    return CommandTransactionDecision<ApplicationResult<ConfirmSalesResult>>.Rollback(
                        ApplicationResult<ConfirmSalesResult>.Failure(allocationPlan.Error));
                }

                var deductionsApplied = await ApplyInventoryDeductionsAsync(
                    allocationPlan.Allocations,
                    operationCancellationToken);
                if (!deductionsApplied)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        SalesApplicationErrorCodes.ConcurrentInventoryChange);
                }

                var allocationRevisionId = Guid.CreateVersion7();
                var inventoryOperationId = Guid.CreateVersion7();
                var receivableId = Guid.CreateVersion7();

                var allocationRevision = SalesAllocationRevision.CreateInitial(
                    allocationRevisionId,
                    sale.Id,
                    now,
                    execution.ActorAccountId.Value);
                var inventoryOperation = InventoryOperation.CreateSalesIssue(
                    inventoryOperationId,
                    sale.Id,
                    now,
                    execution.ActorAccountId.Value);
                var receivable = Receivable.Create(receivableId, sale.Id, now);
                var outstanding = ReceivableOutstandingPosition.Create(receivableId, totalReceivableThb, now);

                _dbContext.AddRange(allocationRevision, inventoryOperation, receivable, outstanding);

                foreach (var detail in details)
                {
                    _dbContext.Add(ReceivableObligationItem.Create(
                        Guid.CreateVersion7(),
                        receivableId,
                        sale.Id,
                        detail.Id,
                        detail.AmountThb,
                        now));
                }

                var movementSequence = 1;
                foreach (var detailGroup in allocationPlan.Allocations
                             .GroupBy(x => x.SalesDetailId)
                             .OrderBy(group => detailById[group.Key].LineNumber))
                {
                    var allocationSequence = 1;
                    foreach (var planned in detailGroup)
                    {
                        var revisionItemId = Guid.CreateVersion7();
                        var revisionItem = SalesAllocationRevisionItem.Create(
                            revisionItemId,
                            allocationRevisionId,
                            sale.Id,
                            planned.SalesDetailId,
                            allocationSequence,
                            planned.Origin,
                            planned.ProcurementBatchId,
                            planned.OutsourcedSupplyBatchId,
                            planned.Quantity,
                            planned.ManualOverride);
                        var currentAllocation = SalesAllocation.Create(
                            planned.SalesDetailId,
                            allocationSequence,
                            revisionItemId);
                        var movement = InventoryMovement.CreateSalesIssue(
                            Guid.CreateVersion7(),
                            inventoryOperationId,
                            movementSequence,
                            planned.Origin,
                            planned.ProcurementBatchId,
                            planned.OutsourcedSupplyBatchId,
                            planned.SalesProductId,
                            planned.StorageLocationId,
                            planned.Quantity,
                            revisionItemId,
                            now);

                        _dbContext.AddRange(revisionItem, currentAllocation, movement);
                        allocationSequence++;
                        movementSequence++;
                    }
                }

                sale.Confirm(now, execution.ActorAccountId.Value);

                try
                {
                    await _dbContext.SaveChangesAsync(operationCancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, SalesApplicationErrorCodes.StaleRowVersion);
                }

                var result = new ConfirmSalesResult(
                    sale.Id,
                    sale.RowVersion,
                    receivableId,
                    allocationRevisionId,
                    inventoryOperationId);
                var storedResultJson = JsonSerializer.Serialize(result, StoredJsonOptions);

                await AppendAuditAsync(
                    execution,
                    sale.Id,
                    command.ExpectedRowVersion,
                    sale.RowVersion,
                    now,
                    operationCancellationToken);
                await EnqueueOutboxAsync(execution, storedResultJson, now, operationCancellationToken);
                await MarkCommandSucceededAsync(
                    execution.CommandId.Value,
                    storedResultJson,
                    now,
                    operationCancellationToken);

                return CommandTransactionDecision<ApplicationResult<ConfirmSalesResult>>.Commit(
                    ApplicationResult<ConfirmSalesResult>.Success(result));
            },
            cancellationToken);
    }

    private AllocationPlanResult BuildAllocationPlan(
        IReadOnlyList<SalesDetail> details,
        IReadOnlyList<SalesManualAllocationOverride> manualOverrides,
        IReadOnlyList<SellablePosition> positions)
    {
        var groups = positions
            .GroupBy(position => new SourceKey(
                position.SalesProductId,
                position.Origin,
                position.Origin == InventoryOrigin.IN_HOUSE
                    ? position.ProcurementBatchId!.Value
                    : position.OutsourcedSupplyBatchId!.Value))
            .Select(group => new SourceGroup(
                group.Key,
                group.Min(position => position.SourceDate),
                group.OrderBy(position => position.StorageLocationId).ToArray()))
            .ToArray();

        var byKey = groups.ToDictionary(group => group.Key);
        var byProduct = groups
            .GroupBy(group => group.Key.SalesProductId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(item => item.Key.Origin == InventoryOrigin.OUTSOURCED ? 0 : 1)
                    .ThenBy(item => item.SourceDate)
                    .ThenBy(item => item.Key.BatchId)
                    .ToArray());
        var overridesByDetail = manualOverrides
            .GroupBy(item => item.SalesDetailId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var allocations = new List<PlannedAllocation>();

        foreach (var detail in details)
        {
            var remainingQuantity = detail.Quantity;
            if (overridesByDetail.TryGetValue(detail.Id, out var detailOverrides))
            {
                decimal overrideTotal;
                try
                {
                    overrideTotal = detailOverrides.Sum(item => item.AllocatedQuantity);
                }
                catch (OverflowException)
                {
                    return AllocationPlanResult.Failed(
                        Error(ApplicationErrorKind.Validation, SalesApplicationErrorCodes.AllocationInvalid));
                }

                if (overrideTotal > detail.Quantity)
                {
                    return AllocationPlanResult.Failed(
                        Error(ApplicationErrorKind.Validation, SalesApplicationErrorCodes.AllocationInvalid));
                }

                foreach (var manual in detailOverrides)
                {
                    if (manual.AllocatedQuantity == 0)
                    {
                        continue;
                    }

                    var sourceKey = new SourceKey(
                        detail.SalesProductId,
                        manual.Origin,
                        manual.Origin == InventoryOrigin.IN_HOUSE
                            ? manual.ProcurementBatchId!.Value
                            : manual.OutsourcedSupplyBatchId!.Value);
                    if (!byKey.TryGetValue(sourceKey, out var sourceGroup))
                    {
                        return AllocationPlanResult.Failed(
                            Error(ApplicationErrorKind.Conflict, SalesApplicationErrorCodes.InsufficientStock));
                    }

                    var reservation = ReserveFromSource(
                        detail.Id,
                        detail.SalesProductId,
                        sourceGroup,
                        manual.AllocatedQuantity,
                        manualOverride: true);
                    if (reservation.Error is not null)
                    {
                        return AllocationPlanResult.Failed(reservation.Error);
                    }

                    allocations.Add(reservation.Allocation!);
                    remainingQuantity -= manual.AllocatedQuantity;
                }
            }

            if (remainingQuantity == 0)
            {
                continue;
            }

            if (!byProduct.TryGetValue(detail.SalesProductId, out var candidates))
            {
                return AllocationPlanResult.Failed(
                    Error(ApplicationErrorKind.Conflict, SalesApplicationErrorCodes.InsufficientStock));
            }

            foreach (var sourceGroup in candidates)
            {
                if (remainingQuantity == 0)
                {
                    break;
                }

                var available = sourceGroup.Positions.Sum(position => position.RemainingBalance);
                if (available <= 0)
                {
                    continue;
                }

                var quantity = Math.Min(remainingQuantity, available);
                var reservation = ReserveFromSource(
                    detail.Id,
                    detail.SalesProductId,
                    sourceGroup,
                    quantity,
                    manualOverride: false);
                if (reservation.Error is not null)
                {
                    return AllocationPlanResult.Failed(reservation.Error);
                }

                allocations.Add(reservation.Allocation!);
                remainingQuantity -= quantity;
            }

            if (remainingQuantity != 0)
            {
                return AllocationPlanResult.Failed(
                    Error(ApplicationErrorKind.Conflict, SalesApplicationErrorCodes.InsufficientStock));
            }
        }

        return AllocationPlanResult.Resolved(allocations);
    }

    private AllocationReservation ReserveFromSource(
        Guid salesDetailId,
        Guid salesProductId,
        SourceGroup sourceGroup,
        decimal quantity,
        bool manualOverride)
    {
        var positivePositions = sourceGroup.Positions
            .Where(position => position.RemainingBalance > 0)
            .ToArray();
        if (positivePositions.Length == 0)
        {
            return AllocationReservation.Failed(
                Error(ApplicationErrorKind.Conflict, SalesApplicationErrorCodes.InsufficientStock));
        }

        if (positivePositions.Select(position => position.StorageLocationId).Distinct().Count() != 1)
        {
            // SALES-001: do not select a storage location when the source batch spans locations.
            return AllocationReservation.Failed(
                Error(ApplicationErrorKind.Validation, SalesApplicationErrorCodes.IssueLocationRequired));
        }

        if (positivePositions.Length != 1)
        {
            throw new InvalidOperationException(
                "A Sales Product source batch has multiple current inventory positions for one storage location.");
        }

        var position = positivePositions[0];
        if (position.RemainingBalance < quantity)
        {
            return AllocationReservation.Failed(
                Error(ApplicationErrorKind.Conflict, SalesApplicationErrorCodes.InsufficientStock));
        }

        position.RemainingBalance -= quantity;
        return AllocationReservation.Resolved(new PlannedAllocation(
            salesDetailId,
            salesProductId,
            position.Origin,
            position.ProcurementBatchId,
            position.OutsourcedSupplyBatchId,
            position.StorageLocationId,
            position.Id,
            position.RowVersion,
            quantity,
            manualOverride));
    }

    private async ValueTask<IReadOnlyList<SellablePosition>> ReadSellablePositionsAsync(
        IReadOnlyCollection<Guid> salesProductIds,
        CancellationToken cancellationToken)
    {
        if (salesProductIds.Count == 0)
        {
            return Array.Empty<SellablePosition>();
        }

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
              AND p.balance_quantity > 0
              AND p.raw_source_kind IS NULL
              AND p.supplier_id IS NULL
              AND (
                    (p.origin = 'OUTSOURCED'
                     AND ob.lifecycle_status = 'ACTIVE'
                     AND ob.deleted_at IS NULL)
                 OR (p.origin = 'IN_HOUSE'
                     AND pb.lifecycle_status = 'ACTIVE'
                     AND pb.deleted_at IS NULL)
              )
            ORDER BY
                p.sales_product_id,
                CASE WHEN p.origin = 'OUTSOURCED' THEN 0 ELSE 1 END,
                source_date,
                COALESCE(p.outsourced_supply_batch_id, p.procurement_batch_id),
                p.storage_location_id;
            """);
        command.Parameters.AddWithValue("sales_product_ids", salesProductIds.ToArray());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var positions = new List<SellablePosition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            positions.Add(new SellablePosition(
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

        return positions;
    }

    private async ValueTask<bool> ApplyInventoryDeductionsAsync(
        IReadOnlyList<PlannedAllocation> allocations,
        CancellationToken cancellationToken)
    {
        foreach (var deduction in allocations
                     .GroupBy(item => item.InventoryPositionId)
                     .Select(group => new
                     {
                         PositionId = group.Key,
                         ExpectedRowVersion = group.First().InventoryPositionRowVersion,
                         Quantity = group.Sum(item => item.Quantity),
                     }))
        {
            await using var command = CreateSqlCommand(
                """
                UPDATE inventory.inventory_positions
                SET balance_quantity = balance_quantity - @quantity,
                    row_version = row_version + 1
                WHERE id = @position_id
                  AND row_version = @expected_row_version
                  AND balance_quantity >= @quantity;
                """);
            command.Parameters.AddWithValue("quantity", deduction.Quantity);
            command.Parameters.AddWithValue("position_id", deduction.PositionId);
            command.Parameters.AddWithValue("expected_row_version", deduction.ExpectedRowVersion);

            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                return false;
            }
        }

        return true;
    }

    private async ValueTask<CommandAcquisition> AcquireCommandAsync(
        ConfirmSalesExecution execution,
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
            throw new InvalidOperationException("CommandExecution conflict was observed but the stored command could not be loaded.");
        }

        if (!string.Equals(reader.GetString(0), CommandType, StringComparison.Ordinal)
            || reader.GetGuid(3) != execution.ActorAccountId.Value
            || !execution.RequestHash.Bytes.Span.SequenceEqual(reader.GetFieldValue<byte[]>(1)))
        {
            return CommandAcquisition.Conflict();
        }

        if (!string.Equals(reader.GetString(2), "SUCCEEDED", StringComparison.Ordinal) || reader.IsDBNull(4))
        {
            throw new InvalidOperationException(
                "A committed ConfirmSales CommandExecution with the same identity is not replayable.");
        }

        var replay = JsonSerializer.Deserialize<ConfirmSalesResult>(reader.GetString(4), StoredJsonOptions)
            ?? throw new InvalidOperationException("Stored ConfirmSales result payload could not be deserialized.");
        return CommandAcquisition.Replay(replay);
    }

    private async ValueTask AppendAuditAsync(
        ConfirmSalesExecution execution,
        Guid salesId,
        long beforeRowVersion,
        long afterRowVersion,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var auditEventId = Guid.CreateVersion7();
        var subjectKeyJson = JsonSerializer.Serialize(new { id = salesId }, StoredJsonOptions);

        await using (var auditEvent = CreateSqlCommand(
            """
            INSERT INTO audit.audit_events
                (id, command_id, command_type, event_kind, actor_account_id, occurred_at, reason_text)
            VALUES
                (@id, @command_id, @command_type, 'BUSINESS_COMMAND', @actor_account_id, @occurred_at, NULL);
            """))
        {
            auditEvent.Parameters.AddWithValue("id", auditEventId);
            auditEvent.Parameters.AddWithValue("command_id", execution.CommandId.Value);
            auditEvent.Parameters.AddWithValue("command_type", CommandType);
            auditEvent.Parameters.AddWithValue("actor_account_id", execution.ActorAccountId.Value);
            auditEvent.Parameters.AddWithValue("occurred_at", occurredAt);
            await auditEvent.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var subject = CreateSqlCommand(
            """
            INSERT INTO audit.audit_event_subjects
                (audit_event_id, sequence, subject_kind, subject_key, change_kind,
                 before_row_version, after_row_version, change_summary)
            VALUES
                (@audit_event_id, 1, 'sales.sale', CAST(@subject_key AS jsonb), 'UPDATE',
                 @before_row_version, @after_row_version, NULL);
            """);
        subject.Parameters.AddWithValue("audit_event_id", auditEventId);
        subject.Parameters.AddWithValue("subject_key", subjectKeyJson);
        subject.Parameters.AddWithValue("before_row_version", beforeRowVersion);
        subject.Parameters.AddWithValue("after_row_version", afterRowVersion);
        await subject.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask EnqueueOutboxAsync(
        ConfirmSalesExecution execution,
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
        command.Parameters.AddWithValue("command_id", execution.CommandId.Value);
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
            throw new InvalidOperationException("CommandExecution could not be transitioned from IN_PROGRESS to SUCCEEDED.");
        }
    }

    private NpgsqlCommand CreateSqlCommand(string sql)
    {
        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("ConfirmSales SQL requires the active command transaction.");

        return new NpgsqlCommand(
            sql,
            (NpgsqlConnection)_dbContext.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction());
    }

    private static CommandTransactionDecision<ApplicationResult<ConfirmSalesResult>> RollbackFailure(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<ConfirmSalesResult>>.Rollback(Failure(kind, code));

    private static ApplicationResult<ConfirmSalesResult> Failure(ApplicationErrorKind kind, string code) =>
        ApplicationResult<ConfirmSalesResult>.Failure(Error(kind, code));

    private static ApplicationError Error(ApplicationErrorKind kind, string code) =>
        ApplicationError.Create(kind, code);

    private sealed class SellablePosition
    {
        public SellablePosition(
            Guid id,
            Guid salesProductId,
            InventoryOrigin origin,
            Guid? procurementBatchId,
            Guid? outsourcedSupplyBatchId,
            Guid storageLocationId,
            decimal balanceQuantity,
            long rowVersion,
            DateOnly sourceDate)
        {
            Id = id;
            SalesProductId = salesProductId;
            Origin = origin;
            ProcurementBatchId = procurementBatchId;
            OutsourcedSupplyBatchId = outsourcedSupplyBatchId;
            StorageLocationId = storageLocationId;
            RemainingBalance = balanceQuantity;
            RowVersion = rowVersion;
            SourceDate = sourceDate;
        }

        public Guid Id { get; }
        public Guid SalesProductId { get; }
        public InventoryOrigin Origin { get; }
        public Guid? ProcurementBatchId { get; }
        public Guid? OutsourcedSupplyBatchId { get; }
        public Guid StorageLocationId { get; }
        public decimal RemainingBalance { get; set; }
        public long RowVersion { get; }
        public DateOnly SourceDate { get; }
    }

    private sealed record SourceKey(Guid SalesProductId, InventoryOrigin Origin, Guid BatchId);
    private sealed record SourceGroup(SourceKey Key, DateOnly SourceDate, IReadOnlyList<SellablePosition> Positions);

    private sealed record PlannedAllocation(
        Guid SalesDetailId,
        Guid SalesProductId,
        InventoryOrigin Origin,
        Guid? ProcurementBatchId,
        Guid? OutsourcedSupplyBatchId,
        Guid StorageLocationId,
        Guid InventoryPositionId,
        long InventoryPositionRowVersion,
        decimal Quantity,
        bool ManualOverride);

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

    private sealed record CommandAcquisition(CommandAcquisitionKind Kind, ConfirmSalesResult? ReplayResult)
    {
        public static CommandAcquisition Acquired() => new(CommandAcquisitionKind.Acquired, null);
        public static CommandAcquisition Replay(ConfirmSalesResult result) => new(CommandAcquisitionKind.Replay, result);
        public static CommandAcquisition Conflict() => new(CommandAcquisitionKind.Conflict, null);
    }
}