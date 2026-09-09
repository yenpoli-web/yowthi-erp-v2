using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Procurement;
using YowThi.Erp.Domain.Procurement;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class ProcurementTransactionLifecycleIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Batch_and_entry_lifecycle_and_entry_hard_delete_preserve_siblings_and_reverse_projections()
    {
        var ct = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(ct);
        var commandIds = Enumerable.Range(1, 8).Select(_ => Guid.CreateVersion7()).ToArray();
        ConfirmProcurementEntryResult first = default!;
        ConfirmProcurementEntryResult second = default!;

        try
        {
            first = (await ConfirmAsync(
                commandIds[0], scenario.ActorAccountId, Hash(1),
                new ConfirmProcurementEntryCommand(
                    scenario.ProcurementDate,
                    scenario.ProductId,
                    ProcurementSourceType.SUPPLIER,
                    scenario.SupplierId,
                    null,
                    10m,
                    20m,
                    false,
                    null), ct)).Value;

            second = (await ConfirmAsync(
                commandIds[1], scenario.ActorAccountId, Hash(2),
                new ConfirmProcurementEntryCommand(
                    scenario.ProcurementDate,
                    scenario.ProductId,
                    ProcurementSourceType.SUPPLIER,
                    scenario.SupplierId,
                    null,
                    5m,
                    30m,
                    true,
                    null), ct)).Value;

            Assert.Equal(first.ProcurementBatchId, second.ProcurementBatchId);
            Assert.Equal(first.PayableId, second.PayableId);
            Assert.Equal(15m, await InventoryBalanceAsync(first.ProcurementBatchId, scenario, ct));
            Assert.Equal(350L, await ScalarAsync<long>(
                "SELECT original_obligation_thb FROM finance.payable_outstanding_positions WHERE payable_id = @id;",
                ct, ("id", first.PayableId)));
            Assert.Equal(350L, await ScalarAsync<long>(
                "SELECT outstanding_thb FROM finance.payable_outstanding_positions WHERE payable_id = @id;",
                ct, ("id", first.PayableId)));

            var batchSoft = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IProcurementTransactionLifecycleExecutor>().SoftDeleteBatchAsync(
                    new SoftDeleteProcurementBatchExecution(
                        CommandId.From(commandIds[2]), Hash(3), ActorAccountId.From(scenario.ActorAccountId),
                        new SoftDeleteProcurementBatchCommand(first.ProcurementBatchId, 1)), token), ct);
            Assert.True(batchSoft.IsSuccess);
            Assert.True(batchSoft.Value.Deleted);
            Assert.Equal(2, batchSoft.Value.RowVersion);

            var batchRestore = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IProcurementTransactionLifecycleExecutor>().RestoreBatchAsync(
                    new RestoreProcurementBatchExecution(
                        CommandId.From(commandIds[3]), Hash(4), ActorAccountId.From(scenario.ActorAccountId),
                        new RestoreProcurementBatchCommand(first.ProcurementBatchId, 2)), token), ct);
            Assert.True(batchRestore.IsSuccess);
            Assert.False(batchRestore.Value.Deleted);
            Assert.Equal(3, batchRestore.Value.RowVersion);

            var entrySoftExecution = new SoftDeleteProcurementEntryExecution(
                CommandId.From(commandIds[4]), Hash(5), ActorAccountId.From(scenario.ActorAccountId),
                new SoftDeleteProcurementEntryCommand(first.ProcurementEntryId, 1));
            var entrySoft = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IProcurementTransactionLifecycleExecutor>().SoftDeleteEntryAsync(entrySoftExecution, token), ct);
            Assert.True(entrySoft.IsSuccess);
            Assert.True(entrySoft.Value.Deleted);
            Assert.Equal(2, entrySoft.Value.RowVersion);

            var entrySoftReplay = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IProcurementTransactionLifecycleExecutor>().SoftDeleteEntryAsync(entrySoftExecution, token), ct);
            Assert.True(entrySoftReplay.IsSuccess);
            Assert.Equal(entrySoft.Value, entrySoftReplay.Value);
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM procurement.procurement_entries WHERE id = @id AND deleted_at IS NOT NULL;",
                ct, ("id", first.ProcurementEntryId)));
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM procurement.procurement_entries WHERE id = @id AND deleted_at IS NOT NULL;",
                ct, ("id", second.ProcurementEntryId)));

            var entryRestore = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IProcurementTransactionLifecycleExecutor>().RestoreEntryAsync(
                    new RestoreProcurementEntryExecution(
                        CommandId.From(commandIds[5]), Hash(6), ActorAccountId.From(scenario.ActorAccountId),
                        new RestoreProcurementEntryCommand(first.ProcurementEntryId, 2)), token), ct);
            Assert.True(entryRestore.IsSuccess);
            Assert.Equal(3, entryRestore.Value.RowVersion);
            Assert.False(entryRestore.Value.Deleted);

            var staleHardDelete = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IHardDeleteProcurementEntryExecutor>().ExecuteAsync(
                    new HardDeleteProcurementEntryExecution(
                        CommandId.From(commandIds[6]), Hash(7), ActorAccountId.From(scenario.ActorAccountId),
                        new HardDeleteProcurementEntryCommand(first.ProcurementEntryId, 2)), token), ct);
            Assert.True(staleHardDelete.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, staleHardDelete.Error.Kind);
            Assert.Equal(ProcurementTransactionLifecycleErrorCodes.StaleRowVersion, staleHardDelete.Error.Code);
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM system.command_executions WHERE command_id = @id;",
                ct, ("id", commandIds[6])));

            var hardExecution = new HardDeleteProcurementEntryExecution(
                CommandId.From(commandIds[7]), Hash(8), ActorAccountId.From(scenario.ActorAccountId),
                new HardDeleteProcurementEntryCommand(first.ProcurementEntryId, 3));
            var hardDelete = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IHardDeleteProcurementEntryExecutor>().ExecuteAsync(hardExecution, token), ct);
            Assert.True(hardDelete.IsSuccess);
            Assert.Equal(first.ProcurementEntryId, hardDelete.Value.ProcurementEntryId);

            var hardReplay = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IHardDeleteProcurementEntryExecutor>().ExecuteAsync(hardExecution, token), ct);
            Assert.True(hardReplay.IsSuccess);
            Assert.Equal(hardDelete.Value, hardReplay.Value);

            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM procurement.procurement_entries WHERE id = @id;",
                ct, ("id", first.ProcurementEntryId)));
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM procurement.procurement_entries WHERE id = @id;",
                ct, ("id", second.ProcurementEntryId)));
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM inventory.inventory_operations WHERE id = @id;",
                ct, ("id", first.InventoryOperationId)));
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM inventory.inventory_operations WHERE id = @id;",
                ct, ("id", second.InventoryOperationId)));
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM finance.payable_obligation_items WHERE procurement_entry_id = @id;",
                ct, ("id", first.ProcurementEntryId)));
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM finance.payable_obligation_items WHERE procurement_entry_id = @id;",
                ct, ("id", second.ProcurementEntryId)));
            Assert.Equal(5m, await InventoryBalanceAsync(first.ProcurementBatchId, scenario, ct));
            Assert.Equal(150L, await ScalarAsync<long>(
                "SELECT original_obligation_thb FROM finance.payable_outstanding_positions WHERE payable_id = @id;",
                ct, ("id", first.PayableId)));
            Assert.Equal(150L, await ScalarAsync<long>(
                "SELECT outstanding_thb FROM finance.payable_outstanding_positions WHERE payable_id = @id;",
                ct, ("id", first.PayableId)));
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM system.outbox_messages WHERE command_id = @id;",
                ct, ("id", commandIds[0])));
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM system.outbox_messages WHERE command_id = @id;",
                ct, ("id", commandIds[1])));
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM audit.audit_events WHERE command_id = @id AND event_kind = 'HARD_DELETE';",
                ct, ("id", commandIds[7])));
        }
        finally
        {
            await CleanupAsync(scenario, first, second, commandIds, ct);
        }
    }

    private static async ValueTask<ApplicationResult<ConfirmProcurementEntryResult>> ConfirmAsync(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash hash,
        ConfirmProcurementEntryCommand command,
        CancellationToken ct) =>
        await ExecuteAsync(
            (services, token) => services.GetRequiredService<IConfirmProcurementEntryExecutor>().ExecuteAsync(
                new ConfirmProcurementEntryExecution(CommandId.From(commandId), hash, ActorAccountId.From(actorAccountId), command), token),
            ct);

    private static async Task<Scenario> SeedScenarioAsync(CancellationToken ct)
    {
        var scenario = new Scenario(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            new DateOnly(2026, 9, 9));
        var now = DateTimeOffset.UtcNow;

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES (@actor, 'Transaction lifecycle actor', true, NULL, NULL, @now);

            INSERT INTO infrastructure.warehouses
                (id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES (@warehouse, 'WH-TL', '交易刪除倉庫', NULL, true, @now, @actor);

            INSERT INTO infrastructure.storage_locations
                (id, warehouse_id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES (@location, @warehouse, 'LOC-TL', '交易刪除儲位', NULL, true, @now, @actor);

            INSERT INTO product.procurement_products
                (id, name_zh_tw, name_th_th, unit_code, default_storage_location_id, active, created_at, created_by_account_id)
            VALUES (@product, '交易刪除採購品', NULL, 'kg', @location, true, @now, @actor);

            INSERT INTO party.suppliers
                (id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES (@supplier, 'SUP-TL', '交易刪除供應商', NULL, true, @now, @actor);
            """,
            ct,
            ("actor", scenario.ActorAccountId), ("warehouse", scenario.WarehouseId),
            ("location", scenario.LocationId), ("product", scenario.ProductId),
            ("supplier", scenario.SupplierId), ("now", now));

        return scenario;
    }

    private static async Task CleanupAsync(
        Scenario scenario,
        ConfirmProcurementEntryResult? first,
        ConfirmProcurementEntryResult? second,
        IReadOnlyCollection<Guid> commandIds,
        CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            """
            DELETE FROM audit.audit_event_subjects
            WHERE audit_event_id IN (SELECT id FROM audit.audit_events WHERE command_id = ANY(@commands));
            DELETE FROM audit.audit_events WHERE command_id = ANY(@commands);
            DELETE FROM system.outbox_messages WHERE command_id = ANY(@commands);
            DELETE FROM system.command_executions WHERE command_id = ANY(@commands);

            DELETE FROM finance.company_pickup_transport_bases
            WHERE procurement_entry_id IN (
                SELECT id FROM procurement.procurement_entries WHERE procurement_batch_id IN (
                    SELECT id FROM procurement.procurement_batches WHERE procurement_product_id = @product));
            DELETE FROM finance.payable_obligation_items
            WHERE payable_id IN (SELECT id FROM finance.payables WHERE procurement_batch_id IN (
                SELECT id FROM procurement.procurement_batches WHERE procurement_product_id = @product));
            DELETE FROM finance.payable_outstanding_positions
            WHERE payable_id IN (SELECT id FROM finance.payables WHERE procurement_batch_id IN (
                SELECT id FROM procurement.procurement_batches WHERE procurement_product_id = @product));
            DELETE FROM finance.payables
            WHERE procurement_batch_id IN (SELECT id FROM procurement.procurement_batches WHERE procurement_product_id = @product);

            DELETE FROM inventory.inventory_movements
            WHERE inventory_operation_id IN (SELECT id FROM inventory.inventory_operations WHERE procurement_entry_id IN (
                SELECT id FROM procurement.procurement_entries WHERE procurement_batch_id IN (
                    SELECT id FROM procurement.procurement_batches WHERE procurement_product_id = @product)));
            DELETE FROM inventory.inventory_operations
            WHERE procurement_entry_id IN (SELECT id FROM procurement.procurement_entries WHERE procurement_batch_id IN (
                SELECT id FROM procurement.procurement_batches WHERE procurement_product_id = @product));
            DELETE FROM inventory.inventory_positions
            WHERE procurement_batch_id IN (SELECT id FROM procurement.procurement_batches WHERE procurement_product_id = @product);

            DELETE FROM procurement.procurement_entries
            WHERE procurement_batch_id IN (SELECT id FROM procurement.procurement_batches WHERE procurement_product_id = @product);
            DELETE FROM procurement.procurement_batches WHERE procurement_product_id = @product;
            DELETE FROM party.suppliers WHERE id = @supplier;
            DELETE FROM product.procurement_products WHERE id = @product;
            DELETE FROM infrastructure.storage_locations WHERE id = @location;
            DELETE FROM infrastructure.warehouses WHERE id = @warehouse;
            DELETE FROM system.accounts WHERE id = @actor;
            """,
            ct,
            ("commands", commandIds.ToArray()), ("product", scenario.ProductId), ("supplier", scenario.SupplierId),
            ("location", scenario.LocationId), ("warehouse", scenario.WarehouseId), ("actor", scenario.ActorAccountId));
    }

    private static async ValueTask<TResult> ExecuteAsync<TResult>(
        Func<IServiceProvider, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken ct)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await operation(scope.ServiceProvider, ct);
    }

    private static CommandRequestHash Hash(byte value)
    {
        var bytes = new byte[CommandRequestHash.Sha256Length];
        Array.Fill(bytes, value);
        return CommandRequestHash.FromSha256(bytes);
    }

    private static async Task<decimal> InventoryBalanceAsync(Guid batchId, Scenario scenario, CancellationToken ct) =>
        await ScalarAsync<decimal>(
            """
            SELECT balance_quantity
            FROM inventory.inventory_positions
            WHERE procurement_batch_id = @batch
              AND procurement_product_id = @product
              AND storage_location_id = @location
              AND raw_source_kind = 'SUPPLIER'
              AND supplier_id = @supplier;
            """,
            ct,
            ("batch", batchId), ("product", scenario.ProductId), ("location", scenario.LocationId), ("supplier", scenario.SupplierId));

    private static async Task<T> ScalarAsync<T>(string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        var value = await command.ExecuteScalarAsync(ct);
        if (value is null || value is DBNull) return default!;
        return (T)value;
    }

    private static async Task ExecuteNonQueryAsync(string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string GetConnectionString()
    {
        var value = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        return string.IsNullOrWhiteSpace(value) ? LocalDevelopmentConnectionString : value;
    }

    private static async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var builder = new NpgsqlConnectionStringBuilder(GetConnectionString())
        {
            Pooling = false,
            Timeout = 5,
            CommandTimeout = 15,
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    private sealed record Scenario(
        Guid ActorAccountId,
        Guid WarehouseId,
        Guid LocationId,
        Guid ProductId,
        Guid SupplierId,
        DateOnly ProcurementDate);
}
