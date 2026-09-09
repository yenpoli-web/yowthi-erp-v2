using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Infrastructure;
using YowThi.Erp.Infrastructure.Persistence.Commands;

namespace YowThi.Erp.Infrastructure.Persistence.Infrastructure;

internal sealed class PostgreSqlStorageLocationMasterExecutor : IStorageLocationMasterExecutor
{
    private const string CreateCommandType = "CreateStorageLocation";
    private const string UpdateCommandType = "UpdateStorageLocation";
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;
    private readonly MasterCommandSupport _support;

    public PostgreSqlStorageLocationMasterExecutor(ErpDbContext dbContext, ICommandTransactionRunner transactionRunner, TimeProvider timeProvider)
    {
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
        _support = new MasterCommandSupport(dbContext);
    }

    public ValueTask<ApplicationResult<StorageLocationMasterWriteResult>> CreateAsync(CreateStorageLocationExecution execution, CancellationToken cancellationToken)
    {
        var validation = StorageLocationMasterValidation.Validate(execution.Command);
        if (validation is not null) return ValueTask.FromResult(ApplicationResult<StorageLocationMasterWriteResult>.Failure(validation));

        return _transactionRunner.ExecuteAsync(async ct =>
        {
            var now = _timeProvider.GetUtcNow();
            var acquisition = await _support.AcquireAsync<StorageLocationMasterWriteResult>(execution.CommandId, execution.RequestHash, execution.ActorAccountId, CreateCommandType, now, ct);
            if (acquisition.Kind == MasterCommandAcquisitionKind.Replay)
                return CommandTransactionDecision<ApplicationResult<StorageLocationMasterWriteResult>>.Rollback(ApplicationResult<StorageLocationMasterWriteResult>.Success(acquisition.ReplayResult!));
            if (acquisition.Kind == MasterCommandAcquisitionKind.Conflict)
                return Rollback(ApplicationErrorKind.Conflict, StorageLocationMasterErrorCodes.IdempotencyKeyReused);
            if (!await WarehouseExistsAsync(execution.Command.WarehouseId, ct))
                return Rollback(ApplicationErrorKind.NotFound, StorageLocationMasterErrorCodes.WarehouseNotFound);

            var id = Guid.CreateVersion7();
            await using (var command = _support.CreateSqlCommand(
                """
                INSERT INTO infrastructure.storage_locations
                    (id, warehouse_id, code, name_zh_tw, name_th_th, active, row_version,
                     created_at, created_by_account_id, deleted_at, deleted_by_account_id)
                VALUES
                    (@id, @warehouse_id, @code, @name_zh_tw, @name_th_th, @active, 1,
                     @created_at, @created_by_account_id, NULL, NULL);
                """))
            {
                command.Parameters.AddWithValue("id", id);
                command.Parameters.AddWithValue("warehouse_id", execution.Command.WarehouseId);
                MasterCommandSupport.AddNullableText(command, "code", execution.Command.Code);
                MasterCommandSupport.AddNullableText(command, "name_zh_tw", execution.Command.NameZhTw);
                MasterCommandSupport.AddNullableText(command, "name_th_th", execution.Command.NameThTh);
                command.Parameters.AddWithValue("active", execution.Command.Active);
                command.Parameters.AddWithValue("created_at", now);
                command.Parameters.AddWithValue("created_by_account_id", execution.ActorAccountId.Value);
                await command.ExecuteNonQueryAsync(ct);
            }

            var result = new StorageLocationMasterWriteResult(id, 1);
            await _support.AppendAuditAsync(execution.CommandId, execution.ActorAccountId, CreateCommandType,
                "infrastructure.storage-location", id, "CREATE", null, 1, now, ct);
            await _support.MarkSucceededAsync(execution.CommandId, result, now, ct);
            return CommandTransactionDecision<ApplicationResult<StorageLocationMasterWriteResult>>.Commit(ApplicationResult<StorageLocationMasterWriteResult>.Success(result));
        }, cancellationToken);
    }

