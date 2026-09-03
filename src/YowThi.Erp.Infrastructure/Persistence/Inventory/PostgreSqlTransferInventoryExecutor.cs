using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Inventory;
using YowThi.Erp.Domain.Infrastructure;
using YowThi.Erp.Domain.Inventory;

namespace YowThi.Erp.Infrastructure.Persistence.Inventory;

internal sealed class PostgreSqlTransferInventoryExecutor : ITransferInventoryExecutor
{
    private const string CommandType = "TransferInventory";
    private const string OutboxMessageType = "inventory.transferred";
    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlTransferInventoryExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public async ValueTask<ApplicationResult<TransferInventoryResult>> ExecuteAsync(
        TransferInventoryExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);

        var validationError = TransferInventoryValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ApplicationResult<TransferInventoryResult>.Failure(validationError);
        }

        return await _transactionRunner.ExecuteAsync(
            async ct =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await InventoryCommandPersistence.AcquireAsync<TransferInventoryResult>(
                    _dbContext,
                    CommandType,
                    execution.CommandId,
                    execution.RequestHash,
                    execution.ActorAccountId,
                    now,
                    StoredJsonOptions,
                    ct);

                if (acquisition.Kind == InventoryCommandAcquisitionKind.Replay)
                {
                    return CommandTransactionDecision<ApplicationResult<TransferInventoryResult>>.Rollback(
                        ApplicationResult<TransferInventoryResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == InventoryCommandAcquisitionKind.Conflict)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        InventoryApplicationErrorCodes.IdempotencyKeyReused);
                }

                var command = execution.Command;
                var batchState = await InventoryBatchLifecycleCommandLock.AcquireAsync(
                    _dbContext,
                    command.InventoryIdentity,
                    ct);
                if (!batchState.Exists)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.NotFound,
                        InventoryApplicationErrorCodes.PositionNotFound);
                }

                if (!batchState.Active || batchState.Deleted)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        InventoryApplicationErrorCodes.BatchClosed);
                }

                if (!await IsCurrentStorageLocationAsync(command.DestinationStorageLocationId, ct))
                {
                    return RollbackFailure(
                        ApplicationErrorKind.NotFound,
                        InventoryApplicationErrorCodes.StorageLocationNotFound);
                }

                var source = await ReadSourcePositionAsync(command, ct);
                if (source is null)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.NotFound,
                        InventoryApplicationErrorCodes.PositionNotFound);
                }

                if (source.Value.BalanceQuantity < command.Quantity)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        InventoryApplicationErrorCodes.InsufficientStock);
                }

                if (!await DeductSourceAsync(source.Value, command.Quantity, ct))
                {
                    var current = await ReadPositionByIdAsync(source.Value.Id, ct);
                    if (current is null)
                    {
                        return RollbackFailure(
                            ApplicationErrorKind.NotFound,
                            InventoryApplicationErrorCodes.PositionNotFound);
                    }

                    if (current.Value.RowVersion != source.Value.RowVersion)
                    {
                        return RollbackFailure(
                            ApplicationErrorKind.Conflict,
                            InventoryApplicationErrorCodes.ConcurrentChange);
                    }

                    if (current.Value.BalanceQuantity < command.Quantity)
                    {
                        return RollbackFailure(
                            ApplicationErrorKind.Conflict,
                            InventoryApplicationErrorCodes.InsufficientStock);
                    }

                    throw new InvalidOperationException(
                        "Inventory source CAS failed without an explained state change.");
                }

                await UpsertDestinationAsync(command, ct);

                var operationId = Guid.CreateVersion7();
                var transferOutId = Guid.CreateVersion7();
                var transferInId = Guid.CreateVersion7();
                var identity = command.InventoryIdentity;

                var operation = InventoryOperation.CreateTransfer(
                    operationId,
                    now,
                    execution.ActorAccountId.Value);
                var transferOut = InventoryMovement.CreateTransferOut(
                    transferOutId,
                    operationId,
                    identity.Origin,
                    identity.ProcurementBatchId,
                    identity.OutsourcedSupplyBatchId,
                    identity.InventoryObjectKind,
                    identity.ProcurementProductId,
                    identity.ProcessMaterialId,
                    identity.SalesProductId,
                    command.SourceStorageLocationId,
                    identity.RawSourceKind,
                    identity.SupplierId,
                    command.Quantity,
                    now);
                var transferIn = InventoryMovement.CreateTransferIn(
                    transferInId,
                    operationId,
                    identity.Origin,
                    identity.ProcurementBatchId,
                    identity.OutsourcedSupplyBatchId,
                    identity.InventoryObjectKind,
                    identity.ProcurementProductId,
                    identity.ProcessMaterialId,
                    identity.SalesProductId,
                    command.DestinationStorageLocationId,
                    identity.RawSourceKind,
                    identity.SupplierId,
                    command.Quantity,
                    now);

                _dbContext.AddRange(operation, transferOut, transferIn);
                await _dbContext.SaveChangesAsync(ct);

                var result = new TransferInventoryResult(
                    operationId,
                    transferOutId,
                    transferInId,
                    command.Quantity);
                var resultJson = JsonSerializer.Serialize(result, StoredJsonOptions);

                await InventoryCommandPersistence.AppendAuditAsync(
                    _dbContext,
                    execution.CommandId,
                    CommandType,
                    execution.ActorAccountId,
                    "inventory.operation",
                    operationId,
                    null,
                    now,
                    StoredJsonOptions,
                    ct);
                await InventoryCommandPersistence.EnqueueOutboxAsync(
                    _dbContext,
                    OutboxMessageType,
                    execution.CommandId,
                    resultJson,
                    now,
                    ct);
                await InventoryCommandPersistence.MarkSucceededAsync(
                    _dbContext,
                    execution.CommandId,
                    resultJson,
                    now,
                    ct);

                return CommandTransactionDecision<ApplicationResult<TransferInventoryResult>>.Commit(
                    ApplicationResult<TransferInventoryResult>.Success(result));
            },
            cancellationToken);
    }

    private async ValueTask<bool> IsCurrentStorageLocationAsync(
        Guid storageLocationId,
        CancellationToken cancellationToken) =>
        await _dbContext.Set<StorageLocation>()
            .AsNoTracking()
            .AnyAsync(
                x => x.Id == storageLocationId && x.Active && x.DeletedAt == null,
                cancellationToken);

    private async ValueTask<PositionSnapshot?> ReadSourcePositionAsync(
        TransferInventoryCommand command,
        CancellationToken cancellationToken)
    {
        await using var sql = InventoryCommandPersistence.CreateSqlCommand(
            _dbContext,
            """
            SELECT id, balance_quantity, row_version
            FROM inventory.inventory_positions
            WHERE origin = @origin
              AND procurement_batch_id IS NOT DISTINCT FROM @procurement_batch_id
              AND outsourced_supply_batch_id IS NOT DISTINCT FROM @outsourced_supply_batch_id
              AND inventory_object_kind = @inventory_object_kind
              AND procurement_product_id IS NOT DISTINCT FROM @procurement_product_id
              AND process_material_id IS NOT DISTINCT FROM @process_material_id
              AND sales_product_id IS NOT DISTINCT FROM @sales_product_id
              AND storage_location_id = @storage_location_id
              AND raw_source_kind IS NOT DISTINCT FROM @raw_source_kind
              AND supplier_id IS NOT DISTINCT FROM @supplier_id;
            """);
        AddIdentityParameters(sql, command.InventoryIdentity);
        sql.Parameters.AddWithValue("storage_location_id", command.SourceStorageLocationId);

        await using var reader = await sql.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PositionSnapshot(
            reader.GetGuid(0),
            reader.GetFieldValue<decimal>(1),
            reader.GetInt64(2));
    }

    private async ValueTask<PositionSnapshot?> ReadPositionByIdAsync(
        Guid positionId,
        CancellationToken cancellationToken)
    {
        await using var sql = InventoryCommandPersistence.CreateSqlCommand(
            _dbContext,
            """
            SELECT id, balance_quantity, row_version
            FROM inventory.inventory_positions
            WHERE id = @id;
            """);
        sql.Parameters.AddWithValue("id", positionId);

        await using var reader = await sql.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PositionSnapshot(
            reader.GetGuid(0),
            reader.GetFieldValue<decimal>(1),
            reader.GetInt64(2));
    }

    private async ValueTask<bool> DeductSourceAsync(
        PositionSnapshot source,
        decimal quantity,
        CancellationToken cancellationToken)
    {
        await using var sql = InventoryCommandPersistence.CreateSqlCommand(
            _dbContext,
            """
            UPDATE inventory.inventory_positions
            SET balance_quantity = balance_quantity - @quantity,
                row_version = row_version + 1
            WHERE id = @id
              AND row_version = @expected_row_version
              AND balance_quantity >= @quantity;
            """);
        sql.Parameters.AddWithValue("quantity", quantity);
        sql.Parameters.AddWithValue("id", source.Id);
        sql.Parameters.AddWithValue("expected_row_version", source.RowVersion);
        return await sql.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private async ValueTask UpsertDestinationAsync(
        TransferInventoryCommand command,
        CancellationToken cancellationToken)
    {
        await using var sql = InventoryCommandPersistence.CreateSqlCommand(
            _dbContext,
            """
            INSERT INTO inventory.inventory_positions
                (id, origin, procurement_batch_id, outsourced_supply_batch_id,
                 inventory_object_kind, procurement_product_id, process_material_id,
                 sales_product_id, storage_location_id, raw_source_kind, supplier_id,
                 balance_quantity, row_version)
            VALUES
                (@id, @origin, @procurement_batch_id, @outsourced_supply_batch_id,
                 @inventory_object_kind, @procurement_product_id, @process_material_id,
                 @sales_product_id, @storage_location_id, @raw_source_kind, @supplier_id,
                 @quantity, 1)
            ON CONFLICT
                (origin, procurement_batch_id, outsourced_supply_batch_id,
                 inventory_object_kind, procurement_product_id, process_material_id,
                 sales_product_id, storage_location_id, raw_source_kind, supplier_id)
            DO UPDATE SET
                balance_quantity = inventory.inventory_positions.balance_quantity + EXCLUDED.balance_quantity,
                row_version = inventory.inventory_positions.row_version + 1;
            """);
        sql.Parameters.AddWithValue("id", Guid.CreateVersion7());
        AddIdentityParameters(sql, command.InventoryIdentity);
        sql.Parameters.AddWithValue("storage_location_id", command.DestinationStorageLocationId);
        sql.Parameters.AddWithValue("quantity", command.Quantity);
        await sql.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddIdentityParameters(NpgsqlCommand sql, InventoryPositionIdentity identity)
    {
        sql.Parameters.AddWithValue("origin", identity.Origin.ToString());
        AddNullableGuid(sql, "procurement_batch_id", identity.ProcurementBatchId);
        AddNullableGuid(sql, "outsourced_supply_batch_id", identity.OutsourcedSupplyBatchId);
        sql.Parameters.AddWithValue("inventory_object_kind", identity.InventoryObjectKind.ToString());
        AddNullableGuid(sql, "procurement_product_id", identity.ProcurementProductId);
        AddNullableGuid(sql, "process_material_id", identity.ProcessMaterialId);
        AddNullableGuid(sql, "sales_product_id", identity.SalesProductId);
        var rawSourceParameter = sql.Parameters.Add("raw_source_kind", NpgsqlDbType.Text);
        rawSourceParameter.Value = identity.RawSourceKind is null
            ? DBNull.Value
            : identity.RawSourceKind.Value.ToString();
        AddNullableGuid(sql, "supplier_id", identity.SupplierId);
    }

    private static void AddNullableGuid(NpgsqlCommand sql, string name, Guid? value)
    {
        var parameter = sql.Parameters.Add(name, NpgsqlDbType.Uuid);
        parameter.Value = value is Guid id ? id : DBNull.Value;
    }

    private static CommandTransactionDecision<ApplicationResult<TransferInventoryResult>> RollbackFailure(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<TransferInventoryResult>>.Rollback(
            ApplicationResult<TransferInventoryResult>.Failure(ApplicationError.Create(kind, code)));

    private readonly record struct PositionSnapshot(
        Guid Id,
        decimal BalanceQuantity,
        long RowVersion);
}
