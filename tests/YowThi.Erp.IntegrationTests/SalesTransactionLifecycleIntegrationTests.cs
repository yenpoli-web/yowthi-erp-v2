using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Sales;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class SalesTransactionLifecycleIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Confirmed_sale_and_detail_lifecycle_reverse_projections_preserve_siblings_and_support_hard_delete_replay()
    {
        var ct = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(ct);
        var commandIds = Enumerable.Range(0, 8).Select(_ => Guid.CreateVersion7()).ToArray();
        ConfirmSalesResult confirmed = default!;

        try
        {
            var confirmExecution = new ConfirmSalesExecution(
                CommandId.From(commandIds[0]),
                Hash(1),
                ActorAccountId.From(scenario.ActorAccountId),
                new ConfirmSalesCommand(
                    scenario.SalesId,
                    1,
                    Array.Empty<SalesManualAllocationOverride>()));
            var confirm = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IConfirmSalesExecutor>().ExecuteAsync(confirmExecution, token),
                ct);
            Assert.True(confirm.IsSuccess);
            confirmed = confirm.Value;
            Assert.Equal(2L, confirmed.SalesRowVersion);
            Assert.Equal(5m, await PositionBalanceAsync(scenario.InventoryPositionId, ct));
            Assert.Equal(250L, await ScalarAsync<long>(
                "SELECT outstanding_thb FROM finance.receivable_outstanding_positions WHERE receivable_id = @id;",
                ct,
                ("id", confirmed.ReceivableId)));

            var softSaleExecution = new SoftDeleteSaleExecution(
                CommandId.From(commandIds[1]),
                Hash(2),
                ActorAccountId.From(scenario.ActorAccountId),
                new SoftDeleteSaleCommand(scenario.SalesId, 2));
            var softSale = await ExecuteAsync(
                (services, token) => services.GetRequiredService<ISalesTransactionLifecycleExecutor>().SoftDeleteSaleAsync(softSaleExecution, token),
                ct);
            Assert.True(softSale.IsSuccess);
            Assert.True(softSale.Value.Deleted);
            Assert.Equal(3L, softSale.Value.RowVersion);

            var softSaleReplay = await ExecuteAsync(
                (services, token) => services.GetRequiredService<ISalesTransactionLifecycleExecutor>().SoftDeleteSaleAsync(softSaleExecution, token),
                ct);
            Assert.True(softSaleReplay.IsSuccess);
            Assert.Equal(softSale.Value, softSaleReplay.Value);

            var restoreSale = await ExecuteAsync(
                (services, token) => services.GetRequiredService<ISalesTransactionLifecycleExecutor>().RestoreSaleAsync(
                    new RestoreSaleExecution(
                        CommandId.From(commandIds[2]),
                        Hash(3),
                        ActorAccountId.From(scenario.ActorAccountId),
                        new RestoreSaleCommand(scenario.SalesId, 3)),
                    token),
                ct);
            Assert.True(restoreSale.IsSuccess);
            Assert.False(restoreSale.Value.Deleted);
            Assert.Equal(4L, restoreSale.Value.RowVersion);

            var softDetail = await ExecuteAsync(
                (services, token) => services.GetRequiredService<ISalesTransactionLifecycleExecutor>().SoftDeleteDetailAsync(
                    new SoftDeleteSalesDetailExecution(
                        CommandId.From(commandIds[3]),
                        Hash(4),
                        ActorAccountId.From(scenario.ActorAccountId),
                        new SoftDeleteSalesDetailCommand(scenario.FirstDetailId, 1)),
                    token),
                ct);
            Assert.True(softDetail.IsSuccess);
            Assert.True(softDetail.Value.Deleted);
            Assert.Equal(2L, softDetail.Value.RowVersion);
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM sales.sales_details WHERE id = @id AND deleted_at IS NOT NULL;",
                ct,
                ("id", scenario.SecondDetailId)));

            var restoreDetail = await ExecuteAsync(
                (services, token) => services.GetRequiredService<ISalesTransactionLifecycleExecutor>().RestoreDetailAsync(
                    new RestoreSalesDetailExecution(
                        CommandId.From(commandIds[4]),
                        Hash(5),
                        ActorAccountId.From(scenario.ActorAccountId),
                        new RestoreSalesDetailCommand(scenario.FirstDetailId, 2)),
                    token),
                ct);
            Assert.True(restoreDetail.IsSuccess);
            Assert.False(restoreDetail.Value.Deleted);
            Assert.Equal(3L, restoreDetail.Value.RowVersion);

            var staleHardDelete = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IHardDeleteSalesTransactionExecutor>().HardDeleteDetailAsync(
                    new HardDeleteSalesDetailExecution(
                        CommandId.From(commandIds[5]),
                        Hash(6),
                        ActorAccountId.From(scenario.ActorAccountId),
                        new HardDeleteSalesDetailCommand(scenario.FirstDetailId, 2)),
                    token),
                ct);
            Assert.True(staleHardDelete.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, staleHardDelete.Error.Kind);
            Assert.Equal(SalesTransactionLifecycleErrorCodes.StaleRowVersion, staleHardDelete.Error.Code);
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM system.command_executions WHERE command_id = @id;",
                ct,
                ("id", commandIds[5])));

            var hardDetailExecution = new HardDeleteSalesDetailExecution(
                CommandId.From(commandIds[6]),
                Hash(7),
                ActorAccountId.From(scenario.ActorAccountId),
                new HardDeleteSalesDetailCommand(scenario.FirstDetailId, 3));
            var hardDetail = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IHardDeleteSalesTransactionExecutor>().HardDeleteDetailAsync(hardDetailExecution, token),
                ct);
            Assert.True(hardDetail.IsSuccess);
            Assert.Equal(scenario.FirstDetailId, hardDetail.Value.Id);

            var hardDetailReplay = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IHardDeleteSalesTransactionExecutor>().HardDeleteDetailAsync(hardDetailExecution, token),
                ct);
            Assert.True(hardDetailReplay.IsSuccess);
            Assert.Equal(hardDetail.Value, hardDetailReplay.Value);

            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM sales.sales_details WHERE id = @id;",
                ct,
                ("id", scenario.FirstDetailId)));
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM sales.sales_details WHERE id = @id;",
                ct,
                ("id", scenario.SecondDetailId)));
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM sales.sales_allocations WHERE sales_detail_id = @id;",
                ct,
                ("id", scenario.FirstDetailId)));
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM sales.sales_allocations WHERE sales_detail_id = @id;",
                ct,
                ("id", scenario.SecondDetailId)));
            Assert.Equal(8m, await PositionBalanceAsync(scenario.InventoryPositionId, ct));
            Assert.Equal(100L, await ScalarAsync<long>(
                "SELECT original_obligation_thb FROM finance.receivable_outstanding_positions WHERE receivable_id = @id;",
                ct,
                ("id", confirmed.ReceivableId)));
            Assert.Equal(100L, await ScalarAsync<long>(
                "SELECT outstanding_thb FROM finance.receivable_outstanding_positions WHERE receivable_id = @id;",
                ct,
                ("id", confirmed.ReceivableId)));
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM finance.receivable_obligation_items WHERE sales_detail_id = @id;",
                ct,
                ("id", scenario.FirstDetailId)));
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM finance.receivable_obligation_items WHERE sales_detail_id = @id;",
                ct,
                ("id", scenario.SecondDetailId)));

            var hardSaleExecution = new HardDeleteSaleExecution(
                CommandId.From(commandIds[7]),
                Hash(8),
                ActorAccountId.From(scenario.ActorAccountId),
                new HardDeleteSaleCommand(scenario.SalesId, 4));
            var hardSale = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IHardDeleteSalesTransactionExecutor>().HardDeleteSaleAsync(hardSaleExecution, token),
                ct);
            Assert.True(hardSale.IsSuccess);
            Assert.Equal(scenario.SalesId, hardSale.Value.Id);

            var hardSaleReplay = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IHardDeleteSalesTransactionExecutor>().HardDeleteSaleAsync(hardSaleExecution, token),
                ct);
            Assert.True(hardSaleReplay.IsSuccess);
            Assert.Equal(hardSale.Value, hardSaleReplay.Value);

            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM sales.sales WHERE id = @id;",
                ct,
                ("id", scenario.SalesId)));
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM sales.sales_details WHERE sales_id = @id;",
                ct,
                ("id", scenario.SalesId)));
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM finance.receivables WHERE sales_id = @id;",
                ct,
                ("id", scenario.SalesId)));
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM inventory.inventory_operations WHERE sales_id = @id;",
                ct,
                ("id", scenario.SalesId)));
            Assert.Equal(10m, await PositionBalanceAsync(scenario.InventoryPositionId, ct));
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM audit.audit_events WHERE command_id = @id AND event_kind = 'HARD_DELETE';",
                ct,
                ("id", commandIds[6])));
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM audit.audit_events WHERE command_id = @id AND event_kind = 'HARD_DELETE';",
                ct,
                ("id", commandIds[7])));
        }
        finally
        {
            await CleanupAsync(scenario, commandIds, ct);
        }
    }

    private static async Task<Scenario> SeedScenarioAsync(CancellationToken ct)
    {
        var scenario = new Scenario(
            ActorAccountId: Guid.CreateVersion7(),
            WarehouseId: Guid.CreateVersion7(),
            StorageLocationId: Guid.CreateVersion7(),
            SalesProductGroupId: Guid.CreateVersion7(),
            SalesProductId: Guid.CreateVersion7(),
            VendorId: Guid.CreateVersion7(),
            CustomerId: Guid.CreateVersion7(),
            OutsourcedSupplyBatchId: Guid.CreateVersion7(),
            InventoryPositionId: Guid.CreateVersion7(),
            SalesId: Guid.CreateVersion7(),
            FirstDetailId: Guid.CreateVersion7(),
            SecondDetailId: Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES (@actor, 'Sales lifecycle actor', true, NULL, NULL, @now);

            INSERT INTO infrastructure.warehouses
                (id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES (@warehouse, NULL, '銷售刪除驗收倉庫', NULL, true, @now, @actor);

            INSERT INTO infrastructure.storage_locations
                (id, warehouse_id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES (@location, @warehouse, NULL, '銷售刪除驗收儲位', NULL, true, @now, @actor);

            INSERT INTO product.sales_product_groups
                (id, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES (@group, '銷售刪除驗收群組', NULL, true, @now, @actor);

            INSERT INTO product.sales_products
                (id, sales_product_group_id, name_zh_tw, name_th_th, pricing_basis,
                 packaging_weight, sales_weight, default_storage_location_id,
                 active, created_at, created_by_account_id)
            VALUES (@product, @group, '銷售刪除驗收產品', NULL, 'UNIT_BASED',
                    NULL, NULL, @location, true, @now, @actor);

            INSERT INTO party.outsourced_vendors
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, created_at, created_by_account_id)
            VALUES (@vendor, '銷售刪除驗收加工商', NULL, NULL, NULL, NULL, NULL,
                    true, @now, @actor);

            INSERT INTO party.customers
                (id, name_zh_tw, name_th_th, phone, active, created_at, created_by_account_id)
            VALUES (@customer, '銷售刪除驗收客戶', NULL, NULL, true, @now, @actor);

            INSERT INTO outsourced.outsourced_supply_batches
                (id, supply_date, outsourced_vendor_id, lifecycle_status,
                 closed_at, closed_by_account_id, created_at, created_by_account_id,
                 deleted_at, deleted_by_account_id)
            VALUES (@batch, DATE '2026-09-09', @vendor, 'ACTIVE',
                    NULL, NULL, @now, @actor, NULL, NULL);

            INSERT INTO inventory.inventory_positions
                (id, origin, procurement_batch_id, outsourced_supply_batch_id,
                 inventory_object_kind, procurement_product_id, process_material_id,
                 sales_product_id, storage_location_id, raw_source_kind, supplier_id,
                 balance_quantity, row_version)
            VALUES (@position, 'OUTSOURCED', NULL, @batch,
                    'SALES_PRODUCT', NULL, NULL, @product, @location, NULL, NULL,
                    10, 1);

            INSERT INTO sales.sales
                (id, sales_date, customer_id, status, confirmed_at, confirmed_by_account_id,
                 row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES (@sale, DATE '2026-09-09', @customer, 'DRAFT', NULL, NULL,
                    1, @now, @actor, NULL, NULL);

            INSERT INTO sales.sales_details
                (id, sales_id, line_number, sales_product_id, quantity,
                 pricing_basis_snapshot, sales_weight_snapshot, unit_price, amount_thb,
                 row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@first_detail, @sale, 1, @product, 3,
                 'UNIT_BASED', NULL, 50, 150,
                 1, @now, @actor, NULL, NULL),
                (@second_detail, @sale, 2, @product, 2,
                 'UNIT_BASED', NULL, 50, 100,
                 1, @now, @actor, NULL, NULL);
            """,
            ct,
            ("actor", scenario.ActorAccountId),
            ("warehouse", scenario.WarehouseId),
            ("location", scenario.StorageLocationId),
            ("group", scenario.SalesProductGroupId),
            ("product", scenario.SalesProductId),
            ("vendor", scenario.VendorId),
            ("customer", scenario.CustomerId),
            ("batch", scenario.OutsourcedSupplyBatchId),
            ("position", scenario.InventoryPositionId),
            ("sale", scenario.SalesId),
            ("first_detail", scenario.FirstDetailId),
            ("second_detail", scenario.SecondDetailId),
            ("now", now));

        return scenario;
    }

    private static async Task CleanupAsync(
        Scenario scenario,
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

            DELETE FROM finance.receipts
            WHERE receivable_id IN (SELECT id FROM finance.receivables WHERE sales_id = @sale);
            DELETE FROM finance.receivable_outstanding_positions
            WHERE receivable_id IN (SELECT id FROM finance.receivables WHERE sales_id = @sale);
            DELETE FROM finance.receivable_obligation_items WHERE sales_id = @sale;
            DELETE FROM finance.receivables WHERE sales_id = @sale;

            DELETE FROM inventory.inventory_movements
            WHERE inventory_operation_id IN
                (SELECT id FROM inventory.inventory_operations
                 WHERE sales_id = @sale
                    OR sales_allocation_revision_id IN
                       (SELECT id FROM sales.sales_allocation_revisions WHERE sales_id = @sale));
            DELETE FROM inventory.inventory_operations
            WHERE sales_id = @sale
               OR sales_allocation_revision_id IN
                  (SELECT id FROM sales.sales_allocation_revisions WHERE sales_id = @sale);

            DELETE FROM sales.sales_allocations
            WHERE sales_detail_id IN (SELECT id FROM sales.sales_details WHERE sales_id = @sale);
            DELETE FROM sales.sales_allocation_revision_items WHERE sales_id = @sale;
            DELETE FROM sales.sales_allocation_revisions WHERE sales_id = @sale;
            DELETE FROM sales.sales_details WHERE sales_id = @sale;
            DELETE FROM sales.sales WHERE id = @sale;

            DELETE FROM inventory.inventory_positions WHERE id = @position;
            DELETE FROM outsourced.outsourced_supply_batches WHERE id = @batch;
            DELETE FROM product.sales_products WHERE id = @product;
            DELETE FROM product.sales_product_groups WHERE id = @group;
            DELETE FROM party.customers WHERE id = @customer;
            DELETE FROM party.outsourced_vendors WHERE id = @vendor;
            DELETE FROM infrastructure.storage_locations WHERE id = @location;
            DELETE FROM infrastructure.warehouses WHERE id = @warehouse;
            DELETE FROM system.accounts WHERE id = @actor;
            """,
            ct,
            ("commands", commandIds.ToArray()),
            ("sale", scenario.SalesId),
            ("position", scenario.InventoryPositionId),
            ("batch", scenario.OutsourcedSupplyBatchId),
            ("product", scenario.SalesProductId),
            ("group", scenario.SalesProductGroupId),
            ("customer", scenario.CustomerId),
            ("vendor", scenario.VendorId),
            ("location", scenario.StorageLocationId),
            ("warehouse", scenario.WarehouseId),
            ("actor", scenario.ActorAccountId));
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

    private static async Task<decimal> PositionBalanceAsync(Guid positionId, CancellationToken ct) =>
        await ScalarAsync<decimal>(
            "SELECT balance_quantity FROM inventory.inventory_positions WHERE id = @id;",
            ct,
            ("id", positionId));

    private static async Task<T> ScalarAsync<T>(
        string sql,
        CancellationToken ct,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        var value = await command.ExecuteScalarAsync(ct);
        if (value is null || value is DBNull) return default!;
        return (T)value;
    }

    private static async Task ExecuteNonQueryAsync(
        string sql,
        CancellationToken ct,
        params (string Name, object Value)[] parameters)
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
        Guid StorageLocationId,
        Guid SalesProductGroupId,
        Guid SalesProductId,
        Guid VendorId,
        Guid CustomerId,
        Guid OutsourcedSupplyBatchId,
        Guid InventoryPositionId,
        Guid SalesId,
        Guid FirstDetailId,
        Guid SecondDetailId);
}
