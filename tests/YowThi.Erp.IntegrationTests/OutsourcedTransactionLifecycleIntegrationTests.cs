using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.DataProtection;
using YowThi.Erp.Application.Outsourced;
using YowThi.Erp.Application.Sales;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class OutsourcedTransactionLifecycleIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Outsourced_detail_and_batch_lifecycle_preserve_siblings_and_mixed_sales_sources()
    {
        var ct = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(ct);
        var commandIds = Enumerable.Range(0, 11).Select(_ => Guid.CreateVersion7()).ToArray();

        ConfirmOutsourcedSupplyDetailResult batch1Detail = default!;
        ConfirmOutsourcedSupplyDetailResult batch2Detail = default!;
        ConfirmOutsourcedSupplyDetailResult batch3DetailA = default!;
        ConfirmOutsourcedSupplyDetailResult batch3DetailB = default!;
        ConfirmSalesResult confirmedSale = default!;

        try
        {
            batch1Detail = await ConfirmOutsourcedAsync(
                commandIds[0], 1, scenario.ActorAccountId,
                scenario.FirstSupplyDate, scenario.FirstVendorId, scenario.SalesProductId, 5m, 10m, ct);
            batch2Detail = await ConfirmOutsourcedAsync(
                commandIds[1], 2, scenario.ActorAccountId,
                scenario.SecondSupplyDate, scenario.SecondVendorId, scenario.SalesProductId, 5m, 11m, ct);
            batch3DetailA = await ConfirmOutsourcedAsync(
                commandIds[2], 3, scenario.ActorAccountId,
                scenario.ThirdSupplyDate, scenario.SecondVendorId, scenario.SalesProductId, 4m, 12m, ct);
            batch3DetailB = await ConfirmOutsourcedAsync(
                commandIds[3], 4, scenario.ActorAccountId,
                scenario.ThirdSupplyDate, scenario.SecondVendorId, scenario.SalesProductId, 6m, 13m, ct);

            Assert.NotEqual(batch1Detail.OutsourcedSupplyBatchId, batch2Detail.OutsourcedSupplyBatchId);
            Assert.Equal(batch3DetailA.OutsourcedSupplyBatchId, batch3DetailB.OutsourcedSupplyBatchId);
            Assert.Equal(10m, await OutsourcedBatchBalanceAsync(batch3DetailA.OutsourcedSupplyBatchId, ct));

            await AssertDataProtectionOptionsAsync(
                new[]
                {
                    batch1Detail.OutsourcedSupplyBatchId,
                    batch2Detail.OutsourcedSupplyBatchId,
                    batch3DetailA.OutsourcedSupplyBatchId,
                },
                new[]
                {
                    batch1Detail.OutsourcedSupplyDetailId,
                    batch2Detail.OutsourcedSupplyDetailId,
                    batch3DetailA.OutsourcedSupplyDetailId,
                    batch3DetailB.OutsourcedSupplyDetailId,
                },
                ct);

            var initialBatch1RowVersion = await ScalarAsync<long>(
                "SELECT row_version FROM outsourced.outsourced_supply_batches WHERE id=@id;",
                ct,
                ("id", batch1Detail.OutsourcedSupplyBatchId));
            var softBatch = await ExecuteServicesAsync(
                (services, token) => services.GetRequiredService<IOutsourcedTransactionLifecycleExecutor>().SoftDeleteBatchAsync(
                    new SoftDeleteOutsourcedSupplyBatchExecution(
                        CommandId.From(commandIds[4]),
                        Hash(5),
                        ActorAccountId.From(scenario.ActorAccountId),
                        new SoftDeleteOutsourcedSupplyBatchCommand(batch1Detail.OutsourcedSupplyBatchId, initialBatch1RowVersion)),
                    token),
                ct);
            Assert.True(softBatch.IsSuccess);
            Assert.True(softBatch.Value.Deleted);
            Assert.Equal(initialBatch1RowVersion + 1, softBatch.Value.RowVersion);

            var restoreBatch = await ExecuteServicesAsync(
                (services, token) => services.GetRequiredService<IOutsourcedTransactionLifecycleExecutor>().RestoreBatchAsync(
                    new RestoreOutsourcedSupplyBatchExecution(
                        CommandId.From(commandIds[5]),
                        Hash(6),
                        ActorAccountId.From(scenario.ActorAccountId),
                        new RestoreOutsourcedSupplyBatchCommand(batch1Detail.OutsourcedSupplyBatchId, softBatch.Value.RowVersion)),
                    token),
                ct);
            Assert.True(restoreBatch.IsSuccess);
            Assert.False(restoreBatch.Value.Deleted);
            Assert.Equal(initialBatch1RowVersion + 2, restoreBatch.Value.RowVersion);

            var softDetail = await ExecuteServicesAsync(
                (services, token) => services.GetRequiredService<IOutsourcedTransactionLifecycleExecutor>().SoftDeleteDetailAsync(
                    new SoftDeleteOutsourcedSupplyDetailExecution(
                        CommandId.From(commandIds[6]),
                        Hash(7),
                        ActorAccountId.From(scenario.ActorAccountId),
                        new SoftDeleteOutsourcedSupplyDetailCommand(
                            batch3DetailA.OutsourcedSupplyDetailId,
                            batch3DetailA.OutsourcedSupplyDetailRowVersion)),
                    token),
                ct);
            Assert.True(softDetail.IsSuccess);
            Assert.True(softDetail.Value.Deleted);

            var restoreDetail = await ExecuteServicesAsync(
                (services, token) => services.GetRequiredService<IOutsourcedTransactionLifecycleExecutor>().RestoreDetailAsync(
                    new RestoreOutsourcedSupplyDetailExecution(
                        CommandId.From(commandIds[7]),
                        Hash(8),
                        ActorAccountId.From(scenario.ActorAccountId),
                        new RestoreOutsourcedSupplyDetailCommand(batch3DetailA.OutsourcedSupplyDetailId, softDetail.Value.RowVersion)),
                    token),
                ct);
            Assert.True(restoreDetail.IsSuccess);
            Assert.False(restoreDetail.Value.Deleted);

            var hardDetailExecution = new HardDeleteOutsourcedSupplyDetailExecution(
                CommandId.From(commandIds[8]),
                Hash(9),
                ActorAccountId.From(scenario.ActorAccountId),
                new HardDeleteOutsourcedSupplyDetailCommand(batch3DetailA.OutsourcedSupplyDetailId, restoreDetail.Value.RowVersion));
            var hardDetail = await ExecuteServicesAsync(
                (services, token) => services.GetRequiredService<IHardDeleteOutsourcedTransactionExecutor>().HardDeleteDetailAsync(hardDetailExecution, token),
                ct);
            Assert.True(hardDetail.IsSuccess);
            Assert.Equal(batch3DetailA.OutsourcedSupplyDetailId, hardDetail.Value.Id);

            var hardDetailReplay = await ExecuteServicesAsync(
                (services, token) => services.GetRequiredService<IHardDeleteOutsourcedTransactionExecutor>().HardDeleteDetailAsync(hardDetailExecution, token),
                ct);
            Assert.True(hardDetailReplay.IsSuccess);
            Assert.Equal(hardDetail.Value, hardDetailReplay.Value);

            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM outsourced.outsourced_supply_details WHERE id=@id;",
                ct,
                ("id", batch3DetailA.OutsourcedSupplyDetailId)));
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM outsourced.outsourced_supply_details WHERE id=@id;",
                ct,
                ("id", batch3DetailB.OutsourcedSupplyDetailId)));
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM finance.payables WHERE id=@id;",
                ct,
                ("id", batch3DetailA.PayableId)));
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM finance.payables WHERE id=@id;",
                ct,
                ("id", batch3DetailB.PayableId)));
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM system.outbox_messages WHERE command_id=@id AND published_at IS NULL;",
                ct,
                ("id", commandIds[3])));
            Assert.Equal(6m, await OutsourcedBatchBalanceAsync(batch3DetailA.OutsourcedSupplyBatchId, ct));

            var confirmSale = await ExecuteServicesAsync(
                (services, token) => services.GetRequiredService<IConfirmSalesExecutor>().ExecuteAsync(
                    new ConfirmSalesExecution(
                        CommandId.From(commandIds[9]),
                        Hash(10),
                        ActorAccountId.From(scenario.ActorAccountId),
                        new ConfirmSalesCommand(scenario.SalesId, 1, Array.Empty<SalesManualAllocationOverride>())),
                    token),
                ct);
            Assert.True(confirmSale.IsSuccess);
            confirmedSale = confirmSale.Value;

            Assert.Equal(2L, await ScalarAsync<long>(
                "SELECT count(*) FROM inventory.inventory_movements WHERE inventory_operation_id=@id AND movement_type='SALES_ISSUE';",
                ct,
                ("id", confirmedSale.InventoryOperationId)));
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM inventory.inventory_movements WHERE inventory_operation_id=@op AND outsourced_supply_batch_id=@batch;",
                ct,
                ("op", confirmedSale.InventoryOperationId),
                ("batch", batch1Detail.OutsourcedSupplyBatchId)));
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM inventory.inventory_movements WHERE inventory_operation_id=@op AND outsourced_supply_batch_id=@batch;",
                ct,
                ("op", confirmedSale.InventoryOperationId),
                ("batch", batch2Detail.OutsourcedSupplyBatchId)));
            Assert.Equal(2m, await OutsourcedBatchBalanceAsync(batch2Detail.OutsourcedSupplyBatchId, ct));

            var hardBatchExecution = new HardDeleteOutsourcedSupplyBatchExecution(
                CommandId.From(commandIds[10]),
                Hash(11),
                ActorAccountId.From(scenario.ActorAccountId),
                new HardDeleteOutsourcedSupplyBatchCommand(batch1Detail.OutsourcedSupplyBatchId, restoreBatch.Value.RowVersion));
            var hardBatch = await ExecuteServicesAsync(
                (services, token) => services.GetRequiredService<IHardDeleteOutsourcedTransactionExecutor>().HardDeleteBatchAsync(hardBatchExecution, token),
                ct);
            Assert.True(hardBatch.IsSuccess);
            Assert.Equal(batch1Detail.OutsourcedSupplyBatchId, hardBatch.Value.Id);

            var hardBatchReplay = await ExecuteServicesAsync(
                (services, token) => services.GetRequiredService<IHardDeleteOutsourcedTransactionExecutor>().HardDeleteBatchAsync(hardBatchExecution, token),
                ct);
            Assert.True(hardBatchReplay.IsSuccess);
            Assert.Equal(hardBatch.Value, hardBatchReplay.Value);

            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM outsourced.outsourced_supply_batches WHERE id=@id;",
                ct,
                ("id", batch1Detail.OutsourcedSupplyBatchId)));
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM outsourced.outsourced_supply_details WHERE id=@id;",
                ct,
                ("id", batch1Detail.OutsourcedSupplyDetailId)));
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM finance.payables WHERE id=@id;",
                ct,
                ("id", batch1Detail.PayableId)));
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM outsourced.outsourced_supply_batches WHERE id=@id;",
                ct,
                ("id", batch2Detail.OutsourcedSupplyBatchId)));
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM finance.payables WHERE id=@id;",
                ct,
                ("id", batch2Detail.PayableId)));

            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM sales.sales WHERE id=@id;",
                ct,
                ("id", scenario.SalesId)));
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM sales.sales_details WHERE id=@id;",
                ct,
                ("id", scenario.SalesDetailId)));
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM finance.receivables WHERE id=@id;",
                ct,
                ("id", confirmedSale.ReceivableId)));
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM inventory.inventory_operations WHERE id=@id;",
                ct,
                ("id", confirmedSale.InventoryOperationId)));
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM inventory.inventory_movements WHERE inventory_operation_id=@op AND outsourced_supply_batch_id=@batch;",
                ct,
                ("op", confirmedSale.InventoryOperationId),
                ("batch", batch1Detail.OutsourcedSupplyBatchId)));
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM inventory.inventory_movements WHERE inventory_operation_id=@op AND outsourced_supply_batch_id=@batch;",
                ct,
                ("op", confirmedSale.InventoryOperationId),
                ("batch", batch2Detail.OutsourcedSupplyBatchId)));
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM sales.sales_allocation_revision_items WHERE sales_allocation_revision_id=@revision AND outsourced_supply_batch_id=@batch;",
                ct,
                ("revision", confirmedSale.AllocationRevisionId),
                ("batch", batch1Detail.OutsourcedSupplyBatchId)));
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM sales.sales_allocation_revision_items WHERE sales_allocation_revision_id=@revision AND outsourced_supply_batch_id=@batch;",
                ct,
                ("revision", confirmedSale.AllocationRevisionId),
                ("batch", batch2Detail.OutsourcedSupplyBatchId)));
            Assert.Equal(2m, await OutsourcedBatchBalanceAsync(batch2Detail.OutsourcedSupplyBatchId, ct));

            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM audit.audit_events WHERE command_id=@id AND event_kind='HARD_DELETE';",
                ct,
                ("id", commandIds[8])));
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM audit.audit_events WHERE command_id=@id AND event_kind='HARD_DELETE';",
                ct,
                ("id", commandIds[10])));
        }
        finally
        {
            await CleanupAsync(scenario, commandIds, ct);
        }
    }

    private static async Task<ConfirmOutsourcedSupplyDetailResult> ConfirmOutsourcedAsync(
        Guid commandId,
        byte hashByte,
        Guid actorAccountId,
        DateOnly supplyDate,
        Guid vendorId,
        Guid productId,
        decimal quantity,
        decimal unitPrice,
        CancellationToken ct)
    {
        var result = await ExecuteServicesAsync(
            (services, token) => services.GetRequiredService<IConfirmOutsourcedSupplyDetailExecutor>().ExecuteAsync(
                new ConfirmOutsourcedSupplyDetailExecution(
                    CommandId.From(commandId),
                    Hash(hashByte),
                    ActorAccountId.From(actorAccountId),
                    new ConfirmOutsourcedSupplyDetailCommand(
                        supplyDate,
                        vendorId,
                        productId,
                        quantity,
                        unitPrice,
                        null)),
                token),
            ct);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static async Task AssertDataProtectionOptionsAsync(
        IReadOnlyCollection<Guid> expectedBatchIds,
        IReadOnlyCollection<Guid> expectedDetailIds,
        CancellationToken ct)
    {
        var result = await ExecuteServicesAsync(async (services, token) =>
        {
            var reader = services.GetRequiredService<IHardDeleteOptionsReader>();
            var batches = await reader.GetOutsourcedSupplyBatchesAsync(
                new HardDeleteOptionsQuery("zh-TW", null, 0, 100), token);
            var details = await reader.GetOutsourcedSupplyDetailsAsync(
                new HardDeleteOptionsQuery("zh-TW", null, 0, 100), token);
            return (Batches: batches, Details: details);
        }, ct);

        foreach (var batchId in expectedBatchIds)
            Assert.Contains(result.Batches.Items, item => item.Id == batchId);
        foreach (var detailId in expectedDetailIds)
            Assert.Contains(result.Details.Items, item => item.Id == detailId);
    }

    private static async Task<Scenario> SeedScenarioAsync(CancellationToken ct)
    {
        var scenario = new Scenario(
            ActorAccountId: Guid.CreateVersion7(),
            WarehouseId: Guid.CreateVersion7(),
            StorageLocationId: Guid.CreateVersion7(),
            SalesProductGroupId: Guid.CreateVersion7(),
            SalesProductId: Guid.CreateVersion7(),
            FirstVendorId: Guid.CreateVersion7(),
            SecondVendorId: Guid.CreateVersion7(),
            CustomerId: Guid.CreateVersion7(),
            SalesId: Guid.CreateVersion7(),
            SalesDetailId: Guid.CreateVersion7(),
            FirstSupplyDate: new DateOnly(2026, 9, 1),
            SecondSupplyDate: new DateOnly(2026, 9, 2),
            ThirdSupplyDate: new DateOnly(2026, 9, 3));
        var now = DateTimeOffset.UtcNow;

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES (@actor, 'Outsourced lifecycle actor', true, NULL, NULL, @now);

            INSERT INTO infrastructure.warehouses
                (id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES (@warehouse, NULL, '委外刪除驗收倉庫', NULL, true, @now, @actor);

            INSERT INTO infrastructure.storage_locations
                (id, warehouse_id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES (@location, @warehouse, NULL, '委外刪除驗收儲位', NULL, true, @now, @actor);

            INSERT INTO product.sales_product_groups
                (id, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES (@group, '委外刪除驗收群組', NULL, true, @now, @actor);

            INSERT INTO product.sales_products
                (id, sales_product_group_id, name_zh_tw, name_th_th, pricing_basis,
                 packaging_weight, sales_weight, default_storage_location_id,
                 active, created_at, created_by_account_id)
            VALUES (@product, @group, '委外刪除驗收產品', NULL, 'UNIT_BASED',
                    NULL, NULL, @location, true, @now, @actor);

            INSERT INTO party.outsourced_vendors
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, created_at, created_by_account_id)
            VALUES
                (@vendor1, '委外刪除驗收加工商一', NULL, NULL, NULL, NULL, NULL, true, @now, @actor),
                (@vendor2, '委外刪除驗收加工商二', NULL, NULL, NULL, NULL, NULL, true, @now, @actor);

            INSERT INTO party.customers
                (id, name_zh_tw, name_th_th, phone, active, created_at, created_by_account_id)
            VALUES (@customer, '委外刪除驗收客戶', NULL, NULL, true, @now, @actor);

            INSERT INTO sales.sales
                (id, sales_date, customer_id, status, confirmed_at, confirmed_by_account_id,
                 row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES (@sale, DATE '2026-09-04', @customer, 'DRAFT', NULL, NULL,
                    1, @now, @actor, NULL, NULL);

            INSERT INTO sales.sales_details
                (id, sales_id, line_number, sales_product_id, quantity,
                 pricing_basis_snapshot, sales_weight_snapshot, unit_price, amount_thb,
                 row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES (@sales_detail, @sale, 1, @product, 8,
                    'UNIT_BASED', NULL, 20, 160,
                    1, @now, @actor, NULL, NULL);
            """,
            ct,
            ("actor", scenario.ActorAccountId),
            ("warehouse", scenario.WarehouseId),
            ("location", scenario.StorageLocationId),
            ("group", scenario.SalesProductGroupId),
            ("product", scenario.SalesProductId),
            ("vendor1", scenario.FirstVendorId),
            ("vendor2", scenario.SecondVendorId),
            ("customer", scenario.CustomerId),
            ("sale", scenario.SalesId),
            ("sales_detail", scenario.SalesDetailId),
            ("now", now));

        return scenario;
    }

    private static async Task CleanupAsync(Scenario scenario, IReadOnlyCollection<Guid> commandIds, CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            """
            DELETE FROM audit.audit_event_subjects
            WHERE audit_event_id IN (SELECT id FROM audit.audit_events WHERE command_id = ANY(@commands));
            DELETE FROM audit.audit_events WHERE command_id = ANY(@commands);
            DELETE FROM system.outbox_messages WHERE command_id = ANY(@commands);
            DELETE FROM system.command_executions WHERE command_id = ANY(@commands);

            DELETE FROM finance.receipts
            WHERE receivable_id IN (SELECT id FROM finance.receivables WHERE sales_id=@sale);
            DELETE FROM finance.receivable_outstanding_positions
            WHERE receivable_id IN (SELECT id FROM finance.receivables WHERE sales_id=@sale);
            DELETE FROM finance.receivable_obligation_items WHERE sales_id=@sale;
            DELETE FROM finance.receivables WHERE sales_id=@sale;

            DELETE FROM inventory.inventory_movements
            WHERE inventory_operation_id IN (
                SELECT id FROM inventory.inventory_operations
                WHERE sales_id=@sale
                   OR sales_allocation_revision_id IN (
                       SELECT id FROM sales.sales_allocation_revisions WHERE sales_id=@sale));
            DELETE FROM inventory.inventory_operations
            WHERE sales_id=@sale
               OR sales_allocation_revision_id IN (
                   SELECT id FROM sales.sales_allocation_revisions WHERE sales_id=@sale);
            DELETE FROM sales.sales_allocations
            WHERE sales_detail_id IN (SELECT id FROM sales.sales_details WHERE sales_id=@sale);
            DELETE FROM sales.sales_allocation_revision_items WHERE sales_id=@sale;
            DELETE FROM sales.sales_allocation_revisions WHERE sales_id=@sale;
            DELETE FROM sales.sales_details WHERE sales_id=@sale;
            DELETE FROM sales.sales WHERE id=@sale;

            DELETE FROM finance.payments
            WHERE payable_id IN (
                SELECT p.id FROM finance.payables p
                JOIN outsourced.outsourced_supply_details d ON d.id=p.outsourced_supply_detail_id
                JOIN outsourced.outsourced_supply_batches b ON b.id=d.outsourced_supply_batch_id
                WHERE b.outsourced_vendor_id = ANY(@vendors));
            DELETE FROM finance.payable_adjustments
            WHERE payable_id IN (
                SELECT p.id FROM finance.payables p
                JOIN outsourced.outsourced_supply_details d ON d.id=p.outsourced_supply_detail_id
                JOIN outsourced.outsourced_supply_batches b ON b.id=d.outsourced_supply_batch_id
                WHERE b.outsourced_vendor_id = ANY(@vendors));
            DELETE FROM finance.payable_outstanding_positions
            WHERE payable_id IN (
                SELECT p.id FROM finance.payables p
                JOIN outsourced.outsourced_supply_details d ON d.id=p.outsourced_supply_detail_id
                JOIN outsourced.outsourced_supply_batches b ON b.id=d.outsourced_supply_batch_id
                WHERE b.outsourced_vendor_id = ANY(@vendors));
            DELETE FROM finance.payable_obligation_items
            WHERE outsourced_supply_detail_id IN (
                SELECT d.id FROM outsourced.outsourced_supply_details d
                JOIN outsourced.outsourced_supply_batches b ON b.id=d.outsourced_supply_batch_id
                WHERE b.outsourced_vendor_id = ANY(@vendors));
            DELETE FROM finance.payables
            WHERE outsourced_supply_detail_id IN (
                SELECT d.id FROM outsourced.outsourced_supply_details d
                JOIN outsourced.outsourced_supply_batches b ON b.id=d.outsourced_supply_batch_id
                WHERE b.outsourced_vendor_id = ANY(@vendors));

            DELETE FROM inventory.inventory_movements
            WHERE outsourced_supply_batch_id IN (
                SELECT id FROM outsourced.outsourced_supply_batches WHERE outsourced_vendor_id = ANY(@vendors));
            DELETE FROM inventory.inventory_operations
            WHERE outsourced_supply_detail_id IN (
                SELECT d.id FROM outsourced.outsourced_supply_details d
                JOIN outsourced.outsourced_supply_batches b ON b.id=d.outsourced_supply_batch_id
                WHERE b.outsourced_vendor_id = ANY(@vendors));
            DELETE FROM inventory.inventory_positions
            WHERE outsourced_supply_batch_id IN (
                SELECT id FROM outsourced.outsourced_supply_batches WHERE outsourced_vendor_id = ANY(@vendors));

            DELETE FROM outsourced.outsourced_supply_details
            WHERE outsourced_supply_batch_id IN (
                SELECT id FROM outsourced.outsourced_supply_batches WHERE outsourced_vendor_id = ANY(@vendors));
            DELETE FROM outsourced.outsourced_supply_batches WHERE outsourced_vendor_id = ANY(@vendors);

            DELETE FROM party.customers WHERE id=@customer;
            DELETE FROM party.outsourced_vendors WHERE id = ANY(@vendors);
            DELETE FROM product.sales_products WHERE id=@product;
            DELETE FROM product.sales_product_groups WHERE id=@group;
            DELETE FROM infrastructure.storage_locations WHERE id=@location;
            DELETE FROM infrastructure.warehouses WHERE id=@warehouse;
            DELETE FROM system.accounts WHERE id=@actor;
            """,
            ct,
            ("commands", commandIds.ToArray()),
            ("sale", scenario.SalesId),
            ("vendors", new[] { scenario.FirstVendorId, scenario.SecondVendorId }),
            ("customer", scenario.CustomerId),
            ("product", scenario.SalesProductId),
            ("group", scenario.SalesProductGroupId),
            ("location", scenario.StorageLocationId),
            ("warehouse", scenario.WarehouseId),
            ("actor", scenario.ActorAccountId));
    }

    private static async ValueTask<TResult> ExecuteServicesAsync<TResult>(
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

    private static Task<decimal> OutsourcedBatchBalanceAsync(Guid batchId, CancellationToken ct) =>
        ScalarAsync<decimal>(
            "SELECT balance_quantity FROM inventory.inventory_positions WHERE outsourced_supply_batch_id=@id;",
            ct,
            ("id", batchId));

    private static async Task<T> ScalarAsync<T>(
        string sql,
        CancellationToken ct,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        var value = await command.ExecuteScalarAsync(ct);
        return (T)(value ?? throw new InvalidOperationException("Expected scalar query to return a value."));
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
        Guid FirstVendorId,
        Guid SecondVendorId,
        Guid CustomerId,
        Guid SalesId,
        Guid SalesDetailId,
        DateOnly FirstSupplyDate,
        DateOnly SecondSupplyDate,
        DateOnly ThirdSupplyDate);
}
