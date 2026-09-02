using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Processing;
using YowThi.Erp.Domain.ProcessingConfiguration;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class ProcessingExecutionOptionsIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Purpose_specific_options_follow_current_use_route_cascade_and_inventory_candidates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(cancellationToken);

        try
        {
            var services = new ServiceCollection();
            services.AddErpPersistence(GetConnectionString());
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var reader = scope.ServiceProvider.GetRequiredService<IProcessingExecutionOptionsReader>();

            var employees = await reader.GetEmployeesAsync(
                new ProcessingExecutionOptionsQuery("th-TH", scenario.EmployeeSearchText, 0, 50),
                cancellationToken);
            var employee = Assert.Single(employees.Items);
            Assert.Equal(scenario.EmployeeId, employee.Id);
            Assert.Equal(scenario.EmployeeZhTwName, employee.DisplayName);
            Assert.DoesNotContain(employees.Items, item => item.Id == scenario.InactiveEmployeeId);

            var suppliers = await reader.GetSuppliersAsync(
                new ProcessingExecutionOptionsQuery("th-TH", scenario.SupplierSearchText, 0, 50),
                cancellationToken);
            var supplier = Assert.Single(suppliers.Items);
            Assert.Equal(scenario.SupplierId, supplier.Id);
            Assert.Equal(scenario.SupplierThName, supplier.DisplayName);
            Assert.DoesNotContain(suppliers.Items, item => item.Id == scenario.InactiveSupplierId);

            var batches = await reader.GetBatchesAsync(
                new ProcessingExecutionOptionsQuery("zh-TW", scenario.ProductSearchText, 0, 50),
                cancellationToken);
            var batch = Assert.Single(batches.Items);
            Assert.Equal(scenario.ProcurementBatchId, batch.Id);
            Assert.Equal(scenario.RouteVersionId, batch.ProcessingRouteVersionId);
            Assert.Equal(scenario.ProcurementProductZhTwName, batch.ProcurementProductDisplayName);
            Assert.DoesNotContain(batches.Items, item => item.Id == scenario.NoRouteBatchId);

            var modules = await reader.GetModulesAsync(
                scenario.ProcurementBatchId,
                new ProcessingExecutionOptionsQuery("zh-TW", null, 0, 50),
                cancellationToken);
            Assert.NotNull(modules);
            Assert.Equal(scenario.RouteVersionId, modules.ProcessingRouteVersionId);
            Assert.Equal(3, modules.Modules.Items.Count);
            var sourceModule = Assert.Single(modules.Modules.Items, item => item.Id == scenario.SourceTrackedModuleId);
            Assert.Equal(ProcessingExecutionMode.SOURCE_TRACKED, sourceModule.ExecutionMode);
            Assert.Null(sourceModule.InputProcessMaterialId);
            Assert.False(sourceModule.InputUsesContainer);
            Assert.Null(sourceModule.InputContainerId);
            Assert.Null(sourceModule.DefaultInputContainerCount);

            Assert.Null(await reader.GetModulesAsync(
                scenario.NoRouteBatchId,
                new ProcessingExecutionOptionsQuery("zh-TW", null, 0, 50),
                cancellationToken));

            await InsertProcessMaterialPositionAsync(
                scenario,
                scenario.MaterialAId,
                scenario.PrimaryLocationId,
                10m,
                cancellationToken);

            var singleInputLocation = await reader.GetInputStorageLocationsAsync(
                scenario.ProcurementBatchId,
                scenario.PooledModuleId,
                new ProcessingExecutionOptionsQuery("zh-TW", null, 0, 50),
                cancellationToken);
            Assert.NotNull(singleInputLocation);
            Assert.Equal(scenario.PrimaryLocationId, singleInputLocation.AutoSelectionLocationId);
            Assert.Equal(scenario.PrimaryLocationId, Assert.Single(singleInputLocation.Locations.Items).Id);

            await InsertProcessMaterialPositionAsync(
                scenario,
                scenario.MaterialAId,
                scenario.SecondaryLocationId,
                5m,
                cancellationToken);

            var ambiguousInputLocations = await reader.GetInputStorageLocationsAsync(
                scenario.ProcurementBatchId,
                scenario.PooledModuleId,
                new ProcessingExecutionOptionsQuery("zh-TW", null, 0, 50),
                cancellationToken);
            Assert.NotNull(ambiguousInputLocations);
            Assert.Null(ambiguousInputLocations.AutoSelectionLocationId);
            Assert.Equal(2, ambiguousInputLocations.Locations.Items.Count);
            Assert.Contains(ambiguousInputLocations.Locations.Items, item => item.Id == scenario.PrimaryLocationId);
            Assert.Contains(ambiguousInputLocations.Locations.Items, item => item.Id == scenario.SecondaryLocationId);

            var finalOutputs = await reader.GetModuleOutputsAsync(
                scenario.ProcurementBatchId,
                scenario.FinalPackagingModuleId,
                "zh-TW",
                cancellationToken);
            Assert.NotNull(finalOutputs);
            var finalOutput = Assert.Single(finalOutputs.Outputs);
            Assert.Equal(scenario.FinalPackagingOutputId, finalOutput.Id);
            Assert.Equal(ProcessingOutputKind.SALES_PRODUCT, finalOutput.OutputKind);
            Assert.Equal(scenario.SalesProductId, finalOutput.TargetId);
            Assert.True(finalOutput.TargetAvailable);
            Assert.Equal(0.5m, finalOutput.PackagingWeight);
            Assert.Equal(scenario.PrimaryLocationId, finalOutput.DefaultStorageLocationId);
            Assert.True(finalOutput.DefaultStorageLocationAvailable);

            var sourceOutputs = await reader.GetModuleOutputsAsync(
                scenario.ProcurementBatchId,
                scenario.SourceTrackedModuleId,
                "th-TH",
                cancellationToken);
            Assert.NotNull(sourceOutputs);
            var sourceOutput = Assert.Single(sourceOutputs.Outputs);
            Assert.Equal(ProcessingOutputKind.PROCESS_MATERIAL, sourceOutput.OutputKind);
            Assert.Equal(scenario.MaterialAId, sourceOutput.TargetId);
            Assert.True(sourceOutput.TargetAvailable);
            Assert.Equal(scenario.MaterialAThName, sourceOutput.TargetDisplayName);

            var locations = await reader.GetStorageLocationsAsync(
                new ProcessingExecutionOptionsQuery("zh-TW", null, 0, 50),
                cancellationToken);
            Assert.Contains(locations.Items, item => item.Id == scenario.PrimaryLocationId);
            Assert.Contains(locations.Items, item => item.Id == scenario.SecondaryLocationId);
            Assert.DoesNotContain(locations.Items, item => item.Id == scenario.InactiveLocationId);

            Assert.Null(await reader.GetInputStorageLocationsAsync(
                scenario.ProcurementBatchId,
                Guid.CreateVersion7(),
                new ProcessingExecutionOptionsQuery("zh-TW", null, 0, 50),
                cancellationToken));
            Assert.Null(await reader.GetModuleOutputsAsync(
                Guid.CreateVersion7(),
                scenario.FinalPackagingModuleId,
                "zh-TW",
                cancellationToken));
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
            EmployeeId: Guid.CreateVersion7(),
            InactiveEmployeeId: Guid.CreateVersion7(),
            SupplierId: Guid.CreateVersion7(),
            InactiveSupplierId: Guid.CreateVersion7(),
            WarehouseId: Guid.CreateVersion7(),
            PrimaryLocationId: Guid.CreateVersion7(),
            SecondaryLocationId: Guid.CreateVersion7(),
            InactiveLocationId: Guid.CreateVersion7(),
            ProcurementProductId: Guid.CreateVersion7(),
            RouteId: Guid.CreateVersion7(),
            RouteVersionId: Guid.CreateVersion7(),
            MaterialAId: Guid.CreateVersion7(),
            MaterialBId: Guid.CreateVersion7(),
            SourceTrackedModuleId: Guid.CreateVersion7(),
            SourceTrackedOutputId: Guid.CreateVersion7(),
            PooledModuleId: Guid.CreateVersion7(),
            PooledOutputId: Guid.CreateVersion7(),
            FinalPackagingModuleId: Guid.CreateVersion7(),
            FinalPackagingOutputId: Guid.CreateVersion7(),
            SalesProductGroupId: Guid.CreateVersion7(),
            SalesProductId: Guid.CreateVersion7(),
            ProcurementBatchId: Guid.CreateVersion7(),
            NoRouteBatchId: Guid.CreateVersion7(),
            EmployeeZhTwName: "P6V3Q1員工Fallback",
            EmployeeSearchText: "Fallback",
            SupplierThName: "P6V3Q1ซัพพลายSearch",
            SupplierSearchText: "Search",
            ProcurementProductZhTwName: "P6V3Q1原料SearchProduct",
            ProductSearchText: "SearchProduct",
            MaterialAThName: "P6V3Q1วัตถุดิบA",
            WorkDate: new DateOnly(2026, 9, 2));

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES (@actor_id, 'P6 V3 Q1 actor', true, NULL, NULL, @now);

            INSERT INTO party.employees
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, created_at, created_by_account_id)
            VALUES
                (@employee_id, @employee_zh_name, NULL, NULL, NULL, NULL, NULL, true, @now, @actor_id),
                (@inactive_employee_id, 'P6V3Q1停用Fallback', NULL, NULL, NULL, NULL, NULL, false, @now, @actor_id);

            INSERT INTO party.suppliers
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, created_at, created_by_account_id)
            VALUES
                (@supplier_id, NULL, @supplier_th_name, NULL, NULL, NULL, NULL, true, @now, @actor_id),
                (@inactive_supplier_id, NULL, 'P6V3Q1ปิดSearch', NULL, NULL, NULL, NULL, false, @now, @actor_id);

            INSERT INTO infrastructure.warehouses
                (id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES (@warehouse_id, 'P6V3Q1', 'P6V3Q1倉庫', 'P6V3Q1คลัง', true, @now, @actor_id);

            INSERT INTO infrastructure.storage_locations
                (id, warehouse_id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@primary_location_id, @warehouse_id, 'P6V3Q1-A', 'P6V3Q1主儲位', 'P6V3Q1หลัก', true, @now, @actor_id),
                (@secondary_location_id, @warehouse_id, 'P6V3Q1-B', 'P6V3Q1次儲位', 'P6V3Q1รอง', true, @now, @actor_id),
                (@inactive_location_id, @warehouse_id, 'P6V3Q1-X', 'P6V3Q1停用儲位', 'P6V3Q1ปิด', false, @now, @actor_id);

            INSERT INTO product.procurement_products
                (id, name_zh_tw, name_th_th, unit_code, default_storage_location_id,
                 active, created_at, created_by_account_id)
            VALUES
                (@procurement_product_id, @product_zh_name, 'P6V3Q1วัตถุดิบ', 'kg', @primary_location_id,
                 true, @now, @actor_id);

            INSERT INTO product.sales_product_groups
                (id, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES (@sales_product_group_id, 'P6V3Q1產品群', 'P6V3Q1กลุ่ม', true, @now, @actor_id);

            INSERT INTO product.sales_products
                (id, sales_product_group_id, name_zh_tw, name_th_th, pricing_basis,
                 packaging_weight, sales_weight, default_storage_location_id,
                 active, created_at, created_by_account_id)
            VALUES
                (@sales_product_id, @sales_product_group_id, 'P6V3Q1成品', 'P6V3Q1สินค้า', 'UNIT_BASED',
                 0.5, NULL, @primary_location_id, true, @now, @actor_id);

            INSERT INTO processing_config.processing_routes
                (id, procurement_product_id, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES (@route_id, @procurement_product_id, 'P6V3Q1流程', 'P6V3Q1เส้นทาง', true, @now, @actor_id);

            INSERT INTO processing_config.processing_route_versions
                (id, processing_route_id, version_number, status)
            VALUES (@route_version_id, @route_id, 1, 'ACTIVE');

            INSERT INTO processing_config.route_input_configs
                (processing_route_version_id, uses_container, container_id, default_container_count, default_storage_location_id)
            VALUES (@route_version_id, false, NULL, NULL, @primary_location_id);

            INSERT INTO processing_config.process_materials
                (id, processing_route_version_id, name_zh_tw, name_th_th,
                 uses_container, container_id, default_container_count, default_storage_location_id,
                 active, created_at, created_by_account_id)
            VALUES
                (@material_a_id, @route_version_id, 'P6V3Q1半成品A', @material_a_th_name,
                 false, NULL, NULL, @primary_location_id, true, @now, @actor_id),
                (@material_b_id, @route_version_id, 'P6V3Q1半成品B', 'P6V3Q1วัตถุดิบB',
                 false, NULL, NULL, @primary_location_id, true, @now, @actor_id);

            INSERT INTO processing_config.processing_modules
                (id, processing_route_version_id, name_zh_tw, name_th_th,
                 execution_mode, input_process_material_id, negative_inventory_policy)
            VALUES
                (@source_module_id, @route_version_id, 'P6V3Q1來源處理', 'P6V3Q1ต้นทาง', 'SOURCE_TRACKED', NULL, 'CONFIGURED'),
                (@pooled_module_id, @route_version_id, 'P6V3Q1合併處理', 'P6V3Q1รวม', 'POOLED_OUTPUT', @material_a_id, 'CONFIGURED'),
                (@final_module_id, @route_version_id, 'P6V3Q1包裝', 'P6V3Q1บรรจุ', 'FINAL_PACKAGING', @material_b_id, 'CONFIGURED');

            INSERT INTO processing_config.processing_module_outputs
                (id, processing_module_id, processing_route_version_id, output_sequence,
                 output_kind, process_material_id, sales_product_id, default_wage_rate)
            VALUES
                (@source_output_id, @source_module_id, @route_version_id, 1, 'PROCESS_MATERIAL', @material_a_id, NULL, 1.25),
                (@pooled_output_id, @pooled_module_id, @route_version_id, 1, 'PROCESS_MATERIAL', @material_b_id, NULL, 1.50),
                (@final_output_id, @final_module_id, @route_version_id, 1, 'SALES_PRODUCT', NULL, @sales_product_id, 2.00);

            INSERT INTO procurement.procurement_batches
                (id, procurement_date, procurement_product_id, procurement_status,
                 lifecycle_status, processing_route_id, processing_route_version_id,
                 created_at, created_by_account_id)
            VALUES
                (@batch_id, @work_date, @procurement_product_id, 'OPEN', 'ACTIVE', @route_id, @route_version_id, @now, @actor_id),
                (@no_route_batch_id, @no_route_date, @procurement_product_id, 'OPEN', 'ACTIVE', NULL, NULL, @now, @actor_id);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("employee_id", scenario.EmployeeId),
            ("inactive_employee_id", scenario.InactiveEmployeeId),
            ("employee_zh_name", scenario.EmployeeZhTwName),
            ("supplier_id", scenario.SupplierId),
            ("inactive_supplier_id", scenario.InactiveSupplierId),
            ("supplier_th_name", scenario.SupplierThName),
            ("warehouse_id", scenario.WarehouseId),
            ("primary_location_id", scenario.PrimaryLocationId),
            ("secondary_location_id", scenario.SecondaryLocationId),
            ("inactive_location_id", scenario.InactiveLocationId),
            ("procurement_product_id", scenario.ProcurementProductId),
            ("product_zh_name", scenario.ProcurementProductZhTwName),
            ("route_id", scenario.RouteId),
            ("route_version_id", scenario.RouteVersionId),
            ("material_a_id", scenario.MaterialAId),
            ("material_b_id", scenario.MaterialBId),
            ("material_a_th_name", scenario.MaterialAThName),
            ("source_module_id", scenario.SourceTrackedModuleId),
            ("source_output_id", scenario.SourceTrackedOutputId),
            ("pooled_module_id", scenario.PooledModuleId),
            ("pooled_output_id", scenario.PooledOutputId),
            ("final_module_id", scenario.FinalPackagingModuleId),
            ("final_output_id", scenario.FinalPackagingOutputId),
            ("sales_product_group_id", scenario.SalesProductGroupId),
            ("sales_product_id", scenario.SalesProductId),
            ("batch_id", scenario.ProcurementBatchId),
            ("no_route_batch_id", scenario.NoRouteBatchId),
            ("work_date", scenario.WorkDate),
            ("no_route_date", scenario.WorkDate.AddDays(-1)),
            ("now", DateTimeOffset.UtcNow));

        return scenario;
    }

    private static Task InsertProcessMaterialPositionAsync(
        Scenario scenario,
        Guid materialId,
        Guid locationId,
        decimal balance,
        CancellationToken cancellationToken) =>
        ExecuteNonQueryAsync(
            """
            INSERT INTO inventory.inventory_positions
                (id, origin, procurement_batch_id, outsourced_supply_batch_id,
                 inventory_object_kind, procurement_product_id, process_material_id, sales_product_id,
                 storage_location_id, raw_source_kind, supplier_id, balance_quantity)
            VALUES
                (@id, 'IN_HOUSE', @batch_id, NULL,
                 'PROCESS_MATERIAL', NULL, @material_id, NULL,
                 @location_id, NULL, NULL, @balance);
            """,
            cancellationToken,
            ("id", Guid.CreateVersion7()),
            ("batch_id", scenario.ProcurementBatchId),
            ("material_id", materialId),
            ("location_id", locationId),
            ("balance", balance));

    private static async Task CleanupScenarioAsync(Scenario scenario, CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            """
            DELETE FROM inventory.inventory_positions WHERE procurement_batch_id = @batch_id;
            DELETE FROM procurement.procurement_batches WHERE id = ANY(@batch_ids);
            DELETE FROM processing_config.processing_module_outputs WHERE processing_route_version_id = @route_version_id;
            DELETE FROM processing_config.processing_modules WHERE processing_route_version_id = @route_version_id;
            DELETE FROM processing_config.process_materials WHERE processing_route_version_id = @route_version_id;
            DELETE FROM processing_config.route_input_configs WHERE processing_route_version_id = @route_version_id;
            DELETE FROM processing_config.processing_route_versions WHERE id = @route_version_id;
            DELETE FROM processing_config.processing_routes WHERE id = @route_id;
            DELETE FROM product.sales_products WHERE id = @sales_product_id;
            DELETE FROM product.sales_product_groups WHERE id = @sales_product_group_id;
            DELETE FROM product.procurement_products WHERE id = @procurement_product_id;
            DELETE FROM party.suppliers WHERE id = ANY(@supplier_ids);
            DELETE FROM party.employees WHERE id = ANY(@employee_ids);
            DELETE FROM infrastructure.storage_locations WHERE id = ANY(@location_ids);
            DELETE FROM infrastructure.warehouses WHERE id = @warehouse_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("batch_id", scenario.ProcurementBatchId),
            ("batch_ids", new[] { scenario.ProcurementBatchId, scenario.NoRouteBatchId }),
            ("route_version_id", scenario.RouteVersionId),
            ("route_id", scenario.RouteId),
            ("sales_product_id", scenario.SalesProductId),
            ("sales_product_group_id", scenario.SalesProductGroupId),
            ("procurement_product_id", scenario.ProcurementProductId),
            ("supplier_ids", new[] { scenario.SupplierId, scenario.InactiveSupplierId }),
            ("employee_ids", new[] { scenario.EmployeeId, scenario.InactiveEmployeeId }),
            ("location_ids", new[] { scenario.PrimaryLocationId, scenario.SecondaryLocationId, scenario.InactiveLocationId }),
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
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string GetConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        return string.IsNullOrWhiteSpace(connectionString) ? LocalDevelopmentConnectionString : connectionString;
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
        Guid EmployeeId,
        Guid InactiveEmployeeId,
        Guid SupplierId,
        Guid InactiveSupplierId,
        Guid WarehouseId,
        Guid PrimaryLocationId,
        Guid SecondaryLocationId,
        Guid InactiveLocationId,
        Guid ProcurementProductId,
        Guid RouteId,
        Guid RouteVersionId,
        Guid MaterialAId,
        Guid MaterialBId,
        Guid SourceTrackedModuleId,
        Guid SourceTrackedOutputId,
        Guid PooledModuleId,
        Guid PooledOutputId,
        Guid FinalPackagingModuleId,
        Guid FinalPackagingOutputId,
        Guid SalesProductGroupId,
        Guid SalesProductId,
        Guid ProcurementBatchId,
        Guid NoRouteBatchId,
        string EmployeeZhTwName,
        string EmployeeSearchText,
        string SupplierThName,
        string SupplierSearchText,
        string ProcurementProductZhTwName,
        string ProductSearchText,
        string MaterialAThName,
        DateOnly WorkDate);
}
