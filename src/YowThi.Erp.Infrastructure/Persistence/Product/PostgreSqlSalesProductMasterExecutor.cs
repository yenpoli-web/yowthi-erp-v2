using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Application.Product;

namespace YowThi.Erp.Infrastructure.Persistence.Product;

internal sealed class PostgreSqlSalesProductMasterExecutor : ISalesProductMasterExecutor
{
    private const string CreateCommandType = "CreateSalesProduct";
    private const string UpdateCommandType = "UpdateSalesProduct";
    private readonly ICommandTransactionRunner _transactionRunner;
    private readonly TimeProvider _timeProvider;
    private readonly ProductMasterCommandSupport _support;

    public PostgreSqlSalesProductMasterExecutor(
        ErpDbContext dbContext,
        ICommandTransactionRunner transactionRunner,
        TimeProvider timeProvider)
    {
        _transactionRunner = transactionRunner;
        _timeProvider = timeProvider;
        _support = new ProductMasterCommandSupport(dbContext);
    }

    public ValueTask<ApplicationResult<SalesProductMasterWriteResult>> CreateAsync(
        CreateSalesProductExecution execution,
        CancellationToken cancellationToken)
    {
        var validation = SalesProductMasterValidation.Validate(execution.Command);
        if (validation is not null)
        {
            return ValueTask.FromResult(ApplicationResult<SalesProductMasterWriteResult>.Failure(validation));
        }

        return _transactionRunner.ExecuteAsync(async ct =>
        {
            var now = _timeProvider.GetUtcNow();
            var acquisition = await _support.AcquireAsync<SalesProductMasterWriteResult>(
                execution.CommandId, execution.RequestHash, execution.ActorAccountId, CreateCommandType, now, ct);
            if (acquisition.Kind == CommandAcquisitionKind.Replay)
            {
                return CommandTransactionDecision<ApplicationResult<SalesProductMasterWriteResult>>.Rollback(
                    ApplicationResult<SalesProductMasterWriteResult>.Success(acquisition.ReplayResult!));
            }
            if (acquisition.Kind == CommandAcquisitionKind.Conflict)
            {
                return Rollback(ApplicationErrorKind.Conflict, SalesProductMasterErrorCodes.IdempotencyKeyReused);
            }
            if (!await ProductGroupExistsAsync(execution.Command.SalesProductGroupId, ct))
            {
                return Rollback(ApplicationErrorKind.NotFound, SalesProductMasterErrorCodes.ProductGroupNotFound);
            }
            if (execution.Command.DefaultStorageLocationId is { } storageLocationId
                && !await StorageLocationExistsAsync(storageLocationId, ct))
            {
                return Rollback(ApplicationErrorKind.NotFound, SalesProductMasterErrorCodes.StorageLocationNotFound);
            }

            var id = Guid.CreateVersion7();
            await using (var command = _support.CreateSqlCommand(
                """
                INSERT INTO product.sales_products
                    (id, sales_product_group_id, name_zh_tw, name_th_th, pricing_basis,
                     packaging_weight, sales_weight, default_storage_location_id,
                     active, row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
                VALUES
                    (@id, @sales_product_group_id, @name_zh_tw, @name_th_th, @pricing_basis,
                     @packaging_weight, @sales_weight, @default_storage_location_id,
                     @active, 1, @created_at, @created_by_account_id, NULL, NULL);
                """))
            {
                AddWriteParameters(command, id, execution.Command.SalesProductGroupId,
                    execution.Command.NameZhTw, execution.Command.NameThTh, execution.Command.PricingBasis.ToString(),
                    execution.Command.PackagingWeight, execution.Command.SalesWeight,
                    execution.Command.DefaultStorageLocationId, execution.Command.Active);
                command.Parameters.AddWithValue("created_at", now);
                command.Parameters.AddWithValue("created_by_account_id", execution.ActorAccountId.Value);
                await command.ExecuteNonQueryAsync(ct);
            }

            var result = new SalesProductMasterWriteResult(id, 1);
            await _support.AppendAuditAsync(execution.CommandId, execution.ActorAccountId, CreateCommandType,
                "product.sales-product", id, "CREATE", null, 1, now, ct);
            await _support.MarkSucceededAsync(execution.CommandId, result, now, ct);
            return CommandTransactionDecision<ApplicationResult<SalesProductMasterWriteResult>>.Commit(
                ApplicationResult<SalesProductMasterWriteResult>.Success(result));
        }, cancellationToken);
    }

