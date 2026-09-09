using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Infrastructure;
using YowThi.Erp.Application.Inventory;
using YowThi.Erp.Application.Product;
using YowThi.Erp.Domain.Inventory;
using YowThi.Erp.Domain.Product;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class ProductInventoryCompletionIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString = "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Product_and_inventory_master_completion_persists_replays_projects_and_reads_negative_inventory()
    {
        var ct = TestContext.Current.CancellationToken;
        var actorId = Guid.CreateVersion7();
        var salesProductGroupId = Guid.CreateVersion7();
        var supplierId = Guid.CreateVersion7();
        var procurementBatchId = Guid.CreateVersion7();
        var inventoryPositionId = Guid.CreateVersion7();
        var commandIds = Enumerable.Range(1, 9).Select(_ => Guid.CreateVersion7()).ToArray();
        Guid warehouseId = Guid.Empty;
        Guid storageLocationId = Guid.Empty;
        Guid procurementProductId = Guid.Empty;
        Guid salesProductId = Guid.Empty;

        await ExecuteNonQueryAsync(
            "INSERT INTO system.accounts (id, display_name, active, identity_issuer, identity_subject, created_at) VALUES (@id, 'P8 product inventory actor', true, NULL, NULL, now());",
            ct, ("id", actorId));

        try
        {
            var warehouseCreate = new CreateWarehouseExecution(
                CommandId.From(commandIds[0]), Hash(1), ActorAccountId.From(actorId),
                new CreateWarehouseCommand("WH-P8", "測試倉庫", "คลังทดสอบ", true));
            var warehouseCreated = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IWarehouseMasterExecutor>().CreateAsync(warehouseCreate, token), ct);
            Assert.True(warehouseCreated.IsSuccess);
            warehouseId = warehouseCreated.Value.WarehouseId;
            Assert.Equal(1, warehouseCreated.Value.RowVersion);

            var locationCreated = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IStorageLocationMasterExecutor>().CreateAsync(new CreateStorageLocationExecution(
                    CommandId.From(commandIds[1]), Hash(2), ActorAccountId.From(actorId),
                    new CreateStorageLocationCommand(warehouseId, "LOC-P8", "測試儲位", "ตำแหน่งทดสอบ", true)), token), ct);
            Assert.True(locationCreated.IsSuccess);
            storageLocationId = locationCreated.Value.StorageLocationId;

            var procurementCreateExecution = new CreateProcurementProductExecution(
                CommandId.From(commandIds[2]), Hash(3), ActorAccountId.From(actorId),
                new CreateProcurementProductCommand("採購測試品", "สินค้าจัดซื้อทดสอบ", "kg", storageLocationId, true));
            var procurementCreated = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IProcurementProductMasterExecutor>().CreateAsync(procurementCreateExecution, token), ct);
            Assert.True(procurementCreated.IsSuccess);
            procurementProductId = procurementCreated.Value.ProcurementProductId;

            var procurementReplay = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IProcurementProductMasterExecutor>().CreateAsync(procurementCreateExecution, token), ct);
            Assert.True(procurementReplay.IsSuccess);
            Assert.Equal(procurementCreated.Value, procurementReplay.Value);

            await ExecuteNonQueryAsync(
                """
                INSERT INTO product.sales_product_groups
                    (id, name_zh_tw, name_th_th, active, row_version, created_at, created_by_account_id)
                VALUES (@id, '銷售測試群組', 'กลุ่มขายทดสอบ', true, 1, now(), @actor);
                """, ct, ("id", salesProductGroupId), ("actor", actorId));

            var salesCreated = await ExecuteAsync(
                (services, token) => services.GetRequiredService<ISalesProductMasterExecutor>().CreateAsync(new CreateSalesProductExecution(
                    CommandId.From(commandIds[3]), Hash(4), ActorAccountId.From(actorId),
                    new CreateSalesProductCommand(
                        salesProductGroupId, "銷售測試品", "สินค้าขายทดสอบ",
                        SalesPricingBasis.WEIGHT_BASED_UNIT, 0.25m, 1.5m, storageLocationId, true)), token), ct);
            Assert.True(salesCreated.IsSuccess);
            salesProductId = salesCreated.Value.SalesProductId;

            var warehouseUpdated = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IWarehouseMasterExecutor>().UpdateAsync(new UpdateWarehouseExecution(
                    CommandId.From(commandIds[4]), Hash(5), ActorAccountId.From(actorId),
                    new UpdateWarehouseCommand(warehouseId, 1, "WH-P8-2", "倉庫更新", "คลังปรับปรุง", false)), token), ct);
            Assert.True(warehouseUpdated.IsSuccess);
            Assert.Equal(2, warehouseUpdated.Value.RowVersion);

            var locationUpdated = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IStorageLocationMasterExecutor>().UpdateAsync(new UpdateStorageLocationExecution(
                    CommandId.From(commandIds[5]), Hash(6), ActorAccountId.From(actorId),
                    new UpdateStorageLocationCommand(storageLocationId, 1, warehouseId, "LOC-P8-2", "儲位更新", "ตำแหน่งปรับปรุง", false)), token), ct);
            Assert.True(locationUpdated.IsSuccess);
            Assert.Equal(2, locationUpdated.Value.RowVersion);

            var procurementUpdated = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IProcurementProductMasterExecutor>().UpdateAsync(new UpdateProcurementProductExecution(
                    CommandId.From(commandIds[6]), Hash(7), ActorAccountId.From(actorId),
                    new UpdateProcurementProductCommand(procurementProductId, 1, "採購產品更新", "สินค้าจัดซื้อปรับปรุง", "kg", storageLocationId, false)), token), ct);
            Assert.True(procurementUpdated.IsSuccess);
            Assert.Equal(2, procurementUpdated.Value.RowVersion);

            var salesUpdated = await ExecuteAsync(
                (services, token) => services.GetRequiredService<ISalesProductMasterExecutor>().UpdateAsync(new UpdateSalesProductExecution(
                    CommandId.From(commandIds[7]), Hash(8), ActorAccountId.From(actorId),
                    new UpdateSalesProductCommand(
                        salesProductId, 1, salesProductGroupId, "銷售產品更新", "สินค้าขายปรับปรุง",
                        SalesPricingBasis.UNIT_BASED, null, null, storageLocationId, false)), token), ct);
            Assert.True(salesUpdated.IsSuccess);
            Assert.Equal(2, salesUpdated.Value.RowVersion);

            var stale = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IProcurementProductMasterExecutor>().UpdateAsync(new UpdateProcurementProductExecution(
                    CommandId.From(commandIds[8]), Hash(9), ActorAccountId.From(actorId),
                    new UpdateProcurementProductCommand(procurementProductId, 1, "過期版本", null, "kg", storageLocationId, false)), token), ct);
            Assert.True(stale.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, stale.Error.Kind);
            Assert.Equal(ProcurementProductMasterErrorCodes.StaleRowVersion, stale.Error.Code);
            Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM system.command_executions WHERE command_id = @id;", ct, ("id", commandIds[8])));

            var warehousePage = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IWarehouseMasterReader>().GetAsync(
                    new WarehouseMasterQuery("WH-P8-2", InfrastructureMasterStatusFilter.Inactive, 0, 100), token), ct);
            Assert.Equal(warehouseId, Assert.Single(warehousePage.Items).Id);

            var locationPage = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IStorageLocationMasterReader>().GetAsync(
                    new StorageLocationMasterQuery("LOC-P8-2", InfrastructureMasterStatusFilter.Inactive, warehouseId, 0, 100), token), ct);
            Assert.Equal(storageLocationId, Assert.Single(locationPage.Items).Id);

            var procurementPage = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IProcurementProductMasterReader>().GetAsync(
                    new ProductMasterQuery("採購產品更新", ProductMasterStatusFilter.Inactive, 0, 100), token), ct);
            Assert.Equal(procurementProductId, Assert.Single(procurementPage.Items).Id);

            var salesPage = await ExecuteAsync(
                (services, token) => services.GetRequiredService<ISalesProductMasterReader>().GetAsync(
                    new ProductMasterQuery("銷售產品更新", ProductMasterStatusFilter.Inactive, 0, 100), token), ct);
            var salesProjection = Assert.Single(salesPage.Items);
            Assert.Equal(salesProductId, salesProjection.Id);
            Assert.Equal(SalesPricingBasis.UNIT_BASED, salesProjection.PricingBasis);
            Assert.Equal(storageLocationId, salesProjection.DefaultStorageLocationId);

            var storageThaiPage = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IStorageLocationMasterReader>().GetAsync(
                    new StorageLocationMasterQuery("LOC-P8-2", InfrastructureMasterStatusFilter.Inactive, warehouseId, 0, 100, "th-TH"), token), ct);
            Assert.Equal("คลังปรับปรุง", Assert.Single(storageThaiPage.Items).WarehouseDisplayName);

            var procurementThaiPage = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IProcurementProductMasterReader>().GetAsync(
                    new ProductMasterQuery("สินค้าจัดซื้อปรับปรุง", ProductMasterStatusFilter.Inactive, 0, 100, "th-TH"), token), ct);
            Assert.Equal("ตำแหน่งปรับปรุง", Assert.Single(procurementThaiPage.Items).DefaultStorageLocationDisplayName);

            var salesThaiPage = await ExecuteAsync(
                (services, token) => services.GetRequiredService<ISalesProductMasterReader>().GetAsync(
                    new ProductMasterQuery("สินค้าขายปรับปรุง", ProductMasterStatusFilter.Inactive, 0, 100, "th-TH"), token), ct);
            var salesThaiProjection = Assert.Single(salesThaiPage.Items);
            Assert.Equal("กลุ่มขายทดสอบ", salesThaiProjection.SalesProductGroupDisplayName);
            Assert.Equal("ตำแหน่งปรับปรุง", salesThaiProjection.DefaultStorageLocationDisplayName);

            await ExecuteNonQueryAsync(
                """
                INSERT INTO party.suppliers
                    (id, code, name_zh_tw, name_th_th, active, row_version, created_at, created_by_account_id)
                VALUES (@supplier, 'SUP-P8', '庫存供應商', 'ผู้ขายสต็อก', true, 1, now(), @actor);

                INSERT INTO procurement.procurement_batches
                    (id, procurement_date, procurement_product_id, procurement_status, lifecycle_status,
                     processing_route_id, processing_route_version_id, completed_at, completed_by_account_id,
                     closed_at, closed_by_account_id, row_version, created_at, created_by_account_id,
                     deleted_at, deleted_by_account_id)
                VALUES
                    (@batch, DATE '2026-09-09', @product, 'OPEN', 'ACTIVE', NULL, NULL, NULL, NULL,
                     NULL, NULL, 1, now(), @actor, NULL, NULL);

                INSERT INTO inventory.inventory_positions
                    (id, origin, procurement_batch_id, outsourced_supply_batch_id, inventory_object_kind,
                     procurement_product_id, process_material_id, sales_product_id, storage_location_id,
                     raw_source_kind, supplier_id, balance_quantity, row_version)
                VALUES
                    (@position, 'IN_HOUSE', @batch, NULL, 'PROCUREMENT_PRODUCT',
                     @product, NULL, NULL, @location, 'SUPPLIER', @supplier, -2.5, 1);
                """, ct,
                ("supplier", supplierId), ("actor", actorId), ("batch", procurementBatchId),
                ("product", procurementProductId), ("position", inventoryPositionId), ("location", storageLocationId));

            var inventoryPage = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IInventoryPositionReader>().GetAsync(
                    new InventoryPositionQuery(null, InventoryOrigin.IN_HOUSE, InventoryObjectKind.PROCUREMENT_PRODUCT, warehouseId, storageLocationId, false, 0, 100), token), ct);
            var position = Assert.Single(inventoryPage.Items, item => item.Id == inventoryPositionId);
            Assert.Equal(-2.5m, position.BalanceQuantity);
            Assert.Equal(procurementProductId, position.ObjectId);
            Assert.Equal(storageLocationId, position.StorageLocationId);
            Assert.Equal(supplierId, position.SupplierId);

            var inventoryThaiPage = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IInventoryPositionReader>().GetAsync(
                    new InventoryPositionQuery(null, InventoryOrigin.IN_HOUSE, InventoryObjectKind.PROCUREMENT_PRODUCT, warehouseId, storageLocationId, false, 0, 100, "th-TH"), token), ct);
            var thaiPosition = Assert.Single(inventoryThaiPage.Items, item => item.Id == inventoryPositionId);
            Assert.Equal("สินค้าจัดซื้อปรับปรุง", thaiPosition.ObjectDisplayName);
            Assert.Equal("คลังปรับปรุง", thaiPosition.WarehouseDisplayName);
            Assert.Equal("ตำแหน่งปรับปรุง", thaiPosition.StorageLocationDisplayName);
            Assert.Equal("SUP-P8", thaiPosition.SupplierDisplayName);

            Assert.Equal(8L, await ScalarAsync<long>(
                "SELECT count(*) FROM system.command_executions WHERE command_id = ANY(@ids) AND status = 'SUCCEEDED';",
                ct, ("ids", commandIds.Take(8).ToArray())));
            Assert.Equal(8L, await ScalarAsync<long>(
                "SELECT count(*) FROM audit.audit_events WHERE command_id = ANY(@ids);",
                ct, ("ids", commandIds.Take(8).ToArray())));
        }
        finally
        {
            await CleanupAsync(actorId, warehouseId, storageLocationId, procurementProductId, salesProductGroupId, salesProductId,
                supplierId, procurementBatchId, inventoryPositionId, commandIds, ct);
        }
    }

    private static async Task<TResult> ExecuteAsync<TResult>(
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

    private static async Task CleanupAsync(
        Guid actorId, Guid warehouseId, Guid storageLocationId, Guid procurementProductId, Guid salesProductGroupId,
        Guid salesProductId, Guid supplierId, Guid batchId, Guid positionId, IReadOnlyCollection<Guid> commandIds,
        CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            """
            DELETE FROM inventory.inventory_positions WHERE id = @position;
            DELETE FROM procurement.procurement_batches WHERE id = @batch;
            DELETE FROM party.suppliers WHERE id = @supplier;
            DELETE FROM product.sales_products WHERE id = @sales_product;
            DELETE FROM product.procurement_products WHERE id = @procurement_product;
            DELETE FROM product.sales_product_groups WHERE id = @sales_group;

            DELETE FROM audit.audit_event_subjects
            WHERE audit_event_id IN (SELECT id FROM audit.audit_events WHERE command_id = ANY(@command_ids));
            DELETE FROM audit.audit_events WHERE command_id = ANY(@command_ids);
            DELETE FROM system.command_executions WHERE command_id = ANY(@command_ids);

            DELETE FROM infrastructure.storage_locations WHERE id = @location;
            DELETE FROM infrastructure.warehouses WHERE id = @warehouse;
            DELETE FROM system.accounts WHERE id = @actor;
            """, ct,
            ("position", positionId), ("batch", batchId), ("supplier", supplierId),
            ("sales_product", salesProductId), ("procurement_product", procurementProductId), ("sales_group", salesProductGroupId),
            ("command_ids", commandIds.ToArray()), ("location", storageLocationId), ("warehouse", warehouseId), ("actor", actorId));
    }

    private static async Task<T> ScalarAsync<T>(string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        var value = await command.ExecuteScalarAsync(ct);
        return (T)(value ?? throw new InvalidOperationException("Expected scalar result."));
    }

    private static async Task ExecuteNonQueryAsync(string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string GetConnectionString() => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable))
        ? LocalDevelopmentConnectionString
        : Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)!;

    private static async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var builder = new NpgsqlConnectionStringBuilder(GetConnectionString()) { Pooling = false, Timeout = 5, CommandTimeout = 15 };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
