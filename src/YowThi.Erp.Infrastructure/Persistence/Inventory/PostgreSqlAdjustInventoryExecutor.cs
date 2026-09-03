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
using YowThi.Erp.Domain.Outsourced;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Domain.ProcessingConfiguration;
using YowThi.Erp.Domain.Procurement;
using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Infrastructure.Persistence.Inventory;

internal sealed class PostgreSqlAdjustInventoryExecutor : IAdjustInventoryExecutor
{
    private const string CommandType = "AdjustInventory";
    private const string OutboxMessageType = "inventory.adjusted";
    private static readonly JsonSerializerOptions StoredJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _dbContext;
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlAdjustInventoryExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
    }

    public async ValueTask<ApplicationResult<AdjustInventoryResult>> ExecuteAsync(
        AdjustInventoryExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);

        var validationError = AdjustInventoryValidation.Validate(execution.Command);
        if (validationError is not null)
        {
            return ApplicationResult<AdjustInventoryResult>.Failure(validationError);
        }

        return await _transactionRunner.ExecuteAsync(
            async ct =>
            {
                var now = _timeProvider.GetUtcNow();
                var acquisition = await InventoryCommandPersistence.AcquireAsync<AdjustInventoryResult>(
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
                    return CommandTransactionDecision<ApplicationResult<AdjustInventoryResult>>.Rollback(
                        ApplicationResult<AdjustInventoryResult>.Success(acquisition.ReplayResult!));
                }

                if (acquisition.Kind == InventoryCommandAcquisitionKind.Conflict)
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Conflict,
                        InventoryApplicationErrorCodes.IdempotencyKeyReused);
                }

                var command = execution.Command;
                if (!await IsUsableStorageLocationAsync(command.StorageLocationId, ct))
                {
                    return RollbackFailure(
                        ApplicationErrorKind.NotFound,
                        InventoryApplicationErrorCodes.StorageLocationNotFound);
                }

                if (!await IdentityReferencesExistAsync(command.InventoryIdentity, ct))
                {
                    return RollbackFailure(
                        ApplicationErrorKind.Validation,
                        InventoryApplicationErrorCodes.InvalidInput);
                }

                await UpsertPositionAsync(command, ct);

                var operationId = Guid.CreateVersion7();
                var movementId = Guid.CreateVersion7();
                var identity = command.InventoryIdentity;

                var operation = InventoryOperation.CreateAdjustment(
                    operationId,
                    now,
                    execution.ActorAccountId.Value);
                var movement = InventoryMovement.CreateAdjustment(
                    movementId,
                    operationId,
                    identity.Origin,
                    identity.ProcurementBatchId,
                    identity.OutsourcedSupplyBatchId,
                    identity.InventoryObjectKind,
                    identity.ProcurementProductId,
                    identity.ProcessMaterialId,
                    identity.SalesProductId,
                    command.StorageLocationId,
                    identity.RawSourceKind,
                    identity.SupplierId,
                    command.QuantityDelta,
                    now);

                _dbContext.AddRange(operation, movement);
                await _dbContext.SaveChangesAsync(ct);

                var result = new AdjustInventoryResult(
                    operationId,
                    movementId,
                    command.QuantityDelta);
                var resultJson = JsonSerializer.Serialize(result, StoredJsonOptions);

                await InventoryCommandPersistence.AppendAuditAsync(
                    _dbContext,
                    execution.CommandId,
                    CommandType,
                    execution.ActorAccountId,
                    "inventory.operation",
                    operationId,
                    command.ReasonText,
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

                return CommandTransactionDecision<ApplicationResult<AdjustInventoryResult>>.Commit(
                    ApplicationResult<AdjustInventoryResult>.Success(result));
            },
            cancellationToken);
    }

    private async ValueTask<bool> IsUsableStorageLocationAsync(
        Guid storageLocationId,
        CancellationToken cancellationToken) =>
        await _dbContext.Set<StorageLocation>()
            .AsNoTracking()
            .AnyAsync(x => x.Id == storageLocationId && x.DeletedAt == null, cancellationToken);

    private async ValueTask<bool> IdentityReferencesExistAsync(
        InventoryPositionIdentity identity,
        CancellationToken cancellationToken)
    {
        var sourceExists = identity.Origin switch
        {
            InventoryOrigin.IN_HOUSE => await _dbContext.Set<ProcurementBatch>()
                .AsNoTracking()
                .AnyAsync(x => x.Id == identity.ProcurementBatchId!.Value, cancellationToken),
            InventoryOrigin.OUTSOURCED => await _dbContext.Set<OutsourcedSupplyBatch>()
                .AsNoTracking()
                .AnyAsync(x => x.Id == identity.OutsourcedSupplyBatchId!.Value, cancellationToken),
            _ => false,
        };

        if (!sourceExists)
        {
            return false;
        }

        var objectExists = identity.InventoryObjectKind switch
        {
            InventoryObjectKind.PROCUREMENT_PRODUCT => await _dbContext.Set<ProcurementProduct>()
                .AsNoTracking()
                .AnyAsync(x => x.Id == identity.ProcurementProductId!.Value, cancellationToken),
            InventoryObjectKind.PROCESS_MATERIAL => await _dbContext.Set<ProcessMaterial>()
                .AsNoTracking()
                .AnyAsync(x => x.Id == identity.ProcessMaterialId!.Value, cancellationToken),
            InventoryObjectKind.SALES_PRODUCT => await _dbContext.Set<SalesProduct>()
                .AsNoTracking()
                .AnyAsync(x => x.Id == identity.SalesProductId!.Value, cancellationToken),
            _ => false,
        };

        if (!objectExists)
        {
            return false;
        }

        return identity.RawSourceKind != InventoryRawSourceKind.SUPPLIER
            || await _dbContext.Set<Supplier>()
                .AsNoTracking()
                .AnyAsync(x => x.Id == identity.SupplierId!.Value, cancellationToken);
    }

    private async ValueTask UpsertPositionAsync(
        AdjustInventoryCommand command,
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
                 @quantity_delta, 1)
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
        sql.Parameters.AddWithValue("storage_location_id", command.StorageLocationId);
        sql.Parameters.AddWithValue("quantity_delta", command.QuantityDelta);
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

    private static CommandTransactionDecision<ApplicationResult<AdjustInventoryResult>> RollbackFailure(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<AdjustInventoryResult>>.Rollback(
            ApplicationResult<AdjustInventoryResult>.Failure(ApplicationError.Create(kind, code)));
}