    public ValueTask<ApplicationResult<SalesProductMasterWriteResult>> UpdateAsync(
        UpdateSalesProductExecution execution,
        CancellationToken cancellationToken)
    {
        var validation = SalesProductMasterValidation.Validate(execution.Command);
        if (validation is not null)
        {
            return ValueTask.FromResult(ApplicationResult<SalesProductMasterWriteResult>.Failure(validation));
        }

        return _transactionRunner.ExecuteAsync(async ct =>
        {
            var now = _timeProvider.GetUtcNow();
            var acquisition = await _support.AcquireAsync<SalesProductMasterWriteResult>(
                execution.CommandId, execution.RequestHash, execution.ActorAccountId, UpdateCommandType, now, ct);
            if (acquisition.Kind == CommandAcquisitionKind.Replay)
            {
                return CommandTransactionDecision<ApplicationResult<SalesProductMasterWriteResult>>.Rollback(
                    ApplicationResult<SalesProductMasterWriteResult>.Success(acquisition.ReplayResult!));
            }
            if (acquisition.Kind == CommandAcquisitionKind.Conflict)
            {
                return Rollback(ApplicationErrorKind.Conflict, SalesProductMasterErrorCodes.IdempotencyKeyReused);
            }

            var state = await LockAsync(execution.Command.SalesProductId, ct);
            if (state is null)
            {
                return Rollback(ApplicationErrorKind.NotFound, SalesProductMasterErrorCodes.ItemNotFound);
            }
            if (state.DeletedAt is not null)
            {
                return Rollback(ApplicationErrorKind.Conflict, SalesProductMasterErrorCodes.ItemDeleted);
            }
            if (state.RowVersion != execution.Command.ExpectedRowVersion)
            {
                return Rollback(ApplicationErrorKind.Conflict, SalesProductMasterErrorCodes.StaleRowVersion);
            }
            if (!await ProductGroupExistsAsync(execution.Command.SalesProductGroupId, ct))
            {
                return Rollback(ApplicationErrorKind.NotFound, SalesProductMasterErrorCodes.ProductGroupNotFound);
            }
            if (execution.Command.DefaultStorageLocationId is { } storageLocationId
                && !await StorageLocationExistsAsync(storageLocationId, ct))
            {
                return Rollback(ApplicationErrorKind.NotFound, SalesProductMasterErrorCodes.StorageLocationNotFound);
            }

            long? nextRowVersion;
            await using (var command = _support.CreateSqlCommand(
                """
                UPDATE product.sales_products
                SET sales_product_group_id = @sales_product_group_id,
                    name_zh_tw = @name_zh_tw,
                    name_th_th = @name_th_th,
                    pricing_basis = @pricing_basis,
                    packaging_weight = @packaging_weight,
                    sales_weight = @sales_weight,
                    default_storage_location_id = @default_storage_location_id,
                    active = @active,
                    row_version = row_version + 1
                WHERE id = @id AND row_version = @expected_row_version AND deleted_at IS NULL
                RETURNING row_version;
                """))
            {
                AddWriteParameters(command, execution.Command.SalesProductId, execution.Command.SalesProductGroupId,
                    execution.Command.NameZhTw, execution.Command.NameThTh, execution.Command.PricingBasis.ToString(),
                    execution.Command.PackagingWeight, execution.Command.SalesWeight,
                    execution.Command.DefaultStorageLocationId, execution.Command.Active);
                command.Parameters.AddWithValue("expected_row_version", execution.Command.ExpectedRowVersion);
                var value = await command.ExecuteScalarAsync(ct);
                nextRowVersion = value is null ? null : Convert.ToInt64(value);
            }
            if (nextRowVersion is null)
            {
                return Rollback(ApplicationErrorKind.Conflict, SalesProductMasterErrorCodes.StaleRowVersion);
            }

            var result = new SalesProductMasterWriteResult(execution.Command.SalesProductId, nextRowVersion.Value);
            await _support.AppendAuditAsync(execution.CommandId, execution.ActorAccountId, UpdateCommandType,
                "product.sales-product", execution.Command.SalesProductId, "UPDATE",
                state.RowVersion, nextRowVersion.Value, now, ct);
            await _support.MarkSucceededAsync(execution.CommandId, result, now, ct);
            return CommandTransactionDecision<ApplicationResult<SalesProductMasterWriteResult>>.Commit(
                ApplicationResult<SalesProductMasterWriteResult>.Success(result));
        }, cancellationToken);
    }

    private void AddWriteParameters(
        Npgsql.NpgsqlCommand command,
        Guid id,
        Guid groupId,
        string? nameZhTw,
        string? nameThTh,
        string pricingBasis,
        decimal? packagingWeight,
        decimal? salesWeight,
        Guid? storageLocationId,
        bool active)
    {
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("sales_product_group_id", groupId);
        ProductMasterCommandSupport.AddNullableText(command, "name_zh_tw", nameZhTw);
        ProductMasterCommandSupport.AddNullableText(command, "name_th_th", nameThTh);
        command.Parameters.AddWithValue("pricing_basis", pricingBasis);
        ProductMasterCommandSupport.AddNullableNumeric(command, "packaging_weight", packagingWeight);
        ProductMasterCommandSupport.AddNullableNumeric(command, "sales_weight", salesWeight);
        ProductMasterCommandSupport.AddNullableGuid(command, "default_storage_location_id", storageLocationId);
        command.Parameters.AddWithValue("active", active);
    }

    private async ValueTask<ProductState?> LockAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = _support.CreateSqlCommand(
            "SELECT row_version, deleted_at FROM product.sales_products WHERE id = @id FOR UPDATE;");
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ProductState(reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1))
            : null;
    }

    private async ValueTask<bool> ProductGroupExistsAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = _support.CreateSqlCommand("SELECT 1 FROM product.sales_product_groups WHERE id = @id;");
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private async ValueTask<bool> StorageLocationExistsAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = _support.CreateSqlCommand("SELECT 1 FROM infrastructure.storage_locations WHERE id = @id;");
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static CommandTransactionDecision<ApplicationResult<SalesProductMasterWriteResult>> Rollback(
        ApplicationErrorKind kind,
        string code) =>
        CommandTransactionDecision<ApplicationResult<SalesProductMasterWriteResult>>.Rollback(
            ApplicationResult<SalesProductMasterWriteResult>.Failure(ApplicationError.Create(kind, code)));

    private sealed record ProductState(long RowVersion, DateTimeOffset? DeletedAt);
}
