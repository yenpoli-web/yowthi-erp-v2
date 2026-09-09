using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Infrastructure;
using YowThi.Erp.Infrastructure.Persistence.Commands;

namespace YowThi.Erp.Infrastructure.Persistence.Infrastructure;

internal sealed class PostgreSqlWarehouseMasterExecutor : IWarehouseMasterExecutor
{
    private const string CreateCommandType = "CreateWarehouse";
    private const string UpdateCommandType = "UpdateWarehouse";
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;
    private readonly MasterCommandSupport _support;

    public PostgreSqlWarehouseMasterExecutor(ErpDbContext dbContext, ICommandTransactionRunner transactionRunner, TimeProvider timeProvider)
    {
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
        _support = new MasterCommandSupport(dbContext);
    }

    public ValueTask<ApplicationResult<WarehouseMasterWriteResult>> CreateAsync(CreateWarehouseExecution execution, CancellationToken cancellationToken)
    {
        var validation = WarehouseMasterValidation.Validate(execution.Command);
        if (validation is not null) return ValueTask.FromResult(ApplicationResult<WarehouseMasterWriteResult>.Failure(validation));

        return _transactionRunner.ExecuteAsync(async ct =>
        {
            var now = _timeProvider.GetUtcNow();
            var acquisition = await _support.AcquireAsync<WarehouseMasterWriteResult>(execution.CommandId, execution.RequestHash, execution.ActorAccountId, CreateCommandType, now, ct);
            if (acquisition.Kind == MasterCommandAcquisitionKind.Replay)
                return CommandTransactionDecision<ApplicationResult<WarehouseMasterWriteResult>>.Rollback(ApplicationResult<WarehouseMasterWriteResult>.Success(acquisition.ReplayResult!));
            if (acquisition.Kind == MasterCommandAcquisitionKind.Conflict)
                return Rollback(ApplicationErrorKind.Conflict, WarehouseMasterErrorCodes.IdempotencyKeyReused);

            var id = Guid.CreateVersion7();
            await using (var command = _support.CreateSqlCommand(
                """
                INSERT INTO infrastructure.warehouses
                    (id, code, name_zh_tw, name_th_th, active, row_version,
                     created_at, created_by_account_id, deleted_at, deleted_by_account_id)
                VALUES
                    (@id, @code, @name_zh_tw, @name_th_th, @active, 1,
                     @created_at, @created_by_account_id, NULL, NULL);
                """))
            {
                command.Parameters.AddWithValue("id", id);
                MasterCommandSupport.AddNullableText(command, "code", execution.Command.Code);
                MasterCommandSupport.AddNullableText(command, "name_zh_tw", execution.Command.NameZhTw);
                MasterCommandSupport.AddNullableText(command, "name_th_th", execution.Command.NameThTh);
                command.Parameters.AddWithValue("active", execution.Command.Active);
                command.Parameters.AddWithValue("created_at", now);
                command.Parameters.AddWithValue("created_by_account_id", execution.ActorAccountId.Value);
                await command.ExecuteNonQueryAsync(ct);
            }

            var result = new WarehouseMasterWriteResult(id, 1);
            await _support.AppendAuditAsync(execution.CommandId, execution.ActorAccountId, CreateCommandType,
                "infrastructure.warehouse", id, "CREATE", null, 1, now, ct);
            await _support.MarkSucceededAsync(execution.CommandId, result, now, ct);
            return CommandTransactionDecision<ApplicationResult<WarehouseMasterWriteResult>>.Commit(ApplicationResult<WarehouseMasterWriteResult>.Success(result));
        }, cancellationToken);
    }

