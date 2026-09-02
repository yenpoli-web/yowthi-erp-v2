using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Procurement;
using YowThi.Erp.Domain.Finance;
using YowThi.Erp.Domain.Infrastructure;
using YowThi.Erp.Domain.Inventory;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.Procurement;
using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Infrastructure.Persistence.Procurement;

internal sealed class PostgreSqlConfirmProcurementEntryExecutor : IConfirmProcurementEntryExecutor
{
    private const string CommandType = "ConfirmProcurementEntry";
    private const string OutboxMessageType = "procurement.entry.confirmed";

    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlConfirmProcurementEntryExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public async ValueTask<ApplicationResult<ConfirmProcurementEntryResult>> ExecuteAsync(
        ConfirmProcurementEntryExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);

        var validationError = ConfirmProcurementEntryValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ApplicationResult<ConfirmProcurementEntryResult>.Failure(validationError);
        }

        if (!ProcurementEntryAmount.TryCalculate(
                execution.Command.NetQuantity,
                execution.Command.UnitPrice,
                out var amountThb))
        {
            return Failure(ApplicationErrorKind.Validation, ProcurementApplicationErrorCodes.AmountOutOfRange);
        }

        return await _transactionRunner.ExecuteAsync(
            async operationCancellationToken =>
            {
                var startedAt = _timeProvider.GetUtcNow();
                var acquisition = await AcquireCommandAsync(execution, startedAt, operationCancellationToken);

                if (acquisition.Kind == CommandAcquisitionKind.Replay)
                {
                    return CommandTransactionDecision<ApplicationResult<ConfirmProcurementEntryResult>>.Rollback(
                        ApplicationResult<ConfirmProcurementEntryResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == CommandAcquisitionKind.Conflict)
                {
                    return CommandTransactionDecision<ApplicationResult<ConfirmProcurementEntryResult>>.Rollback(
                        Failure(ApplicationErrorKind.Conflict, ProcurementApplicationErrorCodes.IdempotencyKeyReused));
                }

                var command = execution.Command;

                var product = await _dbContext.Set<ProcurementProduct>()
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == command.ProcurementProductId, operationCancellationToken);

                if (product is null)
                {
                    return RollbackFailure(ApplicationErrorKind.NotFound, ProcurementApplicationErrorCodes.ProductNotFound);
                }

                if (!product.Active || product.DeletedAt is not null)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, ProcurementApplicationErrorCodes.ProductInactive);
                }

                var sourceError = await ValidateSourceAsync(command, operationCancellationToken);
                if (sourceError is not null)
                {
                    return CommandTransactionDecision<ApplicationResult<ConfirmProcurementEntryResult>>.Rollback(
                        ApplicationResult<ConfirmProcurementEntryResult>.Failure(sourceError));
                }

                var receiptLocationResolution = await ResolveReceiptLocationAsync(
                    command.ReceiptStorageLocationId,
                    product.DefaultStorageLocationId,
                    operationCancellationToken);

                if (receiptLocationResolution.Error is not null)
                {
                    return CommandTransactionDecision<ApplicationResult<ConfirmProcurementEntryResult>>.Rollback(
                        ApplicationResult<ConfirmProcurementEntryResult>.Failure(receiptLocationResolution.Error));
                }

                var batch = await ResolveAndLockBatchAsync(
                    command.ProcurementDate,
                    command.ProcurementProductId,
                    execution.ActorAccountId.Value,
                    startedAt,
                    operationCancellationToken);

                if (batch.ProcurementStatus == ProcurementStatus.COMPLETED)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, ProcurementApplicationErrorCodes.BatchCompleted);
                }

                if (batch.LifecycleStatus != ProcurementBatchLifecycleStatus.ACTIVE || batch.DeletedAt is not null)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, ProcurementApplicationErrorCodes.BatchUnavailable);
                }

                var payableKind = command.SourceType == ProcurementSourceType.SUPPLIER
                    ? PayableKind.PROCUREMENT_SUPPLIER
                    : PayableKind.PROCUREMENT_FARMER;

                var payableId = await ResolvePayableAsync(
                    batch.Id,
                    command.SourceType,
                    command.SupplierId,
                    command.FarmerId,
                    startedAt,
                    operationCancellationToken);

                var procurementEntryId = Guid.CreateVersion7();
                var inventoryOperationId = Guid.CreateVersion7();
                var inventoryMovementId = Guid.CreateVersion7();
                var payableObligationItemId = Guid.CreateVersion7();
                var transportBasisId = command.CompanyPickup ? Guid.CreateVersion7() : (Guid?)null;

                var entry = ProcurementEntry.Create(
                    procurementEntryId,
                    batch.Id,
                    command.SourceType,
                    command.SupplierId,
                    command.FarmerId,
                    command.NetQuantity,
                    product.UnitCode,
                    command.UnitPrice,
                    amountThb,
                    command.CompanyPickup,
                    startedAt,
                    execution.ActorAccountId.Value);

                var inventoryOperation = InventoryOperation.CreateProcurementReceipt(
                    inventoryOperationId,
                    procurementEntryId,
                    startedAt,
                    execution.ActorAccountId.Value);

                var rawSourceKind = command.SourceType == ProcurementSourceType.SUPPLIER
                    ? InventoryRawSourceKind.SUPPLIER
                    : InventoryRawSourceKind.FARMERS_COMBINED;

                var inventoryMovement = InventoryMovement.CreateProcurementReceipt(
                    inventoryMovementId,
                    inventoryOperationId,
                    batch.Id,
                    command.ProcurementProductId,
                    receiptLocationResolution.StorageLocationId,
                    rawSourceKind,
                    command.SourceType == ProcurementSourceType.SUPPLIER ? command.SupplierId : null,
                    command.NetQuantity,
                    startedAt);

                var obligation = PayableObligationItem.CreateProcurementEntry(
                    payableObligationItemId,
                    payableId,
                    payableKind,
                    procurementEntryId,
                    amountThb,
                    startedAt);

                _dbContext.AddRange(entry, inventoryOperation, inventoryMovement, obligation);

                if (transportBasisId is Guid basisId)
                {
                    _dbContext.Add(CompanyPickupTransportBasis.Create(
                        basisId,
                        procurementEntryId,
                        command.NetQuantity,
                        startedAt));
                }

                await _dbContext.SaveChangesAsync(operationCancellationToken);

                await UpsertInventoryPositionAsync(
                    batch.Id,
                    command.ProcurementProductId,
                    receiptLocationResolution.StorageLocationId,
                    command.SourceType,
                    command.SupplierId,
                    command.NetQuantity,
                    operationCancellationToken);

                await UpsertPayableOutstandingAsync(
                    payableId,
                    amountThb,
                    startedAt,
                    operationCancellationToken);

                var result = new ConfirmProcurementEntryResult(
                    procurementEntryId,
                    batch.Id,
                    inventoryOperationId,
                    payableId,
                    transportBasisId,
                    receiptLocationResolution.StorageLocationId,
                    amountThb,
                    entry.RowVersion);

                var storedResultJson = JsonSerializer.Serialize(result, StoredJsonOptions);

                await AppendAuditAsync(
                    execution,
                    procurementEntryId,
                    entry.RowVersion,
                    startedAt,
                    operationCancellationToken);

                await EnqueueOutboxAsync(
                    execution,
                    storedResultJson,
                    startedAt,
                    operationCancellationToken);

                await MarkCommandSucceededAsync(
                    execution.CommandId.Value,
                    storedResultJson,
                    startedAt,
                    operationCancellationToken);

                return CommandTransactionDecision<ApplicationResult<ConfirmProcurementEntryResult>>.Commit(
                    ApplicationResult<ConfirmProcurementEntryResult>.Success(result));
            },
            cancellationToken);
    }

    private async ValueTask<ApplicationError?> ValidateSourceAsync(
        ConfirmProcurementEntryCommand command,
        CancellationToken cancellationToken)
    {
        if (command.SourceType == ProcurementSourceType.SUPPLIER)
        {
            var supplier = await _dbContext.Set<Supplier>()
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == command.SupplierId!.Value, cancellationToken);

            if (supplier is null)
            {
                return Error(ApplicationErrorKind.NotFound, ProcurementApplicationErrorCodes.SourceNotFound);
            }

            if (!supplier.Active || supplier.DeletedAt is not null)
            {
                return Error(ApplicationErrorKind.Conflict, ProcurementApplicationErrorCodes.SourceInactive);
            }

            return null;
        }

        var farmer = await _dbContext.Set<Farmer>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == command.FarmerId!.Value, cancellationToken);

        if (farmer is null)
        {
            return Error(ApplicationErrorKind.NotFound, ProcurementApplicationErrorCodes.SourceNotFound);
        }

        if (!farmer.Active || farmer.DeletedAt is not null)
        {
            return Error(ApplicationErrorKind.Conflict, ProcurementApplicationErrorCodes.SourceInactive);
        }

        return null;
    }

    private async ValueTask<ReceiptLocationResolution> ResolveReceiptLocationAsync(
        Guid? requestedStorageLocationId,
        Guid? productDefaultStorageLocationId,
        CancellationToken cancellationToken)
    {
        if (requestedStorageLocationId is Guid requestedId)
        {
            var requested = await _dbContext.Set<StorageLocation>()
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == requestedId, cancellationToken);

            if (requested is null)
            {
                return ReceiptLocationResolution.Failed(
                    Error(ApplicationErrorKind.NotFound, ProcurementApplicationErrorCodes.ReceiptLocationNotFound));
            }

            if (!requested.Active || requested.DeletedAt is not null)
            {
                return ReceiptLocationResolution.Failed(
                    Error(ApplicationErrorKind.Conflict, ProcurementApplicationErrorCodes.ReceiptLocationInactive));
            }

            return ReceiptLocationResolution.Resolved(requested.Id);
        }

        if (productDefaultStorageLocationId is not Guid defaultId)
        {
            return ReceiptLocationResolution.Failed(
                Error(ApplicationErrorKind.Validation, ProcurementApplicationErrorCodes.ReceiptLocationRequired));
        }

        var defaultLocation = await _dbContext.Set<StorageLocation>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == defaultId, cancellationToken);

        if (defaultLocation is null || !defaultLocation.Active || defaultLocation.DeletedAt is not null)
        {
            return ReceiptLocationResolution.Failed(
                Error(ApplicationErrorKind.Validation, ProcurementApplicationErrorCodes.ReceiptLocationRequired));
        }

        return ReceiptLocationResolution.Resolved(defaultLocation.Id);
    }

    private async ValueTask<BatchState> ResolveAndLockBatchAsync(
        DateOnly procurementDate,
        Guid procurementProductId,
        Guid actorAccountId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var candidateBatchId = Guid.CreateVersion7();

        await using (var insert = CreateSqlCommand(
            """
            INSERT INTO procurement.procurement_batches
                (id, procurement_date, procurement_product_id, procurement_status,
                 lifecycle_status, processing_route_id, processing_route_version_id,
                 created_at, created_by_account_id)
            VALUES
                (@id, @procurement_date, @product_id, 'OPEN',
                 'ACTIVE', NULL, NULL, @created_at, @created_by)
            ON CONFLICT (procurement_date, procurement_product_id) DO NOTHING;
            """))
        {
            insert.Parameters.AddWithValue("id", candidateBatchId);
            insert.Parameters.AddWithValue("procurement_date", procurementDate);
            insert.Parameters.AddWithValue("product_id", procurementProductId);
            insert.Parameters.AddWithValue("created_at", now);
            insert.Parameters.AddWithValue("created_by", actorAccountId);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var select = CreateSqlCommand(
            """
            SELECT id, procurement_status, lifecycle_status, deleted_at
            FROM procurement.procurement_batches
            WHERE procurement_date = @procurement_date
              AND procurement_product_id = @product_id
            FOR UPDATE;
            """);
        select.Parameters.AddWithValue("procurement_date", procurementDate);
        select.Parameters.AddWithValue("product_id", procurementProductId);

        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Procurement Batch resolution did not return the inserted or existing business-identity row.");
        }

        return new BatchState(
            reader.GetGuid(0),
            Enum.Parse<ProcurementStatus>(reader.GetString(1), ignoreCase: false),
            Enum.Parse<ProcurementBatchLifecycleStatus>(reader.GetString(2), ignoreCase: false),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3));
    }

    private async ValueTask<Guid> ResolvePayableAsync(
        Guid procurementBatchId,
        ProcurementSourceType sourceType,
        Guid? supplierId,
        Guid? farmerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var candidatePayableId = Guid.CreateVersion7();

        if (sourceType == ProcurementSourceType.SUPPLIER)
        {
            await using (var insert = CreateSqlCommand(
                """
                INSERT INTO finance.payables
                    (id, payable_kind, procurement_batch_id, supplier_id, farmer_id,
                     outsourced_supply_detail_id, employee_daily_wage_id, created_at)
                VALUES
                    (@id, 'PROCUREMENT_SUPPLIER', @batch_id, @supplier_id, NULL, NULL, NULL, @created_at)
                ON CONFLICT (procurement_batch_id, supplier_id)
                    WHERE payable_kind = 'PROCUREMENT_SUPPLIER'
                DO NOTHING;
                """))
            {
                insert.Parameters.AddWithValue("id", candidatePayableId);
                insert.Parameters.AddWithValue("batch_id", procurementBatchId);
                insert.Parameters.AddWithValue("supplier_id", supplierId!.Value);
                insert.Parameters.AddWithValue("created_at", now);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var select = CreateSqlCommand(
                """
                SELECT id
                FROM finance.payables
                WHERE payable_kind = 'PROCUREMENT_SUPPLIER'
                  AND procurement_batch_id = @batch_id
                  AND supplier_id = @supplier_id;
                """);
            select.Parameters.AddWithValue("batch_id", procurementBatchId);
            select.Parameters.AddWithValue("supplier_id", supplierId!.Value);
            return (Guid)(await select.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Supplier Procurement Payable resolution failed."));
        }

        await using (var insert = CreateSqlCommand(
            """
            INSERT INTO finance.payables
                (id, payable_kind, procurement_batch_id, supplier_id, farmer_id,
                 outsourced_supply_detail_id, employee_daily_wage_id, created_at)
            VALUES
                (@id, 'PROCUREMENT_FARMER', @batch_id, NULL, @farmer_id, NULL, NULL, @created_at)
            ON CONFLICT (procurement_batch_id, farmer_id)
                WHERE payable_kind = 'PROCUREMENT_FARMER'
            DO NOTHING;
            """))
        {
            insert.Parameters.AddWithValue("id", candidatePayableId);
            insert.Parameters.AddWithValue("batch_id", procurementBatchId);
            insert.Parameters.AddWithValue("farmer_id", farmerId!.Value);
            insert.Parameters.AddWithValue("created_at", now);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var select = CreateSqlCommand(
            """
            SELECT id
            FROM finance.payables
            WHERE payable_kind = 'PROCUREMENT_FARMER'
              AND procurement_batch_id = @batch_id
              AND farmer_id = @farmer_id;
            """))
        {
            select.Parameters.AddWithValue("batch_id", procurementBatchId);
            select.Parameters.AddWithValue("farmer_id", farmerId!.Value);
            return (Guid)(await select.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Farmer Procurement Payable resolution failed."));
        }
    }

    private async ValueTask UpsertInventoryPositionAsync(
        Guid procurementBatchId,
        Guid procurementProductId,
        Guid storageLocationId,
        ProcurementSourceType sourceType,
        Guid? supplierId,
        decimal quantity,
        CancellationToken cancellationToken)
    {
        var positionId = Guid.CreateVersion7();

        if (sourceType == ProcurementSourceType.SUPPLIER)
        {
            await using var command = CreateSqlCommand(
                """
                INSERT INTO inventory.inventory_positions
                    (id, origin, procurement_batch_id, outsourced_supply_batch_id,
                     inventory_object_kind, procurement_product_id, process_material_id,
                     sales_product_id, storage_location_id, raw_source_kind, supplier_id,
                     balance_quantity, row_version)
                VALUES
                    (@id, 'IN_HOUSE', @batch_id, NULL,
                     'PROCUREMENT_PRODUCT', @product_id, NULL,
                     NULL, @storage_location_id, 'SUPPLIER', @supplier_id,
                     @quantity, 1)
                ON CONFLICT
                    (origin, procurement_batch_id, outsourced_supply_batch_id,
                     inventory_object_kind, procurement_product_id, process_material_id,
                     sales_product_id, storage_location_id, raw_source_kind, supplier_id)
                DO UPDATE SET
                    balance_quantity = inventory.inventory_positions.balance_quantity + EXCLUDED.balance_quantity,
                    row_version = inventory.inventory_positions.row_version + 1;
                """);
            command.Parameters.AddWithValue("id", positionId);
            command.Parameters.AddWithValue("batch_id", procurementBatchId);
            command.Parameters.AddWithValue("product_id", procurementProductId);
            command.Parameters.AddWithValue("storage_location_id", storageLocationId);
            command.Parameters.AddWithValue("supplier_id", supplierId!.Value);
            command.Parameters.AddWithValue("quantity", quantity);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        await using (var command = CreateSqlCommand(
            """
            INSERT INTO inventory.inventory_positions
                (id, origin, procurement_batch_id, outsourced_supply_batch_id,
                 inventory_object_kind, procurement_product_id, process_material_id,
                 sales_product_id, storage_location_id, raw_source_kind, supplier_id,
                 balance_quantity, row_version)
            VALUES
                (@id, 'IN_HOUSE', @batch_id, NULL,
                 'PROCUREMENT_PRODUCT', @product_id, NULL,
                 NULL, @storage_location_id, 'FARMERS_COMBINED', NULL,
                 @quantity, 1)
            ON CONFLICT
                (origin, procurement_batch_id, outsourced_supply_batch_id,
                 inventory_object_kind, procurement_product_id, process_material_id,
                 sales_product_id, storage_location_id, raw_source_kind, supplier_id)
            DO UPDATE SET
                balance_quantity = inventory.inventory_positions.balance_quantity + EXCLUDED.balance_quantity,
                row_version = inventory.inventory_positions.row_version + 1;
            """))
        {
            command.Parameters.AddWithValue("id", positionId);
            command.Parameters.AddWithValue("batch_id", procurementBatchId);
            command.Parameters.AddWithValue("product_id", procurementProductId);
            command.Parameters.AddWithValue("storage_location_id", storageLocationId);
            command.Parameters.AddWithValue("quantity", quantity);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async ValueTask UpsertPayableOutstandingAsync(
        Guid payableId,
        long amountThb,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            INSERT INTO finance.payable_outstanding_positions
                (payable_id, original_obligation_thb, adjustment_total_thb,
                 settlement_total_thb, outstanding_thb, row_version, updated_at)
            VALUES
                (@payable_id, @amount_thb, 0, 0, @amount_thb, 1, @updated_at)
            ON CONFLICT (payable_id) DO UPDATE SET
                original_obligation_thb = finance.payable_outstanding_positions.original_obligation_thb + EXCLUDED.original_obligation_thb,
                outstanding_thb = finance.payable_outstanding_positions.outstanding_thb + EXCLUDED.original_obligation_thb,
                row_version = finance.payable_outstanding_positions.row_version + 1,
                updated_at = EXCLUDED.updated_at;
            """);
        command.Parameters.AddWithValue("payable_id", payableId);
        command.Parameters.AddWithValue("amount_thb", amountThb);
        command.Parameters.AddWithValue("updated_at", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask<CommandAcquisition> AcquireCommandAsync(
        ConfirmProcurementEntryExecution execution,
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

        var commandType = reader.GetString(0);
        var requestHash = reader.GetFieldValue<byte[]>(1);
        var status = reader.GetString(2);
        var actorAccountId = reader.GetGuid(3);

        if (!string.Equals(commandType, CommandType, StringComparison.Ordinal)
            || actorAccountId != execution.ActorAccountId.Value
            || !execution.RequestHash.Bytes.Span.SequenceEqual(requestHash))
        {
            return CommandAcquisition.Conflict();
        }

        if (!string.Equals(status, "SUCCEEDED", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A committed CommandExecution with the same identity is not in SUCCEEDED state. Manual investigation is required.");
        }

        if (reader.IsDBNull(4))
        {
            throw new InvalidOperationException("A SUCCEEDED CommandExecution is missing its result payload.");
        }

        var replay = JsonSerializer.Deserialize<ConfirmProcurementEntryResult>(reader.GetString(4), StoredJsonOptions)
            ?? throw new InvalidOperationException("Stored ConfirmProcurementEntry result payload could not be deserialized.");

        return CommandAcquisition.Replay(replay);
    }

    private async ValueTask AppendAuditAsync(
        ConfirmProcurementEntryExecution execution,
        Guid procurementEntryId,
        long afterRowVersion,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var auditEventId = Guid.CreateVersion7();
        var subjectKeyJson = JsonSerializer.Serialize(new { id = procurementEntryId }, StoredJsonOptions);

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

        await using (var subject = CreateSqlCommand(
            """
            INSERT INTO audit.audit_event_subjects
                (audit_event_id, sequence, subject_kind, subject_key, change_kind,
                 before_row_version, after_row_version, change_summary)
            VALUES
                (@audit_event_id, 1, 'procurement.entry', CAST(@subject_key AS jsonb), 'CREATE',
                 NULL, @after_row_version, NULL);
            """))
        {
            subject.Parameters.AddWithValue("audit_event_id", auditEventId);
            subject.Parameters.AddWithValue("subject_key", subjectKeyJson);
            subject.Parameters.AddWithValue("after_row_version", afterRowVersion);
            await subject.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async ValueTask EnqueueOutboxAsync(
        ConfirmProcurementEntryExecution execution,
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
            ?? throw new InvalidOperationException("ConfirmProcurementEntry SQL requires the active command transaction.");

        return new NpgsqlCommand(
            sql,
            (NpgsqlConnection)_dbContext.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction());
    }

    private static CommandTransactionDecision<ApplicationResult<ConfirmProcurementEntryResult>> RollbackFailure(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<ConfirmProcurementEntryResult>>.Rollback(Failure(kind, code));

    private static ApplicationResult<ConfirmProcurementEntryResult> Failure(
        ApplicationErrorKind kind,
        string code) =>
        ApplicationResult<ConfirmProcurementEntryResult>.Failure(Error(kind, code));

    private static ApplicationError Error(ApplicationErrorKind kind, string code) =>
        ApplicationError.Create(kind, code);

    private sealed record ReceiptLocationResolution(Guid StorageLocationId, ApplicationError? Error)
    {
        public static ReceiptLocationResolution Resolved(Guid storageLocationId) => new(storageLocationId, null);
        public static ReceiptLocationResolution Failed(ApplicationError error) => new(Guid.Empty, error);
    }

    private sealed record BatchState(
        Guid Id,
        ProcurementStatus ProcurementStatus,
        ProcurementBatchLifecycleStatus LifecycleStatus,
        DateTimeOffset? DeletedAt);

    private enum CommandAcquisitionKind
    {
        Acquired,
        Replay,
        Conflict,
    }

    private sealed record CommandAcquisition(
        CommandAcquisitionKind Kind,
        ConfirmProcurementEntryResult? ReplayResult)
    {
        public static CommandAcquisition Acquired() => new(CommandAcquisitionKind.Acquired, null);
        public static CommandAcquisition Replay(ConfirmProcurementEntryResult result) => new(CommandAcquisitionKind.Replay, result);
        public static CommandAcquisition Conflict() => new(CommandAcquisitionKind.Conflict, null);
    }
}
