using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Outsourced;
using YowThi.Erp.Domain.Finance;
using YowThi.Erp.Domain.Infrastructure;
using YowThi.Erp.Domain.Inventory;
using YowThi.Erp.Domain.Outsourced;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Infrastructure.Persistence.Outsourced;

internal sealed class PostgreSqlConfirmOutsourcedSupplyDetailExecutor : IConfirmOutsourcedSupplyDetailExecutor
{
    private const string CommandType = "ConfirmOutsourcedSupplyDetail";
    private const string OutboxMessageType = "outsourced.supply-detail.confirmed";

    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlConfirmOutsourcedSupplyDetailExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public async ValueTask<ApplicationResult<ConfirmOutsourcedSupplyDetailResult>> ExecuteAsync(
        ConfirmOutsourcedSupplyDetailExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);

        var validationError = ConfirmOutsourcedSupplyDetailValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ApplicationResult<ConfirmOutsourcedSupplyDetailResult>.Failure(validationError);
        }

        return await _transactionRunner.ExecuteAsync(
            async operationCancellationToken =>
            {
                var startedAt = _timeProvider.GetUtcNow();
                var acquisition = await AcquireCommandAsync(execution, startedAt, operationCancellationToken);

                if (acquisition.Kind == CommandAcquisitionKind.Replay)
                {
                    return CommandTransactionDecision<ApplicationResult<ConfirmOutsourcedSupplyDetailResult>>.Rollback(
                        ApplicationResult<ConfirmOutsourcedSupplyDetailResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == CommandAcquisitionKind.Conflict)
                {
                    return CommandTransactionDecision<ApplicationResult<ConfirmOutsourcedSupplyDetailResult>>.Rollback(
                        Failure(ApplicationErrorKind.Conflict, OutsourcedApplicationErrorCodes.IdempotencyKeyReused));
                }

                var command = execution.Command;

                var vendor = await _dbContext.Set<OutsourcedVendor>()
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == command.OutsourcedVendorId, operationCancellationToken);

                if (vendor is null)
                {
                    return RollbackFailure(ApplicationErrorKind.NotFound, OutsourcedApplicationErrorCodes.VendorNotFound);
                }

                if (!vendor.Active || vendor.DeletedAt is not null)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, OutsourcedApplicationErrorCodes.VendorInactive);
                }

                var product = await _dbContext.Set<SalesProduct>()
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == command.SalesProductId, operationCancellationToken);

                if (product is null)
                {
                    return RollbackFailure(ApplicationErrorKind.NotFound, OutsourcedApplicationErrorCodes.ProductNotFound);
                }

                if (!product.Active || product.DeletedAt is not null)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, OutsourcedApplicationErrorCodes.ProductInactive);
                }

                if (!OutsourcedSupplyDetailAmount.TryCalculate(
                        command.Quantity,
                        product.PricingBasis,
                        product.SalesWeight,
                        command.UnitPrice,
                        out var amountThb))
                {
                    return RollbackFailure(ApplicationErrorKind.Validation, OutsourcedApplicationErrorCodes.AmountOutOfRange);
                }

                var receiptLocationResolution = await ResolveReceiptLocationAsync(
                    command.ReceiptStorageLocationId,
                    product.DefaultStorageLocationId,
                    operationCancellationToken);

                if (receiptLocationResolution.Error is not null)
                {
                    return CommandTransactionDecision<ApplicationResult<ConfirmOutsourcedSupplyDetailResult>>.Rollback(
                        ApplicationResult<ConfirmOutsourcedSupplyDetailResult>.Failure(receiptLocationResolution.Error));
                }

                var batch = await ResolveAndLockBatchAsync(
                    command.SupplyDate,
                    command.OutsourcedVendorId,
                    execution.ActorAccountId.Value,
                    startedAt,
                    operationCancellationToken);

                // OUT-003 / TO VERIFY: do not silently reopen a closed batch or add new inventory to it.
                if (batch.LifecycleStatus == OutsourcedSupplyBatchLifecycleStatus.CLOSED)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        OutsourcedApplicationErrorCodes.BatchClosedLateDetailUnverified);
                }

                if (batch.DeletedAt is not null)
                {
                    return RollbackFailure(ApplicationErrorKind.Conflict, OutsourcedApplicationErrorCodes.BatchUnavailable);
                }

                var detailId = Guid.CreateVersion7();
                var inventoryOperationId = Guid.CreateVersion7();
                var inventoryMovementId = Guid.CreateVersion7();
                var payableId = Guid.CreateVersion7();
                var payableObligationItemId = Guid.CreateVersion7();

                var detail = OutsourcedSupplyDetail.Create(
                    detailId,
                    batch.Id,
                    command.SalesProductId,
                    command.Quantity,
                    product.PricingBasis,
                    product.SalesWeight,
                    command.UnitPrice,
                    amountThb,
                    startedAt,
                    execution.ActorAccountId.Value);

                var inventoryOperation = InventoryOperation.CreateOutsourcedReceipt(
                    inventoryOperationId,
                    detailId,
                    startedAt,
                    execution.ActorAccountId.Value);

                var inventoryMovement = InventoryMovement.CreateOutsourcedReceipt(
                    inventoryMovementId,
                    inventoryOperationId,
                    batch.Id,
                    command.SalesProductId,
                    receiptLocationResolution.StorageLocationId,
                    command.Quantity,
                    startedAt);

                _dbContext.AddRange(detail, inventoryOperation, inventoryMovement);
                await _dbContext.SaveChangesAsync(operationCancellationToken);

                await InsertPayableAsync(payableId, detailId, startedAt, operationCancellationToken);

                var obligation = PayableObligationItem.CreateOutsourcedSupplyDetail(
                    payableObligationItemId,
                    payableId,
                    detailId,
                    amountThb,
                    startedAt);

                _dbContext.Add(obligation);
                await _dbContext.SaveChangesAsync(operationCancellationToken);

                await UpsertInventoryPositionAsync(
                    batch.Id,
                    command.SalesProductId,
                    receiptLocationResolution.StorageLocationId,
                    command.Quantity,
                    operationCancellationToken);

                await UpsertPayableOutstandingAsync(
                    payableId,
                    amountThb,
                    startedAt,
                    operationCancellationToken);

                var result = new ConfirmOutsourcedSupplyDetailResult(
                    detailId,
                    batch.Id,
                    inventoryOperationId,
                    payableId,
                    receiptLocationResolution.StorageLocationId,
                    amountThb,
                    detail.RowVersion);

                var storedResultJson = JsonSerializer.Serialize(result, StoredJsonOptions);

                await AppendAuditAsync(
                    execution,
                    detailId,
                    detail.RowVersion,
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

                return CommandTransactionDecision<ApplicationResult<ConfirmOutsourcedSupplyDetailResult>>.Commit(
                    ApplicationResult<ConfirmOutsourcedSupplyDetailResult>.Success(result));
            },
            cancellationToken);
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
                    Error(ApplicationErrorKind.NotFound, OutsourcedApplicationErrorCodes.ReceiptLocationNotFound));
            }

            if (!requested.Active || requested.DeletedAt is not null)
            {
                return ReceiptLocationResolution.Failed(
                    Error(ApplicationErrorKind.Conflict, OutsourcedApplicationErrorCodes.ReceiptLocationInactive));
            }

            return ReceiptLocationResolution.Resolved(requested.Id);
        }

        if (productDefaultStorageLocationId is not Guid defaultId)
        {
            return ReceiptLocationResolution.Failed(
                Error(ApplicationErrorKind.Validation, OutsourcedApplicationErrorCodes.ReceiptLocationRequired));
        }

        var defaultLocation = await _dbContext.Set<StorageLocation>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == defaultId, cancellationToken);

        if (defaultLocation is null || !defaultLocation.Active || defaultLocation.DeletedAt is not null)
        {
            return ReceiptLocationResolution.Failed(
                Error(ApplicationErrorKind.Validation, OutsourcedApplicationErrorCodes.ReceiptLocationRequired));
        }

        return ReceiptLocationResolution.Resolved(defaultLocation.Id);
    }

    private async ValueTask<BatchState> ResolveAndLockBatchAsync(
        DateOnly supplyDate,
        Guid outsourcedVendorId,
        Guid actorAccountId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var candidateBatchId = Guid.CreateVersion7();

        await using (var insert = CreateSqlCommand(
            """
            INSERT INTO outsourced.outsourced_supply_batches
                (id, supply_date, outsourced_vendor_id, lifecycle_status,
                 closed_at, closed_by_account_id, created_at, created_by_account_id,
                 deleted_at, deleted_by_account_id)
            VALUES
                (@id, @supply_date, @vendor_id, 'ACTIVE',
                 NULL, NULL, @created_at, @created_by,
                 NULL, NULL)
            ON CONFLICT (supply_date, outsourced_vendor_id) DO NOTHING;
            """))
        {
            insert.Parameters.AddWithValue("id", candidateBatchId);
            insert.Parameters.AddWithValue("supply_date", supplyDate);
            insert.Parameters.AddWithValue("vendor_id", outsourcedVendorId);
            insert.Parameters.AddWithValue("created_at", now);
            insert.Parameters.AddWithValue("created_by", actorAccountId);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var select = CreateSqlCommand(
            """
            SELECT id, lifecycle_status, deleted_at
            FROM outsourced.outsourced_supply_batches
            WHERE supply_date = @supply_date
              AND outsourced_vendor_id = @vendor_id
            FOR UPDATE;
            """);
        select.Parameters.AddWithValue("supply_date", supplyDate);
        select.Parameters.AddWithValue("vendor_id", outsourcedVendorId);

        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "Outsourced Supply Batch resolution did not return the inserted or existing business-identity row.");
        }

        return new BatchState(
            reader.GetGuid(0),
            Enum.Parse<OutsourcedSupplyBatchLifecycleStatus>(reader.GetString(1), ignoreCase: false),
            reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2));
    }

    private async ValueTask InsertPayableAsync(
        Guid payableId,
        Guid outsourcedSupplyDetailId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            INSERT INTO finance.payables
                (id, payable_kind, procurement_batch_id, supplier_id, farmer_id,
                 outsourced_supply_detail_id, employee_daily_wage_id, created_at)
            VALUES
                (@id, 'OUTSOURCED_VENDOR', NULL, NULL, NULL,
                 @detail_id, NULL, @created_at);
            """);
        command.Parameters.AddWithValue("id", payableId);
        command.Parameters.AddWithValue("detail_id", outsourcedSupplyDetailId);
        command.Parameters.AddWithValue("created_at", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask UpsertInventoryPositionAsync(
        Guid outsourcedSupplyBatchId,
        Guid salesProductId,
        Guid storageLocationId,
        decimal quantity,
        CancellationToken cancellationToken)
    {
        await using var command = CreateSqlCommand(
            """
            INSERT INTO inventory.inventory_positions
                (id, origin, procurement_batch_id, outsourced_supply_batch_id,
                 inventory_object_kind, procurement_product_id, process_material_id,
                 sales_product_id, storage_location_id, raw_source_kind, supplier_id,
                 balance_quantity, row_version)
            VALUES
                (@id, 'OUTSOURCED', NULL, @batch_id,
                 'SALES_PRODUCT', NULL, NULL,
                 @sales_product_id, @storage_location_id, NULL, NULL,
                 @quantity, 1)
            ON CONFLICT
                (origin, procurement_batch_id, outsourced_supply_batch_id,
                 inventory_object_kind, procurement_product_id, process_material_id,
                 sales_product_id, storage_location_id, raw_source_kind, supplier_id)
            DO UPDATE SET
                balance_quantity = inventory.inventory_positions.balance_quantity + EXCLUDED.balance_quantity,
                row_version = inventory.inventory_positions.row_version + 1;
            """);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("batch_id", outsourcedSupplyBatchId);
        command.Parameters.AddWithValue("sales_product_id", salesProductId);
        command.Parameters.AddWithValue("storage_location_id", storageLocationId);
        command.Parameters.AddWithValue("quantity", quantity);
        await command.ExecuteNonQueryAsync(cancellationToken);
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
        ConfirmOutsourcedSupplyDetailExecution execution,
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
            throw new InvalidOperationException(
                "CommandExecution conflict was observed but the stored command could not be loaded.");
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

        var replay = JsonSerializer.Deserialize<ConfirmOutsourcedSupplyDetailResult>(reader.GetString(4), StoredJsonOptions)
            ?? throw new InvalidOperationException(
                "Stored ConfirmOutsourcedSupplyDetail result payload could not be deserialized.");

        return CommandAcquisition.Replay(replay);
    }

    private async ValueTask AppendAuditAsync(
        ConfirmOutsourcedSupplyDetailExecution execution,
        Guid outsourcedSupplyDetailId,
        long afterRowVersion,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var auditEventId = Guid.CreateVersion7();
        var subjectKeyJson = JsonSerializer.Serialize(new { id = outsourcedSupplyDetailId }, StoredJsonOptions);

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
                (@audit_event_id, 1, 'outsourced.supply-detail', CAST(@subject_key AS jsonb), 'CREATE',
                 NULL, @after_row_version, NULL);
            """);
        subject.Parameters.AddWithValue("audit_event_id", auditEventId);
        subject.Parameters.AddWithValue("subject_key", subjectKeyJson);
        subject.Parameters.AddWithValue("after_row_version", afterRowVersion);
        await subject.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask EnqueueOutboxAsync(
        ConfirmOutsourcedSupplyDetailExecution execution,
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
            throw new InvalidOperationException(
                "CommandExecution could not be transitioned from IN_PROGRESS to SUCCEEDED.");
        }
    }

    private NpgsqlCommand CreateSqlCommand(string sql)
    {
        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "ConfirmOutsourcedSupplyDetail SQL requires the active command transaction.");

        return new NpgsqlCommand(
            sql,
            (NpgsqlConnection)_dbContext.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction());
    }

    private static CommandTransactionDecision<ApplicationResult<ConfirmOutsourcedSupplyDetailResult>> RollbackFailure(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<ConfirmOutsourcedSupplyDetailResult>>.Rollback(Failure(kind, code));

    private static ApplicationResult<ConfirmOutsourcedSupplyDetailResult> Failure(
        ApplicationErrorKind kind,
        string code) =>
        ApplicationResult<ConfirmOutsourcedSupplyDetailResult>.Failure(Error(kind, code));

    private static ApplicationError Error(ApplicationErrorKind kind, string code) =>
        ApplicationError.Create(kind, code);

    private sealed record ReceiptLocationResolution(Guid StorageLocationId, ApplicationError? Error)
    {
        public static ReceiptLocationResolution Resolved(Guid storageLocationId) => new(storageLocationId, null);
        public static ReceiptLocationResolution Failed(ApplicationError error) => new(Guid.Empty, error);
    }

    private sealed record BatchState(
        Guid Id,
        OutsourcedSupplyBatchLifecycleStatus LifecycleStatus,
        DateTimeOffset? DeletedAt);

    private enum CommandAcquisitionKind
    {
        Acquired,
        Replay,
        Conflict,
    }

    private sealed record CommandAcquisition(
        CommandAcquisitionKind Kind,
        ConfirmOutsourcedSupplyDetailResult? ReplayResult)
    {
        public static CommandAcquisition Acquired() => new(CommandAcquisitionKind.Acquired, null);
        public static CommandAcquisition Replay(ConfirmOutsourcedSupplyDetailResult result) =>
            new(CommandAcquisitionKind.Replay, result);
        public static CommandAcquisition Conflict() => new(CommandAcquisitionKind.Conflict, null);
    }
}