    public ValueTask<ApplicationResult<WarehouseMasterWriteResult>> UpdateAsync(UpdateWarehouseExecution execution, CancellationToken cancellationToken)
    {
        var validation = WarehouseMasterValidation.Validate(execution.Command);
        if (validation is not null) return ValueTask.FromResult(ApplicationResult<WarehouseMasterWriteResult>.Failure(validation));

        return _transactionRunner.ExecuteAsync(async ct =>
        {
            var now = _timeProvider.GetUtcNow();
            var acquisition = await _support.AcquireAsync<WarehouseMasterWriteResult>(execution.CommandId, execution.RequestHash, execution.ActorAccountId, UpdateCommandType, now, ct);
            if (acquisition.Kind == MasterCommandAcquisitionKind.Replay)
                return CommandTransactionDecision<ApplicationResult<WarehouseMasterWriteResult>>.Rollback(ApplicationResult<WarehouseMasterWriteResult>.Success(acquisition.ReplayResult!));
            if (acquisition.Kind == MasterCommandAcquisitionKind.Conflict)
                return Rollback(ApplicationErrorKind.Conflict, WarehouseMasterErrorCodes.IdempotencyKeyReused);

            var state = await LockAsync(execution.Command.WarehouseId, ct);
            if (state is null) return Rollback(ApplicationErrorKind.NotFound, WarehouseMasterErrorCodes.NotFound);
            if (state.DeletedAt is not null) return Rollback(ApplicationErrorKind.Conflict, WarehouseMasterErrorCodes.Deleted);
            if (state.RowVersion != execution.Command.ExpectedRowVersion) return Rollback(ApplicationErrorKind.Conflict, WarehouseMasterErrorCodes.StaleRowVersion);

            long? nextRowVersion;
            await using (var command = _support.CreateSqlCommand(
                """
                UPDATE infrastructure.warehouses
                SET code = @code,
                    name_zh_tw = @name_zh_tw,
                    name_th_th = @name_th_th,
                    active = @active,
                    row_version = row_version + 1
                WHERE id = @id AND row_version = @expected_row_version AND deleted_at IS NULL
                RETURNING row_version;
                """))
            {
                command.Parameters.AddWithValue("id", execution.Command.WarehouseId);
                command.Parameters.AddWithValue("expected_row_version", execution.Command.ExpectedRowVersion);
                MasterCommandSupport.AddNullableText(command, "code", execution.Command.Code);
                MasterCommandSupport.AddNullableText(command, "name_zh_tw", execution.Command.NameZhTw);
                MasterCommandSupport.AddNullableText(command, "name_th_th", execution.Command.NameThTh);
                command.Parameters.AddWithValue("active", execution.Command.Active);
                var value = await command.ExecuteScalarAsync(ct);
                nextRowVersion = value is null ? null : Convert.ToInt64(value);
            }
            if (nextRowVersion is null) return Rollback(ApplicationErrorKind.Conflict, WarehouseMasterErrorCodes.StaleRowVersion);

            var result = new WarehouseMasterWriteResult(execution.Command.WarehouseId, nextRowVersion.Value);
            await _support.AppendAuditAsync(execution.CommandId, execution.ActorAccountId, UpdateCommandType,
                "infrastructure.warehouse", execution.Command.WarehouseId, "UPDATE", state.RowVersion, nextRowVersion.Value, now, ct);
            await _support.MarkSucceededAsync(execution.CommandId, result, now, ct);
            return CommandTransactionDecision<ApplicationResult<WarehouseMasterWriteResult>>.Commit(ApplicationResult<WarehouseMasterWriteResult>.Success(result));
        }, cancellationToken);
    }

    private async ValueTask<WarehouseState?> LockAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = _support.CreateSqlCommand("SELECT row_version, deleted_at FROM infrastructure.warehouses WHERE id = @id FOR UPDATE;");
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new WarehouseState(reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1))
            : null;
    }

    private static CommandTransactionDecision<ApplicationResult<WarehouseMasterWriteResult>> Rollback(ApplicationErrorKind kind, string code) =>
        CommandTransactionDecision<ApplicationResult<WarehouseMasterWriteResult>>.Rollback(
            ApplicationResult<WarehouseMasterWriteResult>.Failure(ApplicationError.Create(kind, code)));

    private sealed record WarehouseState(long RowVersion, DateTimeOffset? DeletedAt);
}
