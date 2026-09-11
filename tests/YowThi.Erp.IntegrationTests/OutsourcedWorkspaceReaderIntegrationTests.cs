using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Outsourced;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class OutsourcedWorkspaceReaderIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Persisted_outsourced_detail_workspace_readback_includes_receipt_location()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            var services = new ServiceCollection();
            services.AddErpPersistence(GetConnectionString());
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();

            var executor = scope.ServiceProvider.GetRequiredService<IConfirmOutsourcedSupplyDetailExecutor>();
            var reader = scope.ServiceProvider.GetRequiredService<IOutsourcedWorkspaceReader>();
            var command = new ConfirmOutsourcedSupplyDetailCommand(
                scenario.SupplyDate,
                scenario.VendorId,
                scenario.ProductId,
                2m,
                17m,
                null);
            var requestHashBytes = new byte[CommandRequestHash.Sha256Length];
            Array.Fill(requestHashBytes, (byte)0x5A);

            var result = await executor.ExecuteAsync(
                new ConfirmOutsourcedSupplyDetailExecution(
                    CommandId.From(commandId),
                    CommandRequestHash.FromSha256(requestHashBytes),
                    ActorAccountId.From(scenario.ActorAccountId),
                    command),
                cancellationToken);

            Assert.True(result.IsSuccess);

            var workspace = await reader.GetBatchAsync(
                result.Value.OutsourcedSupplyBatchId,
                "zh-TW",
                cancellationToken);

            Assert.NotNull(workspace);
            Assert.Equal(scenario.VendorId, workspace.OutsourcedVendorId);
            var detail = Assert.Single(workspace.Details);
            Assert.Equal(result.Value.OutsourcedSupplyDetailId, detail.Id);
            Assert.Equal(scenario.ProductId, detail.SalesProductId);
            Assert.Equal(scenario.DefaultLocationId, detail.ReceiptStorageLocationId);
            Assert.Equal("Workspace receipt location", detail.ReceiptStorageLocationDisplayName);
        }
        finally
        {
            await CleanupScenarioAsync(scenario, commandId, cancellationToken);
        }
    }

    private static async Task<Scenario> SeedScenarioAsync(CancellationToken cancellationToken)
    {
        var scenario = new Scenario(
            ActorAccountId: Guid.CreateVersion7(),
            WarehouseId: Guid.CreateVersion7(),
            DefaultLocationId: Guid.CreateVersion7(),
            SalesProductGroupId: Guid.CreateVersion7(),
            ProductId: Guid.CreateVersion7(),
            VendorId: Guid.CreateVersion7(),
            SupplyDate: new DateOnly(2026, 9, 11));

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'Workspace reader actor', true, NULL, NULL, @now);

            INSERT INTO infrastructure.warehouses
                (id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@warehouse_id, NULL, 'Workspace warehouse', NULL, true, @now, @actor_id);

            INSERT INTO infrastructure.storage_locations
                (id, warehouse_id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@default_location_id, @warehouse_id, NULL, 'Workspace receipt location', NULL, true, @now, @actor_id);

            INSERT INTO product.sales_product_groups
                (id, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@sales_product_group_id, 'Workspace group', NULL, true, @now, @actor_id);

            INSERT INTO product.sales_products
                (id, sales_product_group_id, name_zh_tw, name_th_th, pricing_basis,
                 packaging_weight, sales_weight, default_storage_location_id,
                 active, created_at, created_by_account_id)
            VALUES
                (@product_id, @sales_product_group_id, 'Workspace product', NULL, 'UNIT_BASED',
                 NULL, NULL, @default_location_id,
                 true, @now, @actor_id);

            INSERT INTO party.outsourced_vendors
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, created_at, created_by_account_id)
            VALUES
                (@vendor_id, 'Workspace vendor', NULL, NULL, NULL, NULL, NULL,
                 true, @now, @actor_id);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("warehouse_id", scenario.WarehouseId),
            ("default_location_id", scenario.DefaultLocationId),
            ("sales_product_group_id", scenario.SalesProductGroupId),
            ("product_id", scenario.ProductId),
            ("vendor_id", scenario.VendorId),
            ("now", DateTimeOffset.UtcNow));

        return scenario;
    }

    private static async Task CleanupScenarioAsync(
        Scenario scenario,
        Guid commandId,
        CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            """
            DELETE FROM audit.audit_event_subjects
            WHERE audit_event_id IN (SELECT id FROM audit.audit_events WHERE command_id = @command_id);
            DELETE FROM audit.audit_events WHERE command_id = @command_id;
            DELETE FROM system.outbox_messages WHERE command_id = @command_id;
            DELETE FROM system.command_executions WHERE command_id = @command_id;

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
            DELETE FROM infrastructure.storage_locations WHERE id = @default_location_id;
            DELETE FROM infrastructure.warehouses WHERE id = @warehouse_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("command_id", commandId),
            ("vendor_id", scenario.VendorId),
            ("product_id", scenario.ProductId),
            ("sales_product_group_id", scenario.SalesProductGroupId),
            ("default_location_id", scenario.DefaultLocationId),
            ("warehouse_id", scenario.WarehouseId),
            ("actor_id", scenario.ActorAccountId));
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
        Guid DefaultLocationId,
        Guid SalesProductGroupId,
        Guid ProductId,
        Guid VendorId,
        DateOnly SupplyDate);
}
