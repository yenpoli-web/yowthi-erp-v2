using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Infrastructure;
using YowThi.Erp.Application.Processing;
using YowThi.Erp.Domain.Processing;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class ContainerLifecycleIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Soft_delete_preserves_processing_configuration_references_then_restore_with_audit_and_replay()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedConfigurationScenarioAsync(containerActive: true, cancellationToken);
        var softDeleteCommandId = Guid.CreateVersion7();
        var restoreCommandId = Guid.CreateVersion7();

        try
        {
            var execution = new SoftDeleteContainerExecution(
                CommandId.From(softDeleteCommandId),
                Hash(1),
                ActorAccountId.From(scenario.ActorAccountId),
                new SoftDeleteContainerCommand(scenario.ContainerId, 1));
            var softDelete = await ExecuteSoftDeleteAsync(execution, cancellationToken);

            Assert.True(softDelete.IsSuccess);
            Assert.Equal(new ContainerLifecycleResult(scenario.ContainerId, 2, true), softDelete.Value);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM infrastructure.containers
                    WHERE id = @container_id
                      AND active = true
                      AND deleted_at IS NOT NULL
                      AND deleted_by_account_id = @actor_id
                      AND row_version = 2;
                    """,
                    cancellationToken,
                    ("container_id", scenario.ContainerId),
                    ("actor_id", scenario.ActorAccountId)));

            Assert.Equal(
                2L,
                await ScalarAsync<long>(
                    """
                    SELECT
                        (SELECT count(*) FROM processing_config.route_input_configs
                         WHERE processing_route_version_id = @route_version_id
                           AND uses_container = true
                           AND container_id = @container_id
                           AND default_container_count = 1)
                      + (SELECT count(*) FROM processing_config.process_materials
                         WHERE id = @material_id
                           AND processing_route_version_id = @route_version_id
                           AND uses_container = true
                           AND container_id = @container_id
                           AND default_container_count = 2
                           AND active = true
                           AND deleted_at IS NULL
                           AND row_version = 1);
                    """,
                    cancellationToken,
                    ("route_version_id", scenario.RouteVersionId),
                    ("container_id", scenario.ContainerId),
                    ("material_id", scenario.MaterialId)));

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.audit_event_subjects s
                    JOIN audit.audit_events e ON e.id = s.audit_event_id
                    WHERE e.command_id = @command_id
                      AND e.command_type = 'SoftDeleteContainer'
                      AND e.event_kind = 'DATA_LIFECYCLE'
                      AND s.subject_kind = 'infrastructure.container'
                      AND s.change_kind = 'SOFT_DELETE'
                      AND s.before_row_version = 1
                      AND s.after_row_version = 2;
                    """,
                    cancellationToken,
                    ("command_id", softDeleteCommandId)));

            var replay = await ExecuteSoftDeleteAsync(execution, cancellationToken);
            Assert.True(replay.IsSuccess);
            Assert.Equal(softDelete.Value, replay.Value);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", softDeleteCommandId)));

            var changedHash = await ExecuteSoftDeleteAsync(
                execution with { RequestHash = Hash(2) },
                cancellationToken);
            Assert.True(changedHash.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, changedHash.Error.Kind);
            Assert.Equal(ContainerLifecycleErrorCodes.IdempotencyKeyReused, changedHash.Error.Code);

            var restore = await ExecuteRestoreAsync(
                new RestoreContainerExecution(
                    CommandId.From(restoreCommandId),
                    Hash(3),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new RestoreContainerCommand(scenario.ContainerId, 2)),
                cancellationToken);

            Assert.True(restore.IsSuccess);
            Assert.Equal(new ContainerLifecycleResult(scenario.ContainerId, 3, false), restore.Value);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM infrastructure.containers
                    WHERE id = @container_id
                      AND active = true
                      AND deleted_at IS NULL
                      AND deleted_by_account_id IS NULL
                      AND row_version = 3;
                    """,
                    cancellationToken,
                    ("container_id", scenario.ContainerId)));
            Assert.Equal(
                2L,
                await ScalarAsync<long>(
                    """
                    SELECT
                        (SELECT count(*) FROM processing_config.route_input_configs
                         WHERE processing_route_version_id = @route_version_id
                           AND container_id = @container_id)
                      + (SELECT count(*) FROM processing_config.process_materials
                         WHERE id = @material_id
                           AND container_id = @container_id
                           AND row_version = 1);
                    """,
                    cancellationToken,
                    ("route_version_id", scenario.RouteVersionId),
                    ("container_id", scenario.ContainerId),
                    ("material_id", scenario.MaterialId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.audit_event_subjects s
                    JOIN audit.audit_events e ON e.id = s.audit_event_id
                    WHERE e.command_id = @command_id
                      AND e.command_type = 'RestoreContainer'
                      AND e.event_kind = 'DATA_LIFECYCLE'
                      AND s.subject_kind = 'infrastructure.container'
                      AND s.change_kind = 'RESTORE'
                      AND s.before_row_version = 2
                      AND s.after_row_version = 3;
                    """,
                    cancellationToken,
                    ("command_id", restoreCommandId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.outbox_messages WHERE command_id = ANY(@command_ids);",
                    cancellationToken,
                    ("command_ids", new[] { softDeleteCommandId, restoreCommandId })));
        }
        finally
        {
            await CleanupConfigurationScenarioAsync(
                scenario,
                [softDeleteCommandId, restoreCommandId],
                cancellationToken);
        }
    }

    [Fact]
    public async Task Restore_preserves_preexisting_inactive_container_and_configuration_references()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedConfigurationScenarioAsync(containerActive: false, cancellationToken);
        var softDeleteCommandId = Guid.CreateVersion7();
        var restoreCommandId = Guid.CreateVersion7();

        try
        {
            var softDelete = await ExecuteSoftDeleteAsync(
                new SoftDeleteContainerExecution(
                    CommandId.From(softDeleteCommandId),
                    Hash(4),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new SoftDeleteContainerCommand(scenario.ContainerId, 1)),
                cancellationToken);
            Assert.True(softDelete.IsSuccess);

            var restore = await ExecuteRestoreAsync(
                new RestoreContainerExecution(
                    CommandId.From(restoreCommandId),
                    Hash(5),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new RestoreContainerCommand(scenario.ContainerId, 2)),
                cancellationToken);
            Assert.True(restore.IsSuccess);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM infrastructure.containers
                    WHERE id = @container_id
                      AND active = false
                      AND deleted_at IS NULL
                      AND deleted_by_account_id IS NULL
                      AND row_version = 3;
                    """,
                    cancellationToken,
                    ("container_id", scenario.ContainerId)));
            Assert.Equal(
                2L,
                await ScalarAsync<long>(
                    """
                    SELECT
                        (SELECT count(*) FROM processing_config.route_input_configs
                         WHERE processing_route_version_id = @route_version_id
                           AND container_id = @container_id)
                      + (SELECT count(*) FROM processing_config.process_materials
                         WHERE id = @material_id
                           AND container_id = @container_id
                           AND active = true
                           AND deleted_at IS NULL
                           AND row_version = 1);
                    """,
                    cancellationToken,
                    ("route_version_id", scenario.RouteVersionId),
                    ("container_id", scenario.ContainerId),
                    ("material_id", scenario.MaterialId)));
        }
        finally
        {
            await CleanupConfigurationScenarioAsync(
                scenario,
                [softDeleteCommandId, restoreCommandId],
                cancellationToken);
        }
    }

    [Fact]
    public async Task Lifecycle_state_and_stale_conflicts_roll_back_command_acquisition_and_audit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedConfigurationScenarioAsync(containerActive: true, cancellationToken);
        var staleCommandId = Guid.CreateVersion7();
        var restoreCurrentCommandId = Guid.CreateVersion7();

        try
        {
            var stale = await ExecuteSoftDeleteAsync(
                new SoftDeleteContainerExecution(
                    CommandId.From(staleCommandId),
                    Hash(6),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new SoftDeleteContainerCommand(scenario.ContainerId, 2)),
                cancellationToken);
            Assert.True(stale.IsFailure);
            Assert.Equal(ContainerLifecycleErrorCodes.StaleRowVersion, stale.Error.Code);

            var restoreCurrent = await ExecuteRestoreAsync(
                new RestoreContainerExecution(
                    CommandId.From(restoreCurrentCommandId),
                    Hash(7),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new RestoreContainerCommand(scenario.ContainerId, 1)),
                cancellationToken);
            Assert.True(restoreCurrent.IsFailure);
            Assert.Equal(ContainerLifecycleErrorCodes.NotDeleted, restoreCurrent.Error.Code);

            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = ANY(@command_ids);",
                    cancellationToken,
                    ("command_ids", new[] { staleCommandId, restoreCurrentCommandId })));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = ANY(@command_ids);",
                    cancellationToken,
                    ("command_ids", new[] { staleCommandId, restoreCurrentCommandId })));
        }
        finally
        {
            await CleanupConfigurationScenarioAsync(
                scenario,
                [staleCommandId, restoreCurrentCommandId],
                cancellationToken);
        }
    }

    [Fact]
    public async Task Soft_delete_preserves_historical_tare_snapshot_and_existing_processing_guard_rejects_new_use()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedProcessingScenarioAsync(cancellationToken);
        var firstProcessingCommandId = Guid.CreateVersion7();
        var softDeleteCommandId = Guid.CreateVersion7();
        var rejectedProcessingCommandId = Guid.CreateVersion7();

        try
        {
            var firstCommand = CreateProcessingCommand(scenario, outputScale: 5m);
            var first = await ExecuteProcessingAsync(
                new ConfirmProcessingExecutionExecution(
                    CommandId.From(firstProcessingCommandId),
                    Hash(8),
                    ActorAccountId.From(scenario.ActorAccountId),
                    firstCommand),
                cancellationToken);
            Assert.True(first.IsSuccess);

            Assert.Equal(
                2m,
                await ScalarAsync<decimal>(
                    """
                    SELECT tare_weight_snapshot
                    FROM processing.processing_execution_inputs
                    WHERE processing_execution_id = @execution_id
                      AND actual_container_count = 1
                      AND consumption_basis = 'SCALE_NET';
                    """,
                    cancellationToken,
                    ("execution_id", first.Value.ProcessingExecutionId)));
            Assert.Equal(
                8m,
                await ScalarAsync<decimal>(
                    "SELECT consumed_quantity FROM processing.processing_execution_inputs WHERE processing_execution_id = @execution_id;",
                    cancellationToken,
                    ("execution_id", first.Value.ProcessingExecutionId)));

            var softDelete = await ExecuteSoftDeleteAsync(
                new SoftDeleteContainerExecution(
                    CommandId.From(softDeleteCommandId),
                    Hash(9),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new SoftDeleteContainerCommand(scenario.ContainerId, 1)),
                cancellationToken);
            Assert.True(softDelete.IsSuccess);

            Assert.Equal(
                2m,
                await ScalarAsync<decimal>(
                    "SELECT tare_weight_snapshot FROM processing.processing_execution_inputs WHERE processing_execution_id = @execution_id;",
                    cancellationToken,
                    ("execution_id", first.Value.ProcessingExecutionId)));
            Assert.Equal(
                2L,
                await ScalarAsync<long>(
                    """
                    SELECT
                        (SELECT count(*) FROM processing_config.route_input_configs
                         WHERE processing_route_version_id = @route_version_id
                           AND container_id = @container_id)
                      + (SELECT count(*) FROM processing_config.process_materials
                         WHERE id = @configured_material_id
                           AND container_id = @container_id
                           AND row_version = 1);
                    """,
                    cancellationToken,
                    ("route_version_id", scenario.RouteVersionId),
                    ("container_id", scenario.ContainerId),
                    ("configured_material_id", scenario.ConfiguredContainerMaterialId)));

            var rejected = await ExecuteProcessingAsync(
                new ConfirmProcessingExecutionExecution(
                    CommandId.From(rejectedProcessingCommandId),
                    Hash(10),
                    ActorAccountId.From(scenario.ActorAccountId),
                    CreateProcessingCommand(scenario, outputScale: 4m)),
                cancellationToken);

            Assert.True(rejected.IsFailure);
            Assert.Equal(ApplicationErrorKind.Validation, rejected.Error.Kind);
            Assert.Equal(ProcessingApplicationErrorCodes.OutputMeasurementInvalid, rejected.Error.Code);
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", rejectedProcessingCommandId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", rejectedProcessingCommandId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.outbox_messages WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", rejectedProcessingCommandId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM processing.processing_executions WHERE procurement_batch_id = @batch_id;",
                    cancellationToken,
                    ("batch_id", scenario.ProcurementBatchId)));
        }
        finally
        {
            await CleanupProcessingScenarioAsync(
                scenario,
                [firstProcessingCommandId, softDeleteCommandId, rejectedProcessingCommandId],
                cancellationToken);
        }
    }

    private static ConfirmProcessingExecutionCommand CreateProcessingCommand(
        ProcessingScenario scenario,
        decimal outputScale) =>
        new(
            scenario.WorkDate,
            scenario.EmployeeId,
            scenario.ProcurementBatchId,
            scenario.ProcessingModuleId,
            new ProcessingSourceSelection(ProcessingSourceKind.SUPPLIER, scenario.SupplierId),
            new ProcessingScaleMeasurement(10m, 1),
            null,
            new[]
            {
                new ProcessingOutputMeasurement(
                    scenario.ProcessingModuleOutputId,
                    outputScale,
                    null,
                    null,
                    null),
            });

    private static async ValueTask<ApplicationResult<ContainerLifecycleResult>> ExecuteSoftDeleteAsync(
        SoftDeleteContainerExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IContainerLifecycleExecutor>()
            .SoftDeleteAsync(execution, cancellationToken);
    }

    private static async ValueTask<ApplicationResult<ContainerLifecycleResult>> ExecuteRestoreAsync(
        RestoreContainerExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IContainerLifecycleExecutor>()
            .RestoreAsync(execution, cancellationToken);
    }

    private static async ValueTask<ApplicationResult<ConfirmProcessingExecutionResult>> ExecuteProcessingAsync(
        ConfirmProcessingExecutionExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IConfirmProcessingExecutionExecutor>()
            .ExecuteAsync(execution, cancellationToken);
    }

    private static CommandRequestHash Hash(byte value)
    {
        var bytes = new byte[CommandRequestHash.Sha256Length];
        Array.Fill(bytes, value);
        return CommandRequestHash.FromSha256(bytes);
    }

    private static async Task<ConfigurationScenario> SeedConfigurationScenarioAsync(
        bool containerActive,
        CancellationToken cancellationToken)
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var scenario = new ConfigurationScenario(
            ActorAccountId: Guid.CreateVersion7(),
            ProcurementProductId: Guid.CreateVersion7(),
            RouteId: Guid.CreateVersion7(),
            RouteVersionId: Guid.CreateVersion7(),
            ContainerId: Guid.CreateVersion7(),
            MaterialId: Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 V8 C15 container actor', true, NULL, NULL, @now);

            INSERT INTO infrastructure.containers
                (id, name_zh_tw, name_th_th, tare_weight, active, row_version, created_at, created_by_account_id)
            VALUES
                (@container_id, @container_name, NULL, 2.0, @container_active, 1, @now, @actor_id);

            INSERT INTO product.procurement_products
                (id, name_zh_tw, name_th_th, unit_code, default_storage_location_id,
                 active, row_version, created_at, created_by_account_id)
            VALUES
                (@procurement_product_id, @product_name, NULL, 'kg', NULL,
                 true, 1, @now, @actor_id);

            INSERT INTO processing_config.processing_routes
                (id, procurement_product_id, name_zh_tw, name_th_th,
                 active, row_version, created_at, created_by_account_id)
            VALUES
                (@route_id, @procurement_product_id, @route_name, NULL,
                 true, 1, @now, @actor_id);

            INSERT INTO processing_config.processing_route_versions
                (id, processing_route_id, version_number, status)
            VALUES
                (@route_version_id, @route_id, 1, 'ACTIVE');

            INSERT INTO processing_config.route_input_configs
                (processing_route_version_id, uses_container, container_id,
                 default_container_count, default_storage_location_id)
            VALUES
                (@route_version_id, true, @container_id, 1, NULL);

            INSERT INTO processing_config.process_materials
                (id, processing_route_version_id, name_zh_tw, name_th_th,
                 uses_container, container_id, default_container_count, default_storage_location_id,
                 active, row_version, created_at, created_by_account_id)
            VALUES
                (@material_id, @route_version_id, @material_name, NULL,
                 true, @container_id, 2, NULL,
                 true, 1, @now, @actor_id);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("container_id", scenario.ContainerId),
            ("container_name", $"P6 V8 C15 container {suffix}"),
            ("container_active", containerActive),
            ("procurement_product_id", scenario.ProcurementProductId),
            ("product_name", $"P6 V8 C15 product {suffix}"),
            ("route_id", scenario.RouteId),
            ("route_name", $"P6 V8 C15 route {suffix}"),
            ("route_version_id", scenario.RouteVersionId),
            ("material_id", scenario.MaterialId),
            ("material_name", $"P6 V8 C15 material {suffix}"),
            ("now", now));

        return scenario;
    }

    private static Task CleanupConfigurationScenarioAsync(
        ConfigurationScenario scenario,
        IReadOnlyCollection<Guid> commandIds,
        CancellationToken cancellationToken) =>
        ExecuteNonQueryAsync(
            """
            DELETE FROM audit.audit_event_subjects
            WHERE audit_event_id IN (SELECT id FROM audit.audit_events WHERE command_id = ANY(@command_ids));
            DELETE FROM audit.audit_events WHERE command_id = ANY(@command_ids);
            DELETE FROM system.outbox_messages WHERE command_id = ANY(@command_ids);
            DELETE FROM system.command_executions WHERE command_id = ANY(@command_ids);
            DELETE FROM processing_config.process_materials WHERE id = @material_id;
            DELETE FROM processing_config.route_input_configs WHERE processing_route_version_id = @route_version_id;
            DELETE FROM processing_config.processing_route_versions WHERE id = @route_version_id;
            DELETE FROM processing_config.processing_routes WHERE id = @route_id;
            DELETE FROM product.procurement_products WHERE id = @procurement_product_id;
            DELETE FROM infrastructure.containers WHERE id = @container_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds.ToArray()),
            ("material_id", scenario.MaterialId),
            ("route_version_id", scenario.RouteVersionId),
            ("route_id", scenario.RouteId),
            ("procurement_product_id", scenario.ProcurementProductId),
            ("container_id", scenario.ContainerId),
            ("actor_id", scenario.ActorAccountId));

    private static async Task<ProcessingScenario> SeedProcessingScenarioAsync(CancellationToken cancellationToken)
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var scenario = new ProcessingScenario(
            ActorAccountId: Guid.CreateVersion7(),
            EmployeeId: Guid.CreateVersion7(),
            SupplierId: Guid.CreateVersion7(),
            WarehouseId: Guid.CreateVersion7(),
            StorageLocationId: Guid.CreateVersion7(),
            ContainerId: Guid.CreateVersion7(),
            ProcurementProductId: Guid.CreateVersion7(),
            RouteId: Guid.CreateVersion7(),
            RouteVersionId: Guid.CreateVersion7(),
            OutputMaterialId: Guid.CreateVersion7(),
            ConfiguredContainerMaterialId: Guid.CreateVersion7(),
            ProcessingModuleId: Guid.CreateVersion7(),
            ProcessingModuleOutputId: Guid.CreateVersion7(),
            ProcurementBatchId: Guid.CreateVersion7(),
            WorkDate: new DateOnly(2026, 9, 5));
        var now = DateTimeOffset.UtcNow;

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 V8 C15 processing actor', true, NULL, NULL, @now);

            INSERT INTO party.employees
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, row_version, created_at, created_by_account_id)
            VALUES
                (@employee_id, @employee_name, NULL, NULL, NULL, NULL, NULL,
                 true, 1, @now, @actor_id);

            INSERT INTO party.suppliers
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, row_version, created_at, created_by_account_id)
            VALUES
                (@supplier_id, @supplier_name, NULL, NULL, NULL, NULL, NULL,
                 true, 1, @now, @actor_id);

            INSERT INTO infrastructure.warehouses
                (id, code, name_zh_tw, name_th_th, active, row_version, created_at, created_by_account_id)
            VALUES
                (@warehouse_id, NULL, @warehouse_name, NULL, true, 1, @now, @actor_id);

            INSERT INTO infrastructure.storage_locations
                (id, warehouse_id, code, name_zh_tw, name_th_th,
                 active, row_version, created_at, created_by_account_id)
            VALUES
                (@location_id, @warehouse_id, NULL, @location_name, NULL,
                 true, 1, @now, @actor_id);

            INSERT INTO infrastructure.containers
                (id, name_zh_tw, name_th_th, tare_weight,
                 active, row_version, created_at, created_by_account_id)
            VALUES
                (@container_id, @container_name, NULL, 2.0,
                 true, 1, @now, @actor_id);

            INSERT INTO product.procurement_products
                (id, name_zh_tw, name_th_th, unit_code, default_storage_location_id,
                 active, row_version, created_at, created_by_account_id)
            VALUES
                (@procurement_product_id, @product_name, NULL, 'kg', @location_id,
                 true, 1, @now, @actor_id);

            INSERT INTO processing_config.processing_routes
                (id, procurement_product_id, name_zh_tw, name_th_th,
                 active, row_version, created_at, created_by_account_id)
            VALUES
                (@route_id, @procurement_product_id, @route_name, NULL,
                 true, 1, @now, @actor_id);

            INSERT INTO processing_config.processing_route_versions
                (id, processing_route_id, version_number, status)
            VALUES
                (@route_version_id, @route_id, 1, 'ACTIVE');

            INSERT INTO processing_config.route_input_configs
                (processing_route_version_id, uses_container, container_id,
                 default_container_count, default_storage_location_id)
            VALUES
                (@route_version_id, true, @container_id, 1, @location_id);

            INSERT INTO processing_config.process_materials
                (id, processing_route_version_id, name_zh_tw, name_th_th,
                 uses_container, container_id, default_container_count, default_storage_location_id,
                 active, row_version, created_at, created_by_account_id)
            VALUES
                (@output_material_id, @route_version_id, @output_material_name, NULL,
                 false, NULL, NULL, @location_id,
                 true, 1, @now, @actor_id),
                (@configured_material_id, @route_version_id, @configured_material_name, NULL,
                 true, @container_id, 2, @location_id,
                 true, 1, @now, @actor_id);

            INSERT INTO processing_config.processing_modules
                (id, processing_route_version_id, name_zh_tw, name_th_th,
                 execution_mode, input_process_material_id, negative_inventory_policy)
            VALUES
                (@module_id, @route_version_id, @module_name, NULL,
                 'SOURCE_TRACKED', NULL, 'CONFIGURED');

            INSERT INTO processing_config.processing_module_outputs
                (id, processing_module_id, processing_route_version_id, output_sequence,
                 output_kind, process_material_id, sales_product_id, default_wage_rate)
            VALUES
                (@output_id, @module_id, @route_version_id, 1,
                 'PROCESS_MATERIAL', @output_material_id, NULL, 1.0);

            INSERT INTO procurement.procurement_batches
                (id, procurement_date, procurement_product_id, procurement_status,
                 lifecycle_status, processing_route_id, processing_route_version_id,
                 created_at, created_by_account_id, row_version)
            VALUES
                (@batch_id, @work_date, @procurement_product_id, 'OPEN',
                 'ACTIVE', @route_id, @route_version_id,
                 @now, @actor_id, 1);

            INSERT INTO inventory.inventory_positions
                (id, origin, procurement_batch_id, outsourced_supply_batch_id,
                 inventory_object_kind, procurement_product_id, process_material_id, sales_product_id,
                 storage_location_id, raw_source_kind, supplier_id, balance_quantity, row_version)
            VALUES
                (@position_id, 'IN_HOUSE', @batch_id, NULL,
                 'PROCUREMENT_PRODUCT', @procurement_product_id, NULL, NULL,
                 @location_id, 'SUPPLIER', @supplier_id, 100.0, 1);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("employee_id", scenario.EmployeeId),
            ("employee_name", $"P6 V8 C15 employee {suffix}"),
            ("supplier_id", scenario.SupplierId),
            ("supplier_name", $"P6 V8 C15 supplier {suffix}"),
            ("warehouse_id", scenario.WarehouseId),
            ("warehouse_name", $"P6 V8 C15 warehouse {suffix}"),
            ("location_id", scenario.StorageLocationId),
            ("location_name", $"P6 V8 C15 location {suffix}"),
            ("container_id", scenario.ContainerId),
            ("container_name", $"P6 V8 C15 container {suffix}"),
            ("procurement_product_id", scenario.ProcurementProductId),
            ("product_name", $"P6 V8 C15 product {suffix}"),
            ("route_id", scenario.RouteId),
            ("route_name", $"P6 V8 C15 route {suffix}"),
            ("route_version_id", scenario.RouteVersionId),
            ("output_material_id", scenario.OutputMaterialId),
            ("output_material_name", $"P6 V8 C15 output material {suffix}"),
            ("configured_material_id", scenario.ConfiguredContainerMaterialId),
            ("configured_material_name", $"P6 V8 C15 configured material {suffix}"),
            ("module_id", scenario.ProcessingModuleId),
            ("module_name", $"P6 V8 C15 module {suffix}"),
            ("output_id", scenario.ProcessingModuleOutputId),
            ("batch_id", scenario.ProcurementBatchId),
            ("work_date", scenario.WorkDate),
            ("position_id", Guid.CreateVersion7()),
            ("now", now));

        return scenario;
    }

    private static Task CleanupProcessingScenarioAsync(
        ProcessingScenario scenario,
        IReadOnlyCollection<Guid> commandIds,
        CancellationToken cancellationToken) =>
        ExecuteNonQueryAsync(
            """
            DELETE FROM audit.audit_event_subjects
            WHERE audit_event_id IN (SELECT id FROM audit.audit_events WHERE command_id = ANY(@command_ids));
            DELETE FROM audit.audit_events WHERE command_id = ANY(@command_ids);
            DELETE FROM system.outbox_messages WHERE command_id = ANY(@command_ids);
            DELETE FROM system.command_executions WHERE command_id = ANY(@command_ids);

            DELETE FROM inventory.inventory_movements WHERE procurement_batch_id = @batch_id;
            DELETE FROM inventory.inventory_operations
            WHERE processing_execution_id IN (
                SELECT id FROM processing.processing_executions WHERE procurement_batch_id = @batch_id);
            DELETE FROM inventory.inventory_positions WHERE procurement_batch_id = @batch_id;

            DELETE FROM processing.processing_execution_outputs
            WHERE processing_execution_id IN (
                SELECT id FROM processing.processing_executions WHERE procurement_batch_id = @batch_id);
            DELETE FROM processing.processing_execution_inputs
            WHERE processing_execution_id IN (
                SELECT id FROM processing.processing_executions WHERE procurement_batch_id = @batch_id);
            DELETE FROM processing.processing_executions WHERE procurement_batch_id = @batch_id;

            DELETE FROM procurement.procurement_batches WHERE id = @batch_id;
            DELETE FROM processing_config.processing_module_outputs WHERE processing_route_version_id = @route_version_id;
            DELETE FROM processing_config.processing_modules WHERE processing_route_version_id = @route_version_id;
            DELETE FROM processing_config.process_materials WHERE processing_route_version_id = @route_version_id;
            DELETE FROM processing_config.route_input_configs WHERE processing_route_version_id = @route_version_id;
            DELETE FROM processing_config.processing_route_versions WHERE id = @route_version_id;
            DELETE FROM processing_config.processing_routes WHERE id = @route_id;
            DELETE FROM product.procurement_products WHERE id = @procurement_product_id;
            DELETE FROM infrastructure.containers WHERE id = @container_id;
            DELETE FROM party.suppliers WHERE id = @supplier_id;
            DELETE FROM party.employees WHERE id = @employee_id;
            DELETE FROM infrastructure.storage_locations WHERE id = @location_id;
            DELETE FROM infrastructure.warehouses WHERE id = @warehouse_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds.ToArray()),
            ("batch_id", scenario.ProcurementBatchId),
            ("route_version_id", scenario.RouteVersionId),
            ("route_id", scenario.RouteId),
            ("procurement_product_id", scenario.ProcurementProductId),
            ("container_id", scenario.ContainerId),
            ("supplier_id", scenario.SupplierId),
            ("employee_id", scenario.EmployeeId),
            ("location_id", scenario.StorageLocationId),
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
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);

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
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);

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

    private sealed record ConfigurationScenario(
        Guid ActorAccountId,
        Guid ProcurementProductId,
        Guid RouteId,
        Guid RouteVersionId,
        Guid ContainerId,
        Guid MaterialId);

    private sealed record ProcessingScenario(
        Guid ActorAccountId,
        Guid EmployeeId,
        Guid SupplierId,
        Guid WarehouseId,
        Guid StorageLocationId,
        Guid ContainerId,
        Guid ProcurementProductId,
        Guid RouteId,
        Guid RouteVersionId,
        Guid OutputMaterialId,
        Guid ConfiguredContainerMaterialId,
        Guid ProcessingModuleId,
        Guid ProcessingModuleOutputId,
        Guid ProcurementBatchId,
        DateOnly WorkDate);
}
