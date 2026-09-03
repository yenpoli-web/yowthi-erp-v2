using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Inventory;
using YowThi.Erp.Domain.Inventory;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class InventoryManualCommandIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Transfer_inventory_moves_full_identity_atomically_and_replays()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            var command = new TransferInventoryCommand(
                Identity(scenario),
                scenario.SourceLocationId,
                scenario.DestinationLocationId,
                4m);
            var execution = TransferExecution(commandId, scenario.ActorAccountId, Hash(1), command);

            var result = await ExecuteTransferAsync(execution, cancellationToken);
            Assert.True(result.IsSuccess);
            Assert.Equal(4m, result.Value.Quantity);

            var replay = await ExecuteTransferAsync(execution, cancellationToken);
            Assert.True(replay.IsSuccess);
            Assert.Equal(result.Value, replay.Value);

            var changedHash = await ExecuteTransferAsync(
                TransferExecution(commandId, scenario.ActorAccountId, Hash(2), command),
                cancellationToken);
            Assert.True(changedHash.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, changedHash.Error.Kind);
            Assert.Equal(InventoryApplicationErrorCodes.IdempotencyKeyReused, changedHash.Error.Code);

            Assert.Equal(6m, await PositionBalanceAsync(scenario, scenario.SourceLocationId, cancellationToken));
            Assert.Equal(2L, await PositionVersionAsync(scenario, scenario.SourceLocationId, cancellationToken));
            Assert.Equal(4m, await PositionBalanceAsync(scenario, scenario.DestinationLocationId, cancellationToken));
            Assert.Equal(1L, await PositionVersionAsync(scenario, scenario.DestinationLocationId, cancellationToken));

            Assert.Equal(
                2L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM inventory.inventory_movements WHERE inventory_operation_id = @operation_id;",
                    cancellationToken,
                    ("operation_id", result.Value.InventoryOperationId)));
            Assert.Equal(
                -4m,
                await ScalarAsync<decimal>(
                    "SELECT quantity_delta FROM inventory.inventory_movements WHERE inventory_operation_id = @operation_id AND sequence = 1 AND movement_type = 'TRANSFER_OUT';",
                    cancellationToken,
                    ("operation_id", result.Value.InventoryOperationId)));
            Assert.Equal(
                4m,
                await ScalarAsync<decimal>(
                    "SELECT quantity_delta FROM inventory.inventory_movements WHERE inventory_operation_id = @operation_id AND sequence = 2 AND movement_type = 'TRANSFER_IN';",
                    cancellationToken,
                    ("operation_id", result.Value.InventoryOperationId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id AND status = 'SUCCEEDED';",
                    cancellationToken,
                    ("command_id", commandId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = @command_id AND command_type = 'TransferInventory';",
                    cancellationToken,
                    ("command_id", commandId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.outbox_messages WHERE command_id = @command_id AND message_type = 'inventory.transferred';",
                    cancellationToken,
                    ("command_id", commandId)));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Transfer_inventory_blocks_insufficient_stock_and_rolls_back_command_acquisition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            var result = await ExecuteTransferAsync(
                TransferExecution(
                    commandId,
                    scenario.ActorAccountId,
                    Hash(3),
                    new TransferInventoryCommand(
                        Identity(scenario),
                        scenario.SourceLocationId,
                        scenario.DestinationLocationId,
                        11m)),
                cancellationToken);

            Assert.True(result.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, result.Error.Kind);
            Assert.Equal(InventoryApplicationErrorCodes.InsufficientStock, result.Error.Code);
            Assert.Equal(10m, await PositionBalanceAsync(scenario, scenario.SourceLocationId, cancellationToken));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", commandId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM inventory.inventory_operations WHERE recorded_by_account_id = @actor_id AND operation_type = 'TRANSFER';",
                    cancellationToken,
                    ("actor_id", scenario.ActorAccountId)));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Concurrent_transfers_from_same_position_allow_one_commit_and_never_negative_stock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(cancellationToken);
        var firstCommandId = Guid.CreateVersion7();
        var secondCommandId = Guid.CreateVersion7();
        var lockReleased = false;

        await using var lockConnection = await OpenConnectionAsync(cancellationToken);
        await using var lockTransaction = await lockConnection.BeginTransactionAsync(cancellationToken);
        await using (var lockCommand = new NpgsqlCommand(
                         "SELECT id FROM inventory.inventory_positions WHERE id = @position_id FOR UPDATE;",
                         lockConnection,
                         lockTransaction))
        {
            lockCommand.Parameters.AddWithValue("position_id", scenario.SourcePositionId);
            Assert.NotNull(await lockCommand.ExecuteScalarAsync(cancellationToken));
        }

        var command = new TransferInventoryCommand(
            Identity(scenario),
            scenario.SourceLocationId,
            scenario.DestinationLocationId,
            7m);
        var firstTask = ExecuteTransferAsync(
                TransferExecution(firstCommandId, scenario.ActorAccountId, Hash(4), command),
                cancellationToken)
            .AsTask();
        var secondTask = ExecuteTransferAsync(
                TransferExecution(secondCommandId, scenario.ActorAccountId, Hash(5), command),
                cancellationToken)
            .AsTask();

        try
        {
            await WaitForBlockedInventoryUpdatesAsync(2, cancellationToken);
            await lockTransaction.CommitAsync(cancellationToken);
            lockReleased = true;

            var results = await Task.WhenAll(firstTask, secondTask);
            var winner = Assert.Single(results, x => x.IsSuccess);
            var loser = Assert.Single(results, x => x.IsFailure);

            Assert.Equal(7m, winner.Value.Quantity);
            Assert.Equal(ApplicationErrorKind.Conflict, loser.Error.Kind);
            Assert.Equal(InventoryApplicationErrorCodes.ConcurrentChange, loser.Error.Code);
            Assert.Equal(3m, await PositionBalanceAsync(scenario, scenario.SourceLocationId, cancellationToken));
            Assert.Equal(2L, await PositionVersionAsync(scenario, scenario.SourceLocationId, cancellationToken));
            Assert.Equal(7m, await PositionBalanceAsync(scenario, scenario.DestinationLocationId, cancellationToken));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = ANY(@command_ids) AND status = 'SUCCEEDED';",
                    cancellationToken,
                    ("command_ids", new[] { firstCommandId, secondCommandId })));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM inventory.inventory_operations WHERE recorded_by_account_id = @actor_id AND operation_type = 'TRANSFER';",
                    cancellationToken,
                    ("actor_id", scenario.ActorAccountId)));
        }
        finally
        {
            if (!lockReleased)
            {
                await lockTransaction.RollbackAsync(CancellationToken.None);
            }

            try
            {
                await Task.WhenAll(firstTask, secondTask);
            }
            catch
            {
                // Ensure blocked work is released before fixture cleanup.
            }

            await CleanupScenarioAsync(
                scenario,
                new[] { firstCommandId, secondCommandId },
                cancellationToken);
        }
    }

    [Fact]
    public async Task Adjust_inventory_applies_signed_deltas_allows_negative_projection_replays_and_audits_reason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(cancellationToken);
        var positiveCommandId = Guid.CreateVersion7();
        var negativeCommandId = Guid.CreateVersion7();

        try
        {
            var positiveCommand = new AdjustInventoryCommand(
                Identity(scenario),
                scenario.AdjustmentLocationId,
                5m,
                "count correction plus five");
            var positiveExecution = AdjustmentExecution(
                positiveCommandId,
                scenario.ActorAccountId,
                Hash(6),
                positiveCommand);
            var positive = await ExecuteAdjustmentAsync(positiveExecution, cancellationToken);
            Assert.True(positive.IsSuccess);
            Assert.Equal(5m, positive.Value.QuantityDelta);

            var negative = await ExecuteAdjustmentAsync(
                AdjustmentExecution(
                    negativeCommandId,
                    scenario.ActorAccountId,
                    Hash(7),
                    new AdjustInventoryCommand(
                        Identity(scenario),
                        scenario.AdjustmentLocationId,
                        -7m,
                        "count correction minus seven")),
                cancellationToken);
            Assert.True(negative.IsSuccess);
            Assert.Equal(-7m, negative.Value.QuantityDelta);

            var replay = await ExecuteAdjustmentAsync(positiveExecution, cancellationToken);
            Assert.True(replay.IsSuccess);
            Assert.Equal(positive.Value, replay.Value);

            Assert.Equal(-2m, await PositionBalanceAsync(scenario, scenario.AdjustmentLocationId, cancellationToken));
            Assert.Equal(2L, await PositionVersionAsync(scenario, scenario.AdjustmentLocationId, cancellationToken));
            Assert.Equal(
                2L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM inventory.inventory_operations WHERE recorded_by_account_id = @actor_id AND operation_type = 'ADJUSTMENT';",
                    cancellationToken,
                    ("actor_id", scenario.ActorAccountId)));
            Assert.Equal(
                -2m,
                await ScalarAsync<decimal>(
                    """
                    SELECT sum(m.quantity_delta)
                    FROM inventory.inventory_movements m
                    JOIN inventory.inventory_operations o ON o.id = m.inventory_operation_id
                    WHERE o.recorded_by_account_id = @actor_id
                      AND o.operation_type = 'ADJUSTMENT';
                    """,
                    cancellationToken,
                    ("actor_id", scenario.ActorAccountId)));
            Assert.Equal(
                "count correction plus five",
                await ScalarAsync<string>(
                    "SELECT reason_text FROM audit.audit_events WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", positiveCommandId)));
            Assert.Equal(
                2L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = ANY(@command_ids) AND status = 'SUCCEEDED';",
                    cancellationToken,
                    ("command_ids", new[] { positiveCommandId, negativeCommandId })));
        }
        finally
        {
            await CleanupScenarioAsync(
                scenario,
                new[] { positiveCommandId, negativeCommandId },
                cancellationToken);
        }
    }

    private static InventoryPositionIdentity Identity(Scenario scenario) =>
        new(
            InventoryOrigin.IN_HOUSE,
            scenario.ProcurementBatchId,
            null,
            InventoryObjectKind.PROCUREMENT_PRODUCT,
            scenario.ProcurementProductId,
            null,
            null,
            InventoryRawSourceKind.SUPPLIER,
            scenario.SupplierId);

    private static TransferInventoryExecution TransferExecution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        TransferInventoryCommand command) =>
        new(CommandId.From(commandId), requestHash, ActorAccountId.From(actorAccountId), command);

    private static AdjustInventoryExecution AdjustmentExecution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        AdjustInventoryCommand command) =>
        new(CommandId.From(commandId), requestHash, ActorAccountId.From(actorAccountId), command);

    private static CommandRequestHash Hash(byte value)
    {
        var bytes = new byte[CommandRequestHash.Sha256Length];
        Array.Fill(bytes, value);
        return CommandRequestHash.FromSha256(bytes);
    }

    private static async ValueTask<ApplicationResult<TransferInventoryResult>> ExecuteTransferAsync(
        TransferInventoryExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<ITransferInventoryExecutor>()
            .ExecuteAsync(execution, cancellationToken);
    }

    private static async ValueTask<ApplicationResult<AdjustInventoryResult>> ExecuteAdjustmentAsync(
        AdjustInventoryExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IAdjustInventoryExecutor>()
            .ExecuteAsync(execution, cancellationToken);
    }

    private static async Task<Scenario> SeedScenarioAsync(CancellationToken cancellationToken)
    {
        var scenario = new Scenario(
            ActorAccountId: Guid.CreateVersion7(),
            WarehouseId: Guid.CreateVersion7(),
            SourceLocationId: Guid.CreateVersion7(),
            DestinationLocationId: Guid.CreateVersion7(),
            AdjustmentLocationId: Guid.CreateVersion7(),
            SupplierId: Guid.CreateVersion7(),
            ProcurementProductId: Guid.CreateVersion7(),
            ProcurementBatchId: Guid.CreateVersion7(),
            SourcePositionId: Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 V7 inventory actor', true, NULL, NULL, @now);

            INSERT INTO infrastructure.warehouses
                (id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@warehouse_id, NULL, 'P6 V7 warehouse', NULL, true, @now, @actor_id);

            INSERT INTO infrastructure.storage_locations
                (id, warehouse_id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@source_location_id, @warehouse_id, NULL, 'P6 V7 source', NULL, true, @now, @actor_id),
                (@destination_location_id, @warehouse_id, NULL, 'P6 V7 destination', NULL, true, @now, @actor_id),
                (@adjustment_location_id, @warehouse_id, NULL, 'P6 V7 adjustment', NULL, true, @now, @actor_id);

            INSERT INTO party.suppliers
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, created_at, created_by_account_id)
            VALUES
                (@supplier_id, 'P6 V7 supplier', NULL, NULL, NULL, NULL, NULL,
                 true, @now, @actor_id);

            INSERT INTO product.procurement_products
                (id, name_zh_tw, name_th_th, unit_code, default_storage_location_id,
                 active, created_at, created_by_account_id)
            VALUES
                (@product_id, 'P6 V7 product', NULL, 'kg', @source_location_id,
                 true, @now, @actor_id);

            INSERT INTO procurement.procurement_batches
                (id, procurement_date, procurement_product_id, procurement_status,
                 lifecycle_status, processing_route_id, processing_route_version_id,
                 completed_at, completed_by_account_id, closed_at, closed_by_account_id,
                 created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@batch_id, DATE '2026-09-03', @product_id, 'OPEN',
                 'ACTIVE', NULL, NULL,
                 NULL, NULL, NULL, NULL,
                 @now, @actor_id, NULL, NULL);

            INSERT INTO inventory.inventory_positions
                (id, origin, procurement_batch_id, outsourced_supply_batch_id,
                 inventory_object_kind, procurement_product_id, process_material_id,
                 sales_product_id, storage_location_id, raw_source_kind, supplier_id,
                 balance_quantity, row_version)
            VALUES
                (@position_id, 'IN_HOUSE', @batch_id, NULL,
                 'PROCUREMENT_PRODUCT', @product_id, NULL,
                 NULL, @source_location_id, 'SUPPLIER', @supplier_id,
                 10, 1);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("warehouse_id", scenario.WarehouseId),
            ("source_location_id", scenario.SourceLocationId),
            ("destination_location_id", scenario.DestinationLocationId),
            ("adjustment_location_id", scenario.AdjustmentLocationId),
            ("supplier_id", scenario.SupplierId),
            ("product_id", scenario.ProcurementProductId),
            ("batch_id", scenario.ProcurementBatchId),
            ("position_id", scenario.SourcePositionId),
            ("now", now));

        return scenario;
    }

    private static async Task<decimal> PositionBalanceAsync(
        Scenario scenario,
        Guid storageLocationId,
        CancellationToken cancellationToken) =>
        await ScalarAsync<decimal>(
            """
            SELECT balance_quantity
            FROM inventory.inventory_positions
            WHERE origin = 'IN_HOUSE'
              AND procurement_batch_id = @batch_id
              AND outsourced_supply_batch_id IS NULL
              AND inventory_object_kind = 'PROCUREMENT_PRODUCT'
              AND procurement_product_id = @product_id
              AND process_material_id IS NULL
              AND sales_product_id IS NULL
              AND storage_location_id = @location_id
              AND raw_source_kind = 'SUPPLIER'
              AND supplier_id = @supplier_id;
            """,
            cancellationToken,
            ("batch_id", scenario.ProcurementBatchId),
            ("product_id", scenario.ProcurementProductId),
            ("location_id", storageLocationId),
            ("supplier_id", scenario.SupplierId));

    private static async Task<long> PositionVersionAsync(
        Scenario scenario,
        Guid storageLocationId,
        CancellationToken cancellationToken) =>
        await ScalarAsync<long>(
            """
            SELECT row_version
            FROM inventory.inventory_positions
            WHERE origin = 'IN_HOUSE'
              AND procurement_batch_id = @batch_id
              AND outsourced_supply_batch_id IS NULL
              AND inventory_object_kind = 'PROCUREMENT_PRODUCT'
              AND procurement_product_id = @product_id
              AND process_material_id IS NULL
              AND sales_product_id IS NULL
              AND storage_location_id = @location_id
              AND raw_source_kind = 'SUPPLIER'
              AND supplier_id = @supplier_id;
            """,
            cancellationToken,
            ("batch_id", scenario.ProcurementBatchId),
            ("product_id", scenario.ProcurementProductId),
            ("location_id", storageLocationId),
            ("supplier_id", scenario.SupplierId));

    private static async Task WaitForBlockedInventoryUpdatesAsync(
        long expectedCount,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var blocked = await ScalarAsync<long>(
                """
                SELECT count(*)
                FROM pg_stat_activity
                WHERE datname = current_database()
                  AND state = 'active'
                  AND wait_event_type = 'Lock'
                  AND query LIKE '%UPDATE inventory.inventory_positions%';
                """,
                cancellationToken);

            if (blocked >= expectedCount)
            {
                return;
            }

            await Task.Delay(25, cancellationToken);
        }

        Assert.Fail($"Expected at least {expectedCount} inventory transfer updates to be blocked concurrently.");
    }

    private static async Task CleanupScenarioAsync(
        Scenario scenario,
        IReadOnlyCollection<Guid> commandIds,
        CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            """
            DELETE FROM audit.audit_event_subjects
            WHERE audit_event_id IN (SELECT id FROM audit.audit_events WHERE command_id = ANY(@command_ids));
            DELETE FROM audit.audit_events WHERE command_id = ANY(@command_ids);
            DELETE FROM system.outbox_messages WHERE command_id = ANY(@command_ids);
            DELETE FROM system.command_executions WHERE command_id = ANY(@command_ids);

            DELETE FROM inventory.inventory_movements
            WHERE inventory_operation_id IN (
                SELECT id FROM inventory.inventory_operations WHERE recorded_by_account_id = @actor_id);
            DELETE FROM inventory.inventory_operations WHERE recorded_by_account_id = @actor_id;
            DELETE FROM inventory.inventory_positions
            WHERE procurement_batch_id = @batch_id
              AND procurement_product_id = @product_id
              AND supplier_id = @supplier_id;

            DELETE FROM procurement.procurement_batches WHERE id = @batch_id;
            DELETE FROM product.procurement_products WHERE id = @product_id;
            DELETE FROM party.suppliers WHERE id = @supplier_id;
            DELETE FROM infrastructure.storage_locations
            WHERE id = @source_location_id OR id = @destination_location_id OR id = @adjustment_location_id;
            DELETE FROM infrastructure.warehouses WHERE id = @warehouse_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds.ToArray()),
            ("actor_id", scenario.ActorAccountId),
            ("batch_id", scenario.ProcurementBatchId),
            ("product_id", scenario.ProcurementProductId),
            ("supplier_id", scenario.SupplierId),
            ("source_location_id", scenario.SourceLocationId),
            ("destination_location_id", scenario.DestinationLocationId),
            ("adjustment_location_id", scenario.AdjustmentLocationId),
            ("warehouse_id", scenario.WarehouseId));
    }

    private static async Task<T> ScalarAsync<T>(
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return (T)(value ?? throw new InvalidOperationException("Expected scalar query to return a value."));
    }

    private static async Task ExecuteNonQueryAsync(
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string GetConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        return string.IsNullOrWhiteSpace(connectionString)
            ? LocalDevelopmentConnectionString
            : connectionString;
    }

    private static async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(GetConnectionString())
        {
            Pooling = false,
            Timeout = 5,
            CommandTimeout = 15,
        };

        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private sealed record Scenario(
        Guid ActorAccountId,
        Guid WarehouseId,
        Guid SourceLocationId,
        Guid DestinationLocationId,
        Guid AdjustmentLocationId,
        Guid SupplierId,
        Guid ProcurementProductId,
        Guid ProcurementBatchId,
        Guid SourcePositionId);
}
