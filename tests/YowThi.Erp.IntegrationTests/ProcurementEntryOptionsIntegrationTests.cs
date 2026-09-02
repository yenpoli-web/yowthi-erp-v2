using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Procurement;
using YowThi.Erp.Domain.Procurement;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class ProcurementEntryOptionsIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Purpose_specific_options_resolve_locale_filter_current_use_and_expose_applicable_default()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(cancellationToken);

        try
        {
            var services = new ServiceCollection();
            services.AddErpPersistence(GetConnectionString());
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var reader = scope.ServiceProvider.GetRequiredService<IProcurementEntryOptionsReader>();

            var productPage = await reader.GetProductsAsync(
                new ProcurementEntryOptionsQuery("zh-TW", null, 0, 1),
                cancellationToken);

            Assert.Single(productPage.Items);
            Assert.Equal(1, productPage.NextOffset);

            var searchedProducts = await reader.GetProductsAsync(
                new ProcurementEntryOptionsQuery("th-TH", scenario.ProductBSearchText, 0, 50),
                cancellationToken);

            var productB = Assert.Single(searchedProducts.Items);
            Assert.Equal(scenario.ProductBId, productB.Id);
            Assert.Equal(scenario.ProductBThaiName, productB.DisplayName);
            Assert.DoesNotContain(searchedProducts.Items, item => item.Id == scenario.InactiveProductId);

            var supplierPage = await reader.GetSourcesAsync(
                ProcurementSourceType.SUPPLIER,
                new ProcurementEntryOptionsQuery("th-TH", scenario.SupplierSearchText, 0, 50),
                cancellationToken);

            var supplier = Assert.Single(supplierPage.Items);
            Assert.Equal(scenario.SupplierId, supplier.Id);
            Assert.Equal(scenario.SupplierZhTwName, supplier.DisplayName);
            Assert.DoesNotContain(supplierPage.Items, item => item.Id == scenario.InactiveSupplierId);

            var farmerPage = await reader.GetSourcesAsync(
                ProcurementSourceType.FARMER,
                new ProcurementEntryOptionsQuery("zh-TW", scenario.FarmerSearchText, 0, 50),
                cancellationToken);

            var farmer = Assert.Single(farmerPage.Items);
            Assert.Equal(scenario.FarmerId, farmer.Id);
            Assert.Equal(scenario.FarmerThaiName, farmer.DisplayName);

            var locations = await reader.GetReceiptStorageLocationsAsync(
                scenario.ProductAId,
                new ProcurementEntryOptionsQuery("zh-TW", null, 0, 50),
                cancellationToken);

            Assert.NotNull(locations);
            Assert.Equal(scenario.DefaultLocationId, locations.DefaultStorageLocationId);
            Assert.Contains(
                locations.Locations.Items,
                item => item.Id == scenario.DefaultLocationId && item.IsProductDefault);
            Assert.Contains(
                locations.Locations.Items,
                item => item.Id == scenario.OtherLocationId && !item.IsProductDefault);
            Assert.DoesNotContain(locations.Locations.Items, item => item.Id == scenario.InactiveLocationId);

            var noApplicableDefault = await reader.GetReceiptStorageLocationsAsync(
                scenario.ProductBId,
                new ProcurementEntryOptionsQuery("th-TH", scenario.OtherLocationSearchText, 0, 50),
                cancellationToken);

            Assert.NotNull(noApplicableDefault);
            Assert.Null(noApplicableDefault.DefaultStorageLocationId);
            Assert.Single(noApplicableDefault.Locations.Items);
            Assert.Equal(scenario.OtherLocationId, noApplicableDefault.Locations.Items[0].Id);

            var missing = await reader.GetReceiptStorageLocationsAsync(
                Guid.CreateVersion7(),
                new ProcurementEntryOptionsQuery("zh-TW", null, 0, 50),
                cancellationToken);

            Assert.Null(missing);
        }
        finally
        {
            await CleanupScenarioAsync(scenario, cancellationToken);
        }
    }

    private static async Task<Scenario> SeedScenarioAsync(CancellationToken cancellationToken)
    {
        var scenario = new Scenario(
            ActorAccountId: Guid.CreateVersion7(),
            WarehouseId: Guid.CreateVersion7(),
            DefaultLocationId: Guid.CreateVersion7(),
            OtherLocationId: Guid.CreateVersion7(),
            InactiveLocationId: Guid.CreateVersion7(),
            ProductAId: Guid.CreateVersion7(),
            ProductBId: Guid.CreateVersion7(),
            InactiveProductId: Guid.CreateVersion7(),
            SupplierId: Guid.CreateVersion7(),
            InactiveSupplierId: Guid.CreateVersion7(),
            FarmerId: Guid.CreateVersion7(),
            SupplierZhTwName: "P6Q1供應商Fallback",
            SupplierSearchText: "Fallback",
            FarmerThaiName: "P6Q1เกษตรกรFallback",
            FarmerSearchText: "Fallback",
            ProductBThaiName: "P6Q1มะม่วงSearch",
            ProductBSearchText: "Search",
            OtherLocationSearchText: "Other");

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 Q1 actor', true, NULL, NULL, @now);

            INSERT INTO infrastructure.warehouses
                (id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@warehouse_id, 'P6Q1', 'P6Q1倉庫', 'P6Q1คลัง', true, @now, @actor_id);

            INSERT INTO infrastructure.storage_locations
                (id, warehouse_id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@default_location_id, @warehouse_id, 'P6Q1-D', 'P6Q1預設儲位', 'P6Q1ตำแหน่งหลัก', true, @now, @actor_id),
                (@other_location_id, @warehouse_id, 'P6Q1-O', 'P6Q1Other儲位', 'P6Q1Otherตำแหน่ง', true, @now, @actor_id),
                (@inactive_location_id, @warehouse_id, 'P6Q1-X', 'P6Q1停用儲位', 'P6Q1ปิด', false, @now, @actor_id);

            INSERT INTO product.procurement_products
                (id, name_zh_tw, name_th_th, unit_code, default_storage_location_id,
                 active, created_at, created_by_account_id)
            VALUES
                (@product_a_id, 'P6Q1鳳梨', 'P6Q1สับปะรด', 'kg', @default_location_id, true, @now, @actor_id),
                (@product_b_id, NULL, @product_b_th_name, 'kg', @inactive_location_id, true, @now, @actor_id),
                (@inactive_product_id, 'P6Q1停用產品', 'P6Q1ปิดสินค้า', 'kg', NULL, false, @now, @actor_id);

            INSERT INTO party.suppliers
                (id, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@supplier_id, @supplier_zh_name, NULL, true, @now, @actor_id),
                (@inactive_supplier_id, 'P6Q1停用Fallback', NULL, false, @now, @actor_id);

            INSERT INTO party.farmers
                (id, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@farmer_id, NULL, @farmer_th_name, true, @now, @actor_id);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("warehouse_id", scenario.WarehouseId),
            ("default_location_id", scenario.DefaultLocationId),
            ("other_location_id", scenario.OtherLocationId),
            ("inactive_location_id", scenario.InactiveLocationId),
            ("product_a_id", scenario.ProductAId),
            ("product_b_id", scenario.ProductBId),
            ("inactive_product_id", scenario.InactiveProductId),
            ("supplier_id", scenario.SupplierId),
            ("inactive_supplier_id", scenario.InactiveSupplierId),
            ("farmer_id", scenario.FarmerId),
            ("product_b_th_name", scenario.ProductBThaiName),
            ("supplier_zh_name", scenario.SupplierZhTwName),
            ("farmer_th_name", scenario.FarmerThaiName),
            ("now", DateTimeOffset.UtcNow));

        return scenario;
    }

    private static async Task CleanupScenarioAsync(Scenario scenario, CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            """
            DELETE FROM product.procurement_products
            WHERE id = ANY(@product_ids);

            DELETE FROM infrastructure.storage_locations
            WHERE id = ANY(@location_ids);

            DELETE FROM infrastructure.warehouses
            WHERE id = @warehouse_id;

            DELETE FROM party.suppliers
            WHERE id = ANY(@supplier_ids);

            DELETE FROM party.farmers
            WHERE id = @farmer_id;

            DELETE FROM system.accounts
            WHERE id = @actor_id;
            """,
            cancellationToken,
            ("product_ids", new[] { scenario.ProductAId, scenario.ProductBId, scenario.InactiveProductId }),
            ("location_ids", new[] { scenario.DefaultLocationId, scenario.OtherLocationId, scenario.InactiveLocationId }),
            ("warehouse_id", scenario.WarehouseId),
            ("supplier_ids", new[] { scenario.SupplierId, scenario.InactiveSupplierId }),
            ("farmer_id", scenario.FarmerId),
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
        Guid OtherLocationId,
        Guid InactiveLocationId,
        Guid ProductAId,
        Guid ProductBId,
        Guid InactiveProductId,
        Guid SupplierId,
        Guid InactiveSupplierId,
        Guid FarmerId,
        string SupplierZhTwName,
        string SupplierSearchText,
        string FarmerThaiName,
        string FarmerSearchText,
        string ProductBThaiName,
        string ProductBSearchText,
        string OtherLocationSearchText);
}
