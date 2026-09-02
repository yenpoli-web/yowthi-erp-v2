using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Outsourced;
using YowThi.Erp.Domain.Product;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class OutsourcedSupplyDetailOptionsIntegrationTests
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
            var reader = scope.ServiceProvider.GetRequiredService<IOutsourcedSupplyDetailOptionsReader>();

            var vendorPage = await reader.GetVendorsAsync(
                new OutsourcedSupplyDetailOptionsQuery("th-TH", scenario.VendorSearchText, 0, 50),
                cancellationToken);

            var vendor = Assert.Single(vendorPage.Items);
            Assert.Equal(scenario.VendorId, vendor.Id);
            Assert.Equal(scenario.VendorZhTwName, vendor.DisplayName);
            Assert.DoesNotContain(vendorPage.Items, item => item.Id == scenario.InactiveVendorId);

            var productPage = await reader.GetProductsAsync(
                new OutsourcedSupplyDetailOptionsQuery("zh-TW", null, 0, 1),
                cancellationToken);

            Assert.Single(productPage.Items);
            Assert.Equal(1, productPage.NextOffset);

            var searchedProducts = await reader.GetProductsAsync(
                new OutsourcedSupplyDetailOptionsQuery("th-TH", scenario.ProductBSearchText, 0, 50),
                cancellationToken);

            var productB = Assert.Single(searchedProducts.Items);
            Assert.Equal(scenario.ProductBId, productB.Id);
            Assert.Equal(scenario.ProductBThaiName, productB.DisplayName);
            Assert.Equal(SalesPricingBasis.UNIT_BASED, productB.PricingBasis);
            Assert.DoesNotContain(searchedProducts.Items, item => item.Id == scenario.InactiveProductId);

            var locations = await reader.GetReceiptStorageLocationsAsync(
                scenario.ProductAId,
                new OutsourcedSupplyDetailOptionsQuery("zh-TW", null, 0, 50),
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
                new OutsourcedSupplyDetailOptionsQuery("th-TH", scenario.OtherLocationSearchText, 0, 50),
                cancellationToken);

            Assert.NotNull(noApplicableDefault);
            Assert.Null(noApplicableDefault.DefaultStorageLocationId);
            Assert.Single(noApplicableDefault.Locations.Items);
            Assert.Equal(scenario.OtherLocationId, noApplicableDefault.Locations.Items[0].Id);

            var missing = await reader.GetReceiptStorageLocationsAsync(
                Guid.CreateVersion7(),
                new OutsourcedSupplyDetailOptionsQuery("zh-TW", null, 0, 50),
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
            SalesProductGroupId: Guid.CreateVersion7(),
            ProductAId: Guid.CreateVersion7(),
            ProductBId: Guid.CreateVersion7(),
            InactiveProductId: Guid.CreateVersion7(),
            VendorId: Guid.CreateVersion7(),
            InactiveVendorId: Guid.CreateVersion7(),
            VendorZhTwName: "P6V2Q1委外商Fallback",
            VendorSearchText: "Fallback",
            ProductBThaiName: "P6V2Q1สินค้าSearch",
            ProductBSearchText: "Search",
            OtherLocationSearchText: "P6V2Q1Alt");

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 V2 Q1 actor', true, NULL, NULL, @now);

            INSERT INTO infrastructure.warehouses
                (id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@warehouse_id, 'P6V2Q1', 'P6V2Q1倉庫', 'P6V2Q1คลัง', true, @now, @actor_id);

            INSERT INTO infrastructure.storage_locations
                (id, warehouse_id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@default_location_id, @warehouse_id, 'P6V2Q1-D', 'P6V2Q1預設儲位', 'P6V2Q1ตำแหน่งหลัก', true, @now, @actor_id),
                (@other_location_id, @warehouse_id, 'P6V2Q1-O', 'P6V2Q1Alt儲位', 'P6V2Q1Altตำแหน่ง', true, @now, @actor_id),
                (@inactive_location_id, @warehouse_id, 'P6V2Q1-X', 'P6V2Q1停用儲位', 'P6V2Q1ปิด', false, @now, @actor_id);

            INSERT INTO product.sales_product_groups
                (id, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@group_id, 'P6V2Q1產品群', 'P6V2Q1กลุ่มสินค้า', true, @now, @actor_id);

            INSERT INTO product.sales_products
                (id, sales_product_group_id, name_zh_tw, name_th_th, pricing_basis,
                 packaging_weight, sales_weight, default_storage_location_id,
                 active, created_at, created_by_account_id)
            VALUES
                (@product_a_id, @group_id, 'P6V2Q1鳳梨', 'P6V2Q1สับปะรด', 'UNIT_BASED', NULL, NULL, @default_location_id, true, @now, @actor_id),
                (@product_b_id, @group_id, NULL, @product_b_th_name, 'UNIT_BASED', NULL, NULL, @inactive_location_id, true, @now, @actor_id),
                (@inactive_product_id, @group_id, 'P6V2Q1停用產品', 'P6V2Q1ปิดสินค้า', 'UNIT_BASED', NULL, NULL, NULL, false, @now, @actor_id);

            INSERT INTO party.outsourced_vendors
                (id, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@vendor_id, @vendor_zh_name, NULL, true, @now, @actor_id),
                (@inactive_vendor_id, 'P6V2Q1停用Fallback', NULL, false, @now, @actor_id);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("warehouse_id", scenario.WarehouseId),
            ("default_location_id", scenario.DefaultLocationId),
            ("other_location_id", scenario.OtherLocationId),
            ("inactive_location_id", scenario.InactiveLocationId),
            ("group_id", scenario.SalesProductGroupId),
            ("product_a_id", scenario.ProductAId),
            ("product_b_id", scenario.ProductBId),
            ("inactive_product_id", scenario.InactiveProductId),
            ("vendor_id", scenario.VendorId),
            ("inactive_vendor_id", scenario.InactiveVendorId),
            ("product_b_th_name", scenario.ProductBThaiName),
            ("vendor_zh_name", scenario.VendorZhTwName),
            ("now", DateTimeOffset.UtcNow));

        return scenario;
    }

    private static async Task CleanupScenarioAsync(Scenario scenario, CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            """
            DELETE FROM product.sales_products
            WHERE id = ANY(@product_ids);

            DELETE FROM product.sales_product_groups
            WHERE id = @group_id;

            DELETE FROM infrastructure.storage_locations
            WHERE id = ANY(@location_ids);

            DELETE FROM infrastructure.warehouses
            WHERE id = @warehouse_id;

            DELETE FROM party.outsourced_vendors
            WHERE id = ANY(@vendor_ids);

            DELETE FROM system.accounts
            WHERE id = @actor_id;
            """,
            cancellationToken,
            ("product_ids", new[] { scenario.ProductAId, scenario.ProductBId, scenario.InactiveProductId }),
            ("group_id", scenario.SalesProductGroupId),
            ("location_ids", new[] { scenario.DefaultLocationId, scenario.OtherLocationId, scenario.InactiveLocationId }),
            ("warehouse_id", scenario.WarehouseId),
            ("vendor_ids", new[] { scenario.VendorId, scenario.InactiveVendorId }),
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
        Guid SalesProductGroupId,
        Guid ProductAId,
        Guid ProductBId,
        Guid InactiveProductId,
        Guid VendorId,
        Guid InactiveVendorId,
        string VendorZhTwName,
        string VendorSearchText,
        string ProductBThaiName,
        string ProductBSearchText,
        string OtherLocationSearchText);
}
