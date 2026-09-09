using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Product;

namespace YowThi.Erp.Infrastructure.Persistence.Product;

internal sealed class PostgreSqlProcurementProductMasterExecutor : IProcurementProductMasterExecutor
{
    private const string CreateCommandType = "CreateProcurementProduct";
    private const string UpdateCommandType = "UpdateProcurementProduct";
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;
    private readonly ProductMasterCommandSupport _support;

    public PostgreSqlProcurementProductMasterExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
        _support = new ProductMasterCommandSupport(dbContext);
    }

    public ValueTask<ApplicationResult<ProcurementProductMasterWriteResult>> CreateAsync(
        CreateProcurementProductExecution execution,
        CancellationToken cancellationToken)
    {
        var validation = ProcurementProductMasterValidation.Validate(execution.Command);
        if (validation is not null)
        {
            return ValueTask.FromResult(ApplicationResult<ProcurementProductMasterWriteResult>.Failure(validation));
        }

        return _transactionRunner.ExecuteAsync(async ct =>
        {
            var now = _timeProvider.GetUtcNow();
            var acquisition = await _support.AcquireAsync<ProcurementProductMasterWriteResult>(
                execution.CommandId, execution.RequestHash, execution.ActorAccountId, CreateCommandType, now, ct);
            if (acquisition.Kind == CommandAcquisitionKind.Replay)
            {
                return CommandTransactionDecision<ApplicationResult<ProcurementProductMasterWriteResult>>.Rollback(
                    ApplicationResult<ProcurementProductMasterWriteResult>.Success(acquisition.ReplayResult!));
            }
            if (acquisition.Kind == CommandAcquisitionKind.Conflict)
            {
                return Rollback(ApplicationErrorKind.Conflict, ProcurementProductMasterErrorCodes.IdempotencyKeyReused);
            }
            if (execution.Command.DefaultStorageLocationId is { } storageLocationId
                && !await StorageLocationExistsAsync(storageLocationId, ct))
            {
                return Rollback(ApplicationErrorKind.NotFound, ProcurementProductMasterErrorCodes.StorageLocationNotFound);
            }

            var id = Guid.CreateVersion7();
            await using (var command = _support.CreateSqlCommand(
                """
                INSERT INTO product.procurement_products
                    (id, name_zh_tw, name_th_th, unit_code, default_storage_location_id,
                     active, row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
                VALUES
                    (@id, @name_zh_tw, @name_th_th, @unit_code, @default_storage_location_id,
                     @active, 1, @created_at, @created_by_account_id, NULL, NULL);
                """))
            {
                command.Parameters.AddWithValue("id", id);
                ProductMasterCommandSupport.AddNullableText(command, "name_zh_tw", execution.Command.NameZhTw);
                ProductMasterCommandSupport.AddNullableText(command, "name_th_th", execution.Command.NameThTh);
                command.Parameters.AddWithValue("unit_code", execution.Command.UnitCode.Trim());
                ProductMasterCommandSupport.AddNullableGuid(command, "default_storage_location_id", execution.Command.DefaultStorageLocationId);
                command.Parameters.AddWithValue("active", execution.Command.Active);
                command.Parameters.AddWithValue("created_at", now);
                command.Parameters.AddWithValue("created_by_account_id", execution.ActorAccountId.Value);
                await command.ExecuteNonQueryAsync(ct);
            }

            var result = new ProcurementProductMasterWriteResult(id, 1);
            await _support.AppendAuditAsync(execution.CommandId, execution.ActorAccountId, CreateCommandType,
                "product.procurement-product", id, "CREATE", null, 1, now, ct);
            await _support.MarkSucceededAsync(execution.CommandId, result, now, ct);
            return CommandTransactionDecision<ApplicationResult<ProcurementProductMasterWriteResult>>.Commit(
                ApplicationResult<ProcurementProductMasterWriteResult>.Success(result));
        }, cancellationToken);
    }

    public ValueTask<ApplicationResult<ProcurementProductMasterWriteResult>> UpdateAsync(
        UpdateProcurementProductExecution execution,
        CancellationToken cancellationToken)
    {
        var validation = ProcurementProductMasterValidation.Validate(execution.Command);
        if (validation is not null)
        {
            return ValueTask.FromResult(ApplicationResult<ProcurementProductMasterWriteResult>.Failure(validation));
        }

        return _transactionRunner.ExecuteAsync(async ct =>
        {
            var now = _timeProvider.GetUtcNow();
            var acquisition = await _support.AcquireAsync<ProcurementProductMasterWriteResult>(
                execution.CommandId, execution.RequestHash, execution.ActorAccountId, UpdateCommandType, now, ct);
            if (acquisition.Kind == CommandAcquisitionKind.Replay)
            {
                return CommandTransactionDecision<ApplicationResult<ProcurementProductMasterWriteResult>>.Rollback(
                    ApplicationResult<ProcurementProductMasterWriteResult>.Success(acquisition.ReplayResult!));
            }
            if (acquisition.Kind == CommandAcquisitionKind.Conflict)
            {
                return Rollback(ApplicationErrorKind.Conflict, ProcurementProductMasterErrorCodes.IdempotencyKeyReused);
            }

            var state = await LockAsync(execution.Command.ProcurementProductId, ct);
            if (state is null)
            {
                return Rollback(ApplicationErrorKind.NotFound, ProcurementProductMasterErrorCodes.ItemNotFound);
            }
            if (state.DeletedAt is not null)
            {
                return Rollback(ApplicationErrorKind.Conflict, ProcurementProductMasterErrorCodes.ItemDeleted);
            }
            if (state.RowVersion != execution.Command.ExpectedRowVersion)
            {
                return Rollback(ApplicationErrorKind.Conflict, ProcurementProductMasterErrorCodes.StaleRowVersion);
            }
            if (execution.Command.DefaultStorageLocationId is { } storageLocationId
                && !await StorageLocationExistsAsync(storageLocationId, ct))
            {
                return Rollback(ApplicationErrorKind.NotFound, ProcurementProductMasterErrorCodes.StorageLocationNotFound);
            }

            long? nextRowVersion;
            await using (var command = _support.CreateSqlCommand(
                """
                UPDATE product.procurement_products
                SET name_zh_tw = @name_zh_tw,
                    name_th_th = @name_th_th,
                    unit_code = @unit_code,
                    default_storage_location_id = @default_storage_location_id,
                    active = @active,
                    row_version = row_version + 1
                WHERE id = @id AND row_version = @expected_row_version AND deleted_at IS NULL
                RETURNING row_version;
                """))
            {
                command.Parameters.AddWithValue("id", execution.Command.ProcurementProductId);
                command.Parameters.AddWithValue("expected_row_version", execution.Command.ExpectedRowVersion);
                ProductMasterCommandSupport.AddNullableText(command, "name_zh_tw", execution.Command.NameZhTw);
                ProductMasterCommandSupport.AddNullableText(command, "name_th_th", execution.Command.NameThTh);
                command.Parameters.AddWithValue("unit_code", execution.Command.UnitCode.Trim());
                ProductMasterCommandSupport.AddNullableGuid(command, "default_storage_location_id", execution.Command.DefaultStorageLocationId);
                command.Parameters.AddWithValue("active", execution.Command.Active);
                var value = await command.ExecuteScalarAsync(ct);
                nextRowVersion = value is null ? null : Convert.ToInt64(value);
            }
            if (nextRowVersion is null)
            {
                return Rollback(ApplicationErrorKind.Conflict, ProcurementProductMasterErrorCodes.StaleRowVersion);
            }

            var result = new ProcurementProductMasterWriteResult(execution.Command.ProcurementProductId, nextRowVersion.Value);
            await _support.AppendAuditAsync(execution.CommandId, execution.ActorAccountId, UpdateCommandType,
                "product.procurement-product", execution.Command.ProcurementProductId, "UPDATE",
                state.RowVersion, nextRowVersion.Value, now, ct);
            await _support.MarkSucceededAsync(execution.CommandId, result, now, ct);
            return CommandTransactionDecision<ApplicationResult<ProcurementProductMasterWriteResult>>.Commit(
                ApplicationResult<ProcurementProductMasterWriteResult>.Success(result));
        }, cancellationToken);
    }

    private async ValueTask<ProductState?> LockAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = _support.CreateSqlCommand(
            "SELECT row_version, deleted_at FROM product.procurement_products WHERE id = @id FOR UPDATE;");
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ProductState(reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1))
            : null;
    }

    private async ValueTask<bool> StorageLocationExistsAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = _support.CreateSqlCommand("SELECT 1 FROM infrastructure.storage_locations WHERE id = @id;");
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static CommandTransactionDecision<ApplicationResult<ProcurementProductMasterWriteResult>> Rollback(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<ProcurementProductMasterWriteResult>>.Rollback(
            ApplicationResult<ProcurementProductMasterWriteResult>.Failure(ApplicationError.Create(kind, code)));

    private sealed record ProductState(long RowVersion, DateTimeOffset? DeletedAt);
}
