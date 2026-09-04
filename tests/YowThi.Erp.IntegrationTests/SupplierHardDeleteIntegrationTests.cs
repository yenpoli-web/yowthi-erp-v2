using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.DataProtection;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class SupplierHardDeleteIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Hard_delete_physically_removes_supplier_retains_audit_and_replays_after_target_is_gone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedIsolatedSupplierAsync(cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            var command = new HardDeleteSupplierCommand(scenario.SupplierId, 1);
            var execution = Execution(commandId, scenario.ActorAccountId, Hash(1), command);

            var result = await ExecuteAsync(execution, cancellationToken);
            Assert.True(result.IsSuccess);
            Assert.Equal(scenario.SupplierId, result.Value.SupplierId);
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM party.suppliers WHERE id = @supplier_id;",
                    cancellationToken,
                    ("supplier_id", scenario.SupplierId)));

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.audit_events
                    WHERE command_id = @command_id
                      AND command_type = 'HardDeleteSupplier'
                      AND event_kind = 'HARD_DELETE';
                    """,
                    cancellationToken,
                    ("command_id", commandId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.audit_event_subjects s
                    JOIN audit.audit_events e ON e.id = s.audit_event_id
                    WHERE e.command_id = @command_id
                      AND s.subject_kind = 'party.supplier'
                      AND s.change_kind = 'HARD_DELETE'
                      AND s.subject_key ->> 'id' = @supplier_id_text
                      AND s.before_row_version = 1
                      AND s.after_row_version IS NULL;
                    """,
                    cancellationToken,
                    ("command_id", commandId),
                    ("supplier_id_text", scenario.SupplierId.ToString())));

            var replay = await ExecuteAsync(execution, cancellationToken);
            Assert.True(replay.IsSuccess);
            Assert.Equal(result.Value, replay.Value);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", commandId)));

            var changedHash = await ExecuteAsync(
                Execution(commandId, scenario.ActorAccountId, Hash(2), command),
                cancellationToken);
            Assert.True(changedHash.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, changedHash.Error.Kind);
            Assert.Equal(SupplierHardDeleteErrorCodes.IdempotencyKeyReused, changedHash.Error.Code);
        }
        finally
        {
            await CleanupIsolatedSupplierAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    [Theory]
    [InlineData("procurement-entry")]
    [InlineData("processing-execution")]
    [InlineData("inventory-movement")]
    [InlineData("inventory-position")]
    [InlineData("procurement-supplier-payable")]
    public async Task Hard_delete_blocks_every_typed_supplier_dependency_without_committing_command_identity(
        string dependencyKind)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedDependencyScenarioAsync(dependencyKind, cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            var result = await ExecuteAsync(
                Execution(
                    commandId,
                    scenario.ActorAccountId,
                    Hash(3),
                    new HardDeleteSupplierCommand(scenario.SupplierId, 1)),
                cancellationToken);

            Assert.True(result.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, result.Error.Kind);
            Assert.Equal(SupplierHardDeleteErrorCodes.DependencyBlocked, result.Error.Code);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM party.suppliers WHERE id = @supplier_id;",
                    cancellationToken,
                    ("supplier_id", scenario.SupplierId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", commandId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", commandId)));
        }
        finally
        {
            await CleanupDependencyScenarioAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Hard_delete_rejects_stale_row_version_and_rolls_back_command_acquisition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedIsolatedSupplierAsync(cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            var result = await ExecuteAsync(
                Execution(
                    commandId,
                    scenario.ActorAccountId,
                    Hash(4),
                    new HardDeleteSupplierCommand(scenario.SupplierId, 2)),
                cancellationToken);

            Assert.True(result.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, result.Error.Kind);
            Assert.Equal(SupplierHardDeleteErrorCodes.StaleRowVersion, result.Error.Code);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM party.suppliers WHERE id = @supplier_id;",
                    cancellationToken,
                    ("supplier_id", scenario.SupplierId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", commandId)));
        }
        finally
        {
            await CleanupIsolatedSupplierAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    private static HardDeleteSupplierExecution Execution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        HardDeleteSupplierCommand command) =>
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

    private static async ValueTask<ApplicationResult<HardDeleteSupplierResult>> ExecuteAsync(
        HardDeleteSupplierExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IHardDeleteSupplierExecutor>()
            .ExecuteAsync(execution, cancellationToken);
    }

    private static async Task<IsolatedSupplierScenario> SeedIsolatedSupplierAsync(CancellationToken cancellationToken)
    {
        var scenario = new IsolatedSupplierScenario(
            ActorAccountId: Guid.CreateVersion7(),
            SupplierId: Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 V8 hard delete actor', true, NULL, NULL, @now);

            INSERT INTO party.suppliers
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, row_version, created_at, created_by_account_id)
            VALUES
                (@supplier_id, 'P6 V8 isolated supplier', NULL, NULL, NULL, NULL, NULL,
                 true, 1, @now, @actor_id);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("supplier_id", scenario.SupplierId),
            ("now", now));

        return scenario;
    }

    private static async Task<DependencyScenario> SeedDependencyScenarioAsync(
        string dependencyKind,
        CancellationToken cancellationToken)
    {
        var scenario = new DependencyScenario(
            ActorAccountId: Guid.CreateVersion7(),
            SupplierId: Guid.CreateVersion7(),
            EmployeeId: Guid.CreateVersion7(),
            WarehouseId: Guid.CreateVersion7(),
            LocationId: Guid.CreateVersion7(),
            ProcurementProductId: Guid.CreateVersion7(),
            RouteId: Guid.CreateVersion7(),
            RouteVersionId: Guid.CreateVersion7(),
            ProcessingModuleId: Guid.CreateVersion7(),
            ProcurementBatchId: Guid.CreateVersion7(),
            InventoryOperationId: Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 V8 dependency actor', true, NULL, NULL, @now);

            INSERT INTO party.suppliers
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, row_version, created_at, created_by_account_id)
            VALUES
                (@supplier_id, 'P6 V8 dependency supplier', NULL, NULL, NULL, NULL, NULL,
                 true, 1, @now, @actor_id);

            INSERT INTO party.employees
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, created_at, created_by_account_id)
            VALUES
                (@employee_id, 'P6 V8 dependency employee', NULL, NULL, NULL, NULL, NULL,
                 true, @now, @actor_id);

            INSERT INTO infrastructure.warehouses
                (id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@warehouse_id, NULL, 'P6 V8 dependency warehouse', NULL, true, @now, @actor_id);

            INSERT INTO infrastructure.storage_locations
                (id, warehouse_id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@location_id, @warehouse_id, NULL, 'P6 V8 dependency location', NULL, true, @now, @actor_id);

            INSERT INTO product.procurement_products
                (id, name_zh_tw, name_th_th, unit_code, default_storage_location_id,
                 active, created_at, created_by_account_id)
            VALUES
                (@product_id, 'P6 V8 dependency product', NULL, 'kg', @location_id,
                 true, @now, @actor_id);

            INSERT INTO processing_config.processing_routes
                (id, procurement_product_id, name_zh_tw, name_th_th,
                 active, created_at, created_by_account_id)
            VALUES
                (@route_id, @product_id, 'P6 V8 dependency route', NULL,
                 true, @now, @actor_id);

            INSERT INTO processing_config.processing_route_versions
                (id, processing_route_id, version_number, status)
            VALUES
                (@route_version_id, @route_id, 1, 'ACTIVE');

            INSERT INTO processing_config.processing_modules
                (id, processing_route_version_id, name_zh_tw, name_th_th,
                 execution_mode, input_process_material_id, negative_inventory_policy)
            VALUES
                (@module_id, @route_version_id, 'P6 V8 dependency module', NULL,
                 'SOURCE_TRACKED', NULL, 'CONFIGURED');

            INSERT INTO procurement.procurement_batches
                (id, procurement_date, procurement_product_id, procurement_status,
                 lifecycle_status, processing_route_id, processing_route_version_id,
                 created_at, created_by_account_id)
            VALUES
                (@batch_id, DATE '2026-09-04', @product_id, 'OPEN',
                 'ACTIVE', @route_id, @route_version_id,
                 @now, @actor_id);

            INSERT INTO inventory.inventory_operations
                (id, operation_type, recorded_at, recorded_by_account_id)
            VALUES
                (@operation_id, 'ADJUSTMENT', @now, @actor_id);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("supplier_id", scenario.SupplierId),
            ("employee_id", scenario.EmployeeId),
            ("warehouse_id", scenario.WarehouseId),
            ("location_id", scenario.LocationId),
            ("product_id", scenario.ProcurementProductId),
            ("route_id", scenario.RouteId),
            ("route_version_id", scenario.RouteVersionId),
            ("module_id", scenario.ProcessingModuleId),
            ("batch_id", scenario.ProcurementBatchId),
            ("operation_id", scenario.InventoryOperationId),
            ("now", now));

        var dependencySql = dependencyKind switch
        {
            "procurement-entry" =>
                """
                INSERT INTO procurement.procurement_entries
                    (id, procurement_batch_id, source_type, supplier_id, farmer_id,
                     net_quantity, unit_code_snapshot, unit_price, amount_thb,
                     company_pickup, recorded_at, recorded_by_account_id, row_version)
                VALUES
                    (@dependency_id, @batch_id, 'SUPPLIER', @supplier_id, NULL,
                     1, 'kg', 1, 1,
                     false, @now, @actor_id, 1);
                """,
            "processing-execution" =>
                """
                INSERT INTO processing.processing_executions
                    (id, work_date, employee_id, procurement_batch_id,
                     processing_route_version_id, processing_module_id,
                     execution_mode_snapshot, negative_inventory_policy_snapshot,
                     processing_source_kind, supplier_id,
                     recorded_at, recorded_by_account_id, row_version)
                VALUES
                    (@dependency_id, DATE '2026-09-04', @employee_id, @batch_id,
                     @route_version_id, @module_id,
                     'SOURCE_TRACKED', 'CONFIGURED',
                     'SUPPLIER', @supplier_id,
                     @now, @actor_id, 1);
                """,
            "inventory-movement" =>
                """
                INSERT INTO inventory.inventory_movements
                    (id, inventory_operation_id, sequence, movement_type, origin,
                     procurement_batch_id, outsourced_supply_batch_id, inventory_object_kind,
                     procurement_product_id, process_material_id, sales_product_id,
                     storage_location_id, raw_source_kind, supplier_id,
                     quantity_delta, sales_allocation_revision_item_id, recorded_at)
                VALUES
                    (@dependency_id, @operation_id, 1, 'ADJUSTMENT', 'IN_HOUSE',
                     @batch_id, NULL, 'PROCUREMENT_PRODUCT',
                     @product_id, NULL, NULL,
                     @location_id, 'SUPPLIER', @supplier_id,
                     1, NULL, @now);
                """,
            "inventory-position" =>
                """
                INSERT INTO inventory.inventory_positions
                    (id, origin, procurement_batch_id, outsourced_supply_batch_id,
                     inventory_object_kind, procurement_product_id, process_material_id, sales_product_id,
                     storage_location_id, raw_source_kind, supplier_id, balance_quantity, row_version)
                VALUES
                    (@dependency_id, 'IN_HOUSE', @batch_id, NULL,
                     'PROCUREMENT_PRODUCT', @product_id, NULL, NULL,
                     @location_id, 'SUPPLIER', @supplier_id, 1, 1);
                """,
            "procurement-supplier-payable" =>
                """
                INSERT INTO finance.payables
                    (id, payable_kind, procurement_batch_id, supplier_id, farmer_id,
                     outsourced_supply_detail_id, employee_daily_wage_id, created_at, row_version)
                VALUES
                    (@dependency_id, 'PROCUREMENT_SUPPLIER', @batch_id, @supplier_id, NULL,
                     NULL, NULL, @now, 1);
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(dependencyKind), dependencyKind, null),
        };

        await ExecuteNonQueryAsync(
            dependencySql,
            cancellationToken,
            ("dependency_id", Guid.CreateVersion7()),
            ("actor_id", scenario.ActorAccountId),
            ("supplier_id", scenario.SupplierId),
            ("employee_id", scenario.EmployeeId),
            ("location_id", scenario.LocationId),
            ("product_id", scenario.ProcurementProductId),
            ("route_version_id", scenario.RouteVersionId),
            ("module_id", scenario.ProcessingModuleId),
            ("batch_id", scenario.ProcurementBatchId),
            ("operation_id", scenario.InventoryOperationId),
            ("now", now));

        return scenario;
    }

    private static Task CleanupIsolatedSupplierAsync(
        IsolatedSupplierScenario scenario,
        IReadOnlyCollection<Guid> commandIds,
        CancellationToken cancellationToken) =>
        ExecuteNonQueryAsync(
            """
            DELETE FROM audit.audit_event_subjects
            WHERE audit_event_id IN (SELECT id FROM audit.audit_events WHERE command_id = ANY(@command_ids));
            DELETE FROM audit.audit_events WHERE command_id = ANY(@command_ids);
            DELETE FROM system.command_executions WHERE command_id = ANY(@command_ids);
            DELETE FROM party.suppliers WHERE id = @supplier_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds.ToArray()),
            ("supplier_id", scenario.SupplierId),
            ("actor_id", scenario.ActorAccountId));

    private static Task CleanupDependencyScenarioAsync(
        DependencyScenario scenario,
        IReadOnlyCollection<Guid> commandIds,
        CancellationToken cancellationToken) =>
        ExecuteNonQueryAsync(
            """
            DELETE FROM audit.audit_event_subjects
            WHERE audit_event_id IN (SELECT id FROM audit.audit_events WHERE command_id = ANY(@command_ids));
            DELETE FROM audit.audit_events WHERE command_id = ANY(@command_ids);
            DELETE FROM system.command_executions WHERE command_id = ANY(@command_ids);

            DELETE FROM finance.payables WHERE supplier_id = @supplier_id;
            DELETE FROM inventory.inventory_movements WHERE supplier_id = @supplier_id;
            DELETE FROM inventory.inventory_positions WHERE supplier_id = @supplier_id;
            DELETE FROM processing.processing_executions WHERE supplier_id = @supplier_id;
            DELETE FROM procurement.procurement_entries WHERE supplier_id = @supplier_id;
            DELETE FROM inventory.inventory_operations WHERE id = @operation_id;
            DELETE FROM procurement.procurement_batches WHERE id = @batch_id;
            DELETE FROM processing_config.processing_modules WHERE id = @module_id;
            DELETE FROM processing_config.processing_route_versions WHERE id = @route_version_id;
            DELETE FROM processing_config.processing_routes WHERE id = @route_id;
            DELETE FROM product.procurement_products WHERE id = @product_id;
            DELETE FROM party.suppliers WHERE id = @supplier_id;
            DELETE FROM party.employees WHERE id = @employee_id;
            DELETE FROM infrastructure.storage_locations WHERE id = @location_id;
            DELETE FROM infrastructure.warehouses WHERE id = @warehouse_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds.ToArray()),
            ("supplier_id", scenario.SupplierId),
            ("operation_id", scenario.InventoryOperationId),
            ("batch_id", scenario.ProcurementBatchId),
            ("module_id", scenario.ProcessingModuleId),
            ("route_version_id", scenario.RouteVersionId),
            ("route_id", scenario.RouteId),
            ("product_id", scenario.ProcurementProductId),
            ("employee_id", scenario.EmployeeId),
            ("location_id", scenario.LocationId),
            ("warehouse_id", scenario.WarehouseId),
            ("actor_id", scenario.ActorAccountId));

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

    private sealed record IsolatedSupplierScenario(Guid ActorAccountId, Guid SupplierId);

    private sealed record DependencyScenario(
        Guid ActorAccountId,
        Guid SupplierId,
        Guid EmployeeId,
        Guid WarehouseId,
        Guid LocationId,
        Guid ProcurementProductId,
        Guid RouteId,
        Guid RouteVersionId,
        Guid ProcessingModuleId,
        Guid ProcurementBatchId,
        Guid InventoryOperationId);
}
