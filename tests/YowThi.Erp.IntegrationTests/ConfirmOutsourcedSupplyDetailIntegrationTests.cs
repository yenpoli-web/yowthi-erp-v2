using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Outsourced;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class ConfirmOutsourcedSupplyDetailIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Weighted_detail_replay_inventory_payable_audit_and_outbox_are_atomic()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(productHasDefaultLocation: true, cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            var command = new ConfirmOutsourcedSupplyDetailCommand(
                scenario.SupplyDate,
                scenario.VendorId,
                scenario.ProductId,
                12.5m,
                100.25m,
                null);
            var execution = Execution(commandId, scenario.ActorAccountId, Hash(1), command);

            var result = await ExecuteAsync(execution, cancellationToken);
            Assert.True(result.IsSuccess);
            Assert.Equal(6516L, result.Value.AmountThb);
            Assert.Equal(scenario.DefaultLocationId, result.Value.ReceiptStorageLocationId);
            Assert.Equal(1L, result.Value.OutsourcedSupplyDetailRowVersion);

            var replay = await ExecuteAsync(execution, cancellationToken);
            Assert.True(replay.IsSuccess);
            Assert.Equal(result.Value, replay.Value);

            var changedHash = await ExecuteAsync(
                Execution(commandId, scenario.ActorAccountId, Hash(2), command),
                cancellationToken);
            Assert.True(changedHash.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, changedHash.Error.Kind);
            Assert.Equal(OutsourcedApplicationErrorCodes.IdempotencyKeyReused, changedHash.Error.Code);

            var changedActor = await ExecuteAsync(
                Execution(commandId, scenario.SecondActorAccountId, Hash(1), command),
                cancellationToken);
            Assert.True(changedActor.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, changedActor.Error.Kind);
            Assert.Equal(OutsourcedApplicationErrorCodes.IdempotencyKeyReused, changedActor.Error.Code);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM outsourced.outsourced_supply_details
                    WHERE id = @detail_id
                      AND outsourced_supply_batch_id = @batch_id
                      AND sales_product_id = @product_id
                      AND quantity = 12.5
                      AND pricing_basis_snapshot = 'WEIGHT_BASED_UNIT'
                      AND sales_weight_snapshot = 5.2
                      AND unit_price = 100.25
                      AND amount_thb = 6516;
                    """,
                    cancellationToken,
                    ("detail_id", result.Value.OutsourcedSupplyDetailId),
                    ("batch_id", result.Value.OutsourcedSupplyBatchId),
                    ("product_id", scenario.ProductId)));

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM inventory.inventory_movements m
                    JOIN inventory.inventory_operations o ON o.id = m.inventory_operation_id
                    WHERE o.id = @operation_id
                      AND o.operation_type = 'OUTSOURCED_RECEIPT'
                      AND o.outsourced_supply_detail_id = @detail_id
                      AND m.movement_type = 'OUTSOURCED_RECEIPT'
                      AND m.origin = 'OUTSOURCED'
                      AND m.outsourced_supply_batch_id = @batch_id
                      AND m.inventory_object_kind = 'SALES_PRODUCT'
                      AND m.sales_product_id = @product_id
                      AND m.storage_location_id = @location_id
                      AND m.quantity_delta = 12.5;
                    """,
                    cancellationToken,
                    ("operation_id", result.Value.InventoryOperationId),
                    ("detail_id", result.Value.OutsourcedSupplyDetailId),
                    ("batch_id", result.Value.OutsourcedSupplyBatchId),
                    ("product_id", scenario.ProductId),
                    ("location_id", scenario.DefaultLocationId)));

            Assert.Equal(
                12.5m,
                await ScalarAsync<decimal>(
                    """
                    SELECT balance_quantity
                    FROM inventory.inventory_positions
                    WHERE origin = 'OUTSOURCED'
                      AND outsourced_supply_batch_id = @batch_id
                      AND inventory_object_kind = 'SALES_PRODUCT'
                      AND sales_product_id = @product_id
                      AND storage_location_id = @location_id;
                    """,
                    cancellationToken,
                    ("batch_id", result.Value.OutsourcedSupplyBatchId),
                    ("product_id", scenario.ProductId),
                    ("location_id", scenario.DefaultLocationId)));

            Assert.Equal(
                "OUTSOURCED_VENDOR",
                await ScalarAsync<string>(
                    "SELECT payable_kind FROM finance.payables WHERE id = @payable_id AND outsourced_supply_detail_id = @detail_id;",
                    cancellationToken,
                    ("payable_id", result.Value.PayableId),
                    ("detail_id", result.Value.OutsourcedSupplyDetailId)));
            Assert.Equal(
                6516L,
                await ScalarAsync<long>(
                    "SELECT amount_thb FROM finance.payable_obligation_items WHERE outsourced_supply_detail_id = @detail_id;",
                    cancellationToken,
                    ("detail_id", result.Value.OutsourcedSupplyDetailId)));
            Assert.Equal(
                6516L,
                await ScalarAsync<long>(
                    "SELECT outstanding_thb FROM finance.payable_outstanding_positions WHERE payable_id = @payable_id;",
                    cancellationToken,
                    ("payable_id", result.Value.PayableId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id AND status = 'SUCCEEDED';",
                    cancellationToken,
                    ("command_id", commandId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = @command_id AND command_type = 'ConfirmOutsourcedSupplyDetail';",
                    cancellationToken,
                    ("command_id", commandId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.outbox_messages WHERE command_id = @command_id AND message_type = 'outsourced.supply-detail.confirmed';",
                    cancellationToken,
                    ("command_id", commandId)));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Missing_default_receipt_location_requires_explicit_choice_without_persisting_command()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(productHasDefaultLocation: false, cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            var command = new ConfirmOutsourcedSupplyDetailCommand(
                scenario.SupplyDate,
                scenario.VendorId,
                scenario.ProductId,
                10m,
                4m,
                null);

            var result = await ExecuteAsync(
                Execution(commandId, scenario.ActorAccountId, Hash(3), command),
                cancellationToken);

            Assert.True(result.IsFailure);
            Assert.Equal(ApplicationErrorKind.Validation, result.Error.Kind);
            Assert.Equal(OutsourcedApplicationErrorCodes.ReceiptLocationRequired, result.Error.Code);
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", commandId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM outsourced.outsourced_supply_batches WHERE outsourced_vendor_id = @vendor_id;",
                    cancellationToken,
                    ("vendor_id", scenario.VendorId)));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Closed_batch_blocks_normal_late_detail_without_new_facts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(productHasDefaultLocation: true, cancellationToken);
        var commandId = Guid.CreateVersion7();
        var batchId = Guid.CreateVersion7();

        try
        {
            await InsertClosedBatchAsync(batchId, scenario, cancellationToken);

            var command = new ConfirmOutsourcedSupplyDetailCommand(
                scenario.SupplyDate,
                scenario.VendorId,
                scenario.ProductId,
                10m,
                4m,
                null);

            var result = await ExecuteAsync(
                Execution(commandId, scenario.ActorAccountId, Hash(4), command),
                cancellationToken);

            Assert.True(result.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, result.Error.Kind);
            Assert.Equal(OutsourcedApplicationErrorCodes.BatchClosedLateDetailUnverified, result.Error.Code);
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM outsourced.outsourced_supply_details WHERE outsourced_supply_batch_id = @batch_id;",
                    cancellationToken,
                    ("batch_id", batchId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", commandId)));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Concurrent_same_date_vendor_commands_resolve_one_outsourced_batch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(productHasDefaultLocation: true, cancellationToken);
        var firstCommandId = Guid.CreateVersion7();
        var secondCommandId = Guid.CreateVersion7();

        try
        {
            var first = new ConfirmOutsourcedSupplyDetailCommand(
                scenario.SupplyDate,
                scenario.VendorId,
                scenario.ProductId,
                11m,
                2m,
                null);
            var second = new ConfirmOutsourcedSupplyDetailCommand(
                scenario.SupplyDate,
                scenario.VendorId,
                scenario.ProductId,
                13m,
                3m,
                null);

            var results = await Task.WhenAll(
                ExecuteAsync(Execution(firstCommandId, scenario.ActorAccountId, Hash(5), first), cancellationToken).AsTask(),
                ExecuteAsync(Execution(secondCommandId, scenario.ActorAccountId, Hash(6), second), cancellationToken).AsTask());

            Assert.All(results, result => Assert.True(result.IsSuccess));
            Assert.Equal(results[0].Value.OutsourcedSupplyBatchId, results[1].Value.OutsourcedSupplyBatchId);
            Assert.NotEqual(results[0].Value.PayableId, results[1].Value.PayableId);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM outsourced.outsourced_supply_batches
                    WHERE supply_date = @supply_date
                      AND outsourced_vendor_id = @vendor_id;
                    """,
                    cancellationToken,
                    ("supply_date", scenario.SupplyDate),
                    ("vendor_id", scenario.VendorId)));
            Assert.Equal(
                2L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM outsourced.outsourced_supply_details WHERE outsourced_supply_batch_id = @batch_id;",
                    cancellationToken,
                    ("batch_id", results[0].Value.OutsourcedSupplyBatchId)));
            Assert.Equal(
                24m,
                await ScalarAsync<decimal>(
                    """
                    SELECT balance_quantity
                    FROM inventory.inventory_positions
                    WHERE origin = 'OUTSOURCED'
                      AND outsourced_supply_batch_id = @batch_id
                      AND sales_product_id = @product_id
                      AND storage_location_id = @location_id;
                    """,
                    cancellationToken,
                    ("batch_id", results[0].Value.OutsourcedSupplyBatchId),
                    ("product_id", scenario.ProductId),
                    ("location_id", scenario.DefaultLocationId)));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, new[] { firstCommandId, secondCommandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Zero_quantity_is_blocked_as_to_verify_without_database_work()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new ConfirmOutsourcedSupplyDetailCommand(
            new DateOnly(2026, 9, 2),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            0m,
            1m,
            null);

        var result = await ExecuteAsync(
            Execution(Guid.CreateVersion7(), Guid.CreateVersion7(), Hash(7), command),
            cancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(ApplicationErrorKind.Validation, result.Error.Kind);
        Assert.Equal(OutsourcedApplicationErrorCodes.ZeroQuantityUnverified, result.Error.Code);
    }

    private static ConfirmOutsourcedSupplyDetailExecution Execution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        ConfirmOutsourcedSupplyDetailCommand command) =>
        new(
            CommandId.From(commandId),
            requestHash,
            ActorAccountId.From(actorAccountId),
            command);

    private static CommandRequestHash Hash(byte value)
    {
        var bytes = new byte[CommandRequestHash.Sha256Length];
        Array.Fill(bytes, value);
        return CommandRequestHash.FromSha256(bytes);
    }

    private static async ValueTask<ApplicationResult<ConfirmOutsourcedSupplyDetailResult>> ExecuteAsync(
        ConfirmOutsourcedSupplyDetailExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<IConfirmOutsourcedSupplyDetailExecutor>();
        return await executor.ExecuteAsync(execution, cancellationToken);
    }

    private static async Task<Scenario> SeedScenarioAsync(
        bool productHasDefaultLocation,
        CancellationToken cancellationToken)
    {
        var scenario = new Scenario(
            ActorAccountId: Guid.CreateVersion7(),
            SecondActorAccountId: Guid.CreateVersion7(),
            WarehouseId: Guid.CreateVersion7(),
            DefaultLocationId: Guid.CreateVersion7(),
            ExplicitLocationId: Guid.CreateVersion7(),
            SalesProductGroupId: Guid.CreateVersion7(),
            ProductId: Guid.CreateVersion7(),
            VendorId: Guid.CreateVersion7(),
            SupplyDate: new DateOnly(2026, 9, 2));

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 V2 actor', true, NULL, NULL, @now),
                (@second_actor_id, 'P6 V2 second actor', true, NULL, NULL, @now);

            INSERT INTO infrastructure.warehouses
                (id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@warehouse_id, NULL, 'P6 V2 warehouse', NULL, true, @now, @actor_id);

            INSERT INTO infrastructure.storage_locations
                (id, warehouse_id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@default_location_id, @warehouse_id, NULL, 'P6 V2 default location', NULL, true, @now, @actor_id),
                (@explicit_location_id, @warehouse_id, NULL, 'P6 V2 explicit location', NULL, true, @now, @actor_id);

            INSERT INTO product.sales_product_groups
                (id, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@sales_product_group_id, 'P6 V2 group', NULL, true, @now, @actor_id);

            INSERT INTO product.sales_products
                (id, sales_product_group_id, name_zh_tw, name_th_th, pricing_basis,
                 packaging_weight, sales_weight, default_storage_location_id,
                 active, created_at, created_by_account_id)
            VALUES
                (@product_id, @sales_product_group_id, 'P6 V2 product', NULL, 'WEIGHT_BASED_UNIT',
                 NULL, 5.2, @default_location_id,
                 true, @now, @actor_id);

            INSERT INTO party.outsourced_vendors
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, created_at, created_by_account_id)
            VALUES
                (@vendor_id, 'P6 V2 vendor', NULL, NULL, NULL, NULL, NULL,
                 true, @now, @actor_id);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("second_actor_id", scenario.SecondActorAccountId),
            ("warehouse_id", scenario.WarehouseId),
            ("default_location_id", scenario.DefaultLocationId),
            ("explicit_location_id", scenario.ExplicitLocationId),
            ("sales_product_group_id", scenario.SalesProductGroupId),
            ("product_id", scenario.ProductId),
            ("vendor_id", scenario.VendorId),
            ("now", DateTimeOffset.UtcNow));

        if (!productHasDefaultLocation)
        {
            await ExecuteNonQueryAsync(
                "UPDATE product.sales_products SET default_storage_location_id = NULL WHERE id = @product_id;",
                cancellationToken,
                ("product_id", scenario.ProductId));
        }

        return scenario;
    }

    private static Task InsertClosedBatchAsync(
        Guid batchId,
        Scenario scenario,
        CancellationToken cancellationToken) =>
        ExecuteNonQueryAsync(
            """
            INSERT INTO outsourced.outsourced_supply_batches
                (id, supply_date, outsourced_vendor_id, lifecycle_status,
                 closed_at, closed_by_account_id, created_at, created_by_account_id)
            VALUES
                (@batch_id, @supply_date, @vendor_id, 'CLOSED',
                 @now, @actor_id, @now, @actor_id);
            """,
            cancellationToken,
            ("batch_id", batchId),
            ("supply_date", scenario.SupplyDate),
            ("vendor_id", scenario.VendorId),
            ("now", DateTimeOffset.UtcNow),
            ("actor_id", scenario.ActorAccountId));

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

            DELETE FROM finance.payable_obligation_items
            WHERE outsourced_supply_detail_id IN (
                SELECT d.id
                FROM outsourced.outsourced_supply_details d
                JOIN outsourced.outsourced_supply_batches b ON b.id = d.outsourced_supply_batch_id
                WHERE b.outsourced_vendor_id = @vendor_id);

            DELETE FROM finance.payable_outstanding_positions
            WHERE payable_id IN (
                SELECT p.id
                FROM finance.payables p
                JOIN outsourced.outsourced_supply_details d ON d.id = p.outsourced_supply_detail_id
                JOIN outsourced.outsourced_supply_batches b ON b.id = d.outsourced_supply_batch_id
                WHERE b.outsourced_vendor_id = @vendor_id);

            DELETE FROM finance.payables
            WHERE outsourced_supply_detail_id IN (
                SELECT d.id
                FROM outsourced.outsourced_supply_details d
                JOIN outsourced.outsourced_supply_batches b ON b.id = d.outsourced_supply_batch_id
                WHERE b.outsourced_vendor_id = @vendor_id);

            DELETE FROM inventory.inventory_movements
            WHERE outsourced_supply_batch_id IN (
                SELECT id FROM outsourced.outsourced_supply_batches WHERE outsourced_vendor_id = @vendor_id);

            DELETE FROM inventory.inventory_operations
            WHERE outsourced_supply_detail_id IN (
                SELECT d.id
                FROM outsourced.outsourced_supply_details d
                JOIN outsourced.outsourced_supply_batches b ON b.id = d.outsourced_supply_batch_id
                WHERE b.outsourced_vendor_id = @vendor_id);

            DELETE FROM inventory.inventory_positions
            WHERE outsourced_supply_batch_id IN (
                SELECT id FROM outsourced.outsourced_supply_batches WHERE outsourced_vendor_id = @vendor_id);

            DELETE FROM outsourced.outsourced_supply_details
            WHERE outsourced_supply_batch_id IN (
                SELECT id FROM outsourced.outsourced_supply_batches WHERE outsourced_vendor_id = @vendor_id);
            DELETE FROM outsourced.outsourced_supply_batches WHERE outsourced_vendor_id = @vendor_id;

            DELETE FROM product.sales_products WHERE id = @product_id;
            DELETE FROM product.sales_product_groups WHERE id = @sales_product_group_id;
            DELETE FROM party.outsourced_vendors WHERE id = @vendor_id;
            DELETE FROM infrastructure.storage_locations WHERE id = @default_location_id OR id = @explicit_location_id;
            DELETE FROM infrastructure.warehouses WHERE id = @warehouse_id;
            DELETE FROM system.accounts WHERE id = @actor_id OR id = @second_actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds.ToArray()),
            ("vendor_id", scenario.VendorId),
            ("product_id", scenario.ProductId),
            ("sales_product_group_id", scenario.SalesProductGroupId),
            ("default_location_id", scenario.DefaultLocationId),
            ("explicit_location_id", scenario.ExplicitLocationId),
            ("warehouse_id", scenario.WarehouseId),
            ("actor_id", scenario.ActorAccountId),
            ("second_actor_id", scenario.SecondActorAccountId));
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
        Guid SecondActorAccountId,
        Guid WarehouseId,
        Guid DefaultLocationId,
        Guid ExplicitLocationId,
        Guid SalesProductGroupId,
        Guid ProductId,
        Guid VendorId,
        DateOnly SupplyDate);
}