    public ValueTask<ApplicationResult<StorageLocationMasterWriteResult>> UpdateAsync(UpdateStorageLocationExecution execution, CancellationToken cancellationToken)
    {
        var validation = StorageLocationMasterValidation.Validate(execution.Command);
        if (validation is not null) return ValueTask.FromResult(ApplicationResult<StorageLocationMasterWriteResult>.Failure(validation));

        return _transactionRunner.ExecuteAsync(async ct =>
        {
            var now = _timeProvider.GetUtcNow();
            var acquisition = await _support.AcquireAsync<StorageLocationMasterWriteResult>(execution.CommandId, execution.RequestHash, execution.ActorAccountId, UpdateCommandType, now, ct);
            if (acquisition.Kind == MasterCommandAcquisitionKind.Replay)
                return CommandTransactionDecision<ApplicationResult<StorageLocationMasterWriteResult>>.Rollback(ApplicationResult<StorageLocationMasterWriteResult>.Success(acquisition.ReplayResult!));
            if (acquisition.Kind == MasterCommandAcquisitionKind.Conflict)
                return Rollback(ApplicationErrorKind.Conflict, StorageLocationMasterErrorCodes.IdempotencyKeyReused);

            var state = await LockAsync(execution.Command.StorageLocationId, ct);
            if (state is null) return Rollback(ApplicationErrorKind.NotFound, StorageLocationMasterErrorCodes.NotFound);
            if (state.DeletedAt is not null) return Rollback(ApplicationErrorKind.Conflict, StorageLocationMasterErrorCodes.Deleted);
            if (state.RowVersion != execution.Command.ExpectedRowVersion) return Rollback(ApplicationErrorKind.Conflict, StorageLocationMasterErrorCodes.StaleRowVersion);
            if (!await WarehouseExistsAsync(execution.Command.WarehouseId, ct))
                return Rollback(ApplicationErrorKind.NotFound, StorageLocationMasterErrorCodes.WarehouseNotFound);

            long? nextRowVersion;
            await using (var command = _support.CreateSqlCommand(
                """
                UPDATE infrastructure.storage_locations
                SET warehouse_id = @warehouse_id,
                    code = @code,
                    name_zh_tw = @name_zh_tw,
                    name_th_th = @name_th_th,
                    active = @active,
                    row_version = row_version + 1
                WHERE id = @id AND row_version = @expected_row_version AND deleted_at IS NULL
                RETURNING row_version;
                """))
            {
                command.Parameters.AddWithValue("id", execution.Command.StorageLocationId);
                command.Parameters.AddWithValue("expected_row_version", execution.Command.ExpectedRowVersion);
                command.Parameters.AddWithValue("warehouse_id", execution.Command.WarehouseId);
                MasterCommandSupport.AddNullableText(command, "code", execution.Command.Code);
                MasterCommandSupport.AddNullableText(command, "name_zh_tw", execution.Command.NameZhTw);
                MasterCommandSupport.AddNullableText(command, "name_th_th", execution.Command.NameThTh);
                command.Parameters.AddWithValue("active", execution.Command.Active);
                var value = await command.ExecuteScalarAsync(ct);
                nextRowVersion = value is null ? null : Convert.ToInt64(value);
            }
            if (nextRowVersion is null) return Rollback(ApplicationErrorKind.Conflict, StorageLocationMasterErrorCodes.StaleRowVersion);

            var result = new StorageLocationMasterWriteResult(execution.Command.StorageLocationId, nextRowVersion.Value);
            await _support.AppendAuditAsync(execution.CommandId, execution.ActorAccountId, UpdateCommandType,
                "infrastructure.storage-location", execution.Command.StorageLocationId, "UPDATE", state.RowVersion, nextRowVersion.Value, now, ct);
            await _support.MarkSucceededAsync(execution.CommandId, result, now, ct);
            return CommandTransactionDecision<ApplicationResult<StorageLocationMasterWriteResult>>.Commit(ApplicationResult<StorageLocationMasterWriteResult>.Success(result));
        }, cancellationToken);
    }

    private async ValueTask<LocationState?> LockAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = _support.CreateSqlCommand("SELECT row_version, deleted_at FROM infrastructure.storage_locations WHERE id = @id FOR UPDATE;");
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new LocationState(reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1))
            : null;
    }

    private async ValueTask<bool> WarehouseExistsAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = _support.CreateSqlCommand("SELECT 1 FROM infrastructure.warehouses WHERE id = @id;");
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static CommandTransactionDecision<ApplicationResult<StorageLocationMasterWriteResult>> Rollback(ApplicationErrorKind kind, string code) =>
        CommandTransactionDecision<ApplicationResult<StorageLocationMasterWriteResult>>.Rollback(
            ApplicationResult<StorageLocationMasterWriteResult>.Failure(ApplicationError.Create(kind, code)));

    private sealed record LocationState(long RowVersion, DateTimeOffset? DeletedAt);
}
