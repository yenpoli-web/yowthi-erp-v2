using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Labor;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class ConfirmEmployeeDailyWageIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Aggregates_processing_before_multiply_applies_override_includes_packaging_and_commits_payable_replay_audit_outbox()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            var command = new ConfirmEmployeeDailyWageCommand(
                scenario.WorkDate,
                scenario.EmployeeId,
                new[]
                {
                    new ProcessingWageRateOverride(
                        scenario.ModuleOutputBId,
                        3m,
                        4.5m),
                });
            var execution = Execution(commandId, scenario.ActorAccountId, Hash(1), command);

            var result = await ExecuteAsync(execution, cancellationToken);
            Assert.True(result.IsSuccess);
            Assert.Equal(20L, result.Value.ProcessingWageTotalThb);
            Assert.Equal(150L, result.Value.SalesPackagingWageTotalThb);
            Assert.Equal(170L, result.Value.TotalWageThb);
            Assert.Equal(1L, result.Value.RowVersion);

            var replay = await ExecuteAsync(execution, cancellationToken);
            Assert.True(replay.IsSuccess);
            Assert.Equal(result.Value, replay.Value);

            var changedHash = await ExecuteAsync(
                Execution(commandId, scenario.ActorAccountId, Hash(2), command),
                cancellationToken);
            Assert.True(changedHash.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, changedHash.Error.Kind);
            Assert.Equal(LaborApplicationErrorCodes.IdempotencyKeyReused, changedHash.Error.Code);

            Assert.Equal(
                4.5m,
                await ScalarAsync<decimal>(
                    "SELECT aggregated_quantity FROM labor.processing_wage_components WHERE employee_daily_wage_id = @wage_id AND processing_module_output_id = @output_id;",
                    cancellationToken,
                    ("wage_id", result.Value.EmployeeDailyWageId),
                    ("output_id", scenario.ModuleOutputAId)));
            Assert.Equal(
                11L,
                await ScalarAsync<long>(
                    "SELECT amount_thb FROM labor.processing_wage_components WHERE employee_daily_wage_id = @wage_id AND processing_module_output_id = @output_id;",
                    cancellationToken,
                    ("wage_id", result.Value.EmployeeDailyWageId),
                    ("output_id", scenario.ModuleOutputAId)));
            Assert.False(
                await ScalarAsync<bool>(
                    "SELECT rate_overridden FROM labor.processing_wage_components WHERE employee_daily_wage_id = @wage_id AND processing_module_output_id = @output_id;",
                    cancellationToken,
                    ("wage_id", result.Value.EmployeeDailyWageId),
                    ("output_id", scenario.ModuleOutputAId)));

            Assert.Equal(
                2m,
                await ScalarAsync<decimal>(
                    "SELECT aggregated_quantity FROM labor.processing_wage_components WHERE employee_daily_wage_id = @wage_id AND processing_module_output_id = @output_id;",
                    cancellationToken,
                    ("wage_id", result.Value.EmployeeDailyWageId),
                    ("output_id", scenario.ModuleOutputBId)));
            Assert.Equal(
                4.5m,
                await ScalarAsync<decimal>(
                    "SELECT applied_wage_rate FROM labor.processing_wage_components WHERE employee_daily_wage_id = @wage_id AND processing_module_output_id = @output_id;",
                    cancellationToken,
                    ("wage_id", result.Value.EmployeeDailyWageId),
                    ("output_id", scenario.ModuleOutputBId)));
            Assert.True(
                await ScalarAsync<bool>(
                    "SELECT rate_overridden FROM labor.processing_wage_components WHERE employee_daily_wage_id = @wage_id AND processing_module_output_id = @output_id;",
                    cancellationToken,
                    ("wage_id", result.Value.EmployeeDailyWageId),
                    ("output_id", scenario.ModuleOutputBId)));
            Assert.Equal(
                9L,
                await ScalarAsync<long>(
                    "SELECT amount_thb FROM labor.processing_wage_components WHERE employee_daily_wage_id = @wage_id AND processing_module_output_id = @output_id;",
                    cancellationToken,
                    ("wage_id", result.Value.EmployeeDailyWageId),
                    ("output_id", scenario.ModuleOutputBId)));

            Assert.Equal(
                3L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM labor.processing_wage_component_sources s JOIN labor.processing_wage_components c ON c.id = s.processing_wage_component_id WHERE c.employee_daily_wage_id = @wage_id;",
                    cancellationToken,
                    ("wage_id", result.Value.EmployeeDailyWageId)));
            Assert.Equal(
                2L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM labor.sales_packaging_wage_components WHERE employee_daily_wage_id = @wage_id;",
                    cancellationToken,
                    ("wage_id", result.Value.EmployeeDailyWageId)));

            Assert.Equal(
                "EMPLOYEE_DAILY_WAGE",
                await ScalarAsync<string>(
                    "SELECT payable_kind FROM finance.payables WHERE id = @payable_id;",
                    cancellationToken,
                    ("payable_id", result.Value.PayableId)));
            Assert.Equal(
                170L,
                await ScalarAsync<long>(
                    "SELECT amount_thb FROM finance.payable_obligation_items WHERE payable_id = @payable_id;",
                    cancellationToken,
                    ("payable_id", result.Value.PayableId)));
            Assert.Equal(
                170L,
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
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = @command_id AND command_type = 'ConfirmEmployeeDailyWage';",
                    cancellationToken,
                    ("command_id", commandId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.outbox_messages WHERE command_id = @command_id AND message_type = 'labor.employee-daily-wage.confirmed';",
                    cancellationToken,
                    ("command_id", commandId)));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Concurrent_same_work_date_employee_allows_one_daily_wage_and_rolls_back_loser()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(cancellationToken);
        var firstCommandId = Guid.CreateVersion7();
        var secondCommandId = Guid.CreateVersion7();

        try
        {
            var command = new ConfirmEmployeeDailyWageCommand(
                scenario.WorkDate,
                scenario.EmployeeId,
                Array.Empty<ProcessingWageRateOverride>());

            var results = await Task.WhenAll(
                ExecuteAsync(Execution(firstCommandId, scenario.ActorAccountId, Hash(3), command), cancellationToken).AsTask(),
                ExecuteAsync(Execution(secondCommandId, scenario.ActorAccountId, Hash(4), command), cancellationToken).AsTask());

            Assert.Single(results, result => result.IsSuccess);
            var losingResult = Assert.Single(results, result => result.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, losingResult.Error.Kind);
            Assert.Equal(LaborApplicationErrorCodes.DailyWageAlreadyConfirmed, losingResult.Error.Code);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM labor.employee_daily_wages WHERE work_date = @work_date AND employee_id = @employee_id;",
                    cancellationToken,
                    ("work_date", scenario.WorkDate),
                    ("employee_id", scenario.EmployeeId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM finance.payables p JOIN labor.employee_daily_wages w ON w.id = p.employee_daily_wage_id WHERE w.work_date = @work_date AND w.employee_id = @employee_id AND p.payable_kind = 'EMPLOYEE_DAILY_WAGE';",
                    cancellationToken,
                    ("work_date", scenario.WorkDate),
                    ("employee_id", scenario.EmployeeId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = ANY(@command_ids) AND status = 'SUCCEEDED';",
                    cancellationToken,
                    ("command_ids", new[] { firstCommandId, secondCommandId })));
        }
        finally
        {
            await CleanupScenarioAsync(
                scenario,
                new[] { firstCommandId, secondCommandId },
                cancellationToken);
        }
    }

    private static ConfirmEmployeeDailyWageExecution Execution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        ConfirmEmployeeDailyWageCommand command) =>
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

    private static async ValueTask<ApplicationResult<ConfirmEmployeeDailyWageResult>> ExecuteAsync(
        ConfirmEmployeeDailyWageExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<IConfirmEmployeeDailyWageExecutor>();
        return await executor.ExecuteAsync(execution, cancellationToken);
    }

    private static async Task<Scenario> SeedScenarioAsync(CancellationToken cancellationToken)
    {
        var scenario = new Scenario(
            ActorAccountId: Guid.CreateVersion7(),
            EmployeeId: Guid.CreateVersion7(),
            CustomerId: Guid.CreateVersion7(),
            ProcurementProductId: Guid.CreateVersion7(),
            ProcessingRouteId: Guid.CreateVersion7(),
            ProcessingRouteVersionId: Guid.CreateVersion7(),
            ProcessingModuleId: Guid.CreateVersion7(),
            ProcessMaterialAId: Guid.CreateVersion7(),
            ProcessMaterialBId: Guid.CreateVersion7(),
            ModuleOutputAId: Guid.CreateVersion7(),
            ModuleOutputBId: Guid.CreateVersion7(),
            ProcurementBatchId: Guid.CreateVersion7(),
            ProcessingExecution1Id: Guid.CreateVersion7(),
            ProcessingExecution2Id: Guid.CreateVersion7(),
            ProcessingOutputA1Id: Guid.CreateVersion7(),
            ProcessingOutputA2Id: Guid.CreateVersion7(),
            ProcessingOutputB1Id: Guid.CreateVersion7(),
            SalesId: Guid.CreateVersion7(),
            PackagingItemId: Guid.CreateVersion7(),
            PackagingWorkRecord1Id: Guid.CreateVersion7(),
            PackagingWorkRecord2Id: Guid.CreateVersion7(),
            WorkDate: new DateOnly(2026, 9, 3));
        var now = DateTimeOffset.UtcNow;

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 V5 actor', true, NULL, NULL, @now);

            INSERT INTO party.employees
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@employee_id, 'P6 V5 employee', NULL, NULL, NULL, NULL, NULL,
                 true, 1, @now, @actor_id, NULL, NULL);

            INSERT INTO party.customers
                (id, name_zh_tw, name_th_th, phone, active, row_version,
                 created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@customer_id, 'P6 V5 customer', NULL, NULL, true, 1,
                 @now, @actor_id, NULL, NULL);

            INSERT INTO product.procurement_products
                (id, name_zh_tw, name_th_th, unit_code, default_storage_location_id,
                 active, row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@procurement_product_id, 'P6 V5 procurement product', NULL, 'kg', NULL,
                 true, 1, @now, @actor_id, NULL, NULL);

            INSERT INTO processing_config.processing_routes
                (id, procurement_product_id, name_zh_tw, name_th_th, active, row_version,
                 created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@route_id, @procurement_product_id, 'P6 V5 route', NULL, true, 1,
                 @now, @actor_id, NULL, NULL);

            INSERT INTO processing_config.processing_route_versions
                (id, processing_route_id, version_number, status)
            VALUES
                (@route_version_id, @route_id, 1, 'ACTIVE');

            INSERT INTO processing_config.process_materials
                (id, processing_route_version_id, name_zh_tw, name_th_th,
                 uses_container, container_id, default_container_count, default_storage_location_id,
                 active, row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@material_a_id, @route_version_id, 'P6 V5 material A', NULL,
                 false, NULL, NULL, NULL, true, 1, @now, @actor_id, NULL, NULL),
                (@material_b_id, @route_version_id, 'P6 V5 material B', NULL,
                 false, NULL, NULL, NULL, true, 1, @now, @actor_id, NULL, NULL);

            INSERT INTO processing_config.processing_modules
                (id, processing_route_version_id, name_zh_tw, name_th_th,
                 execution_mode, input_process_material_id, negative_inventory_policy)
            VALUES
                (@module_id, @route_version_id, 'P6 V5 module', NULL,
                 'POOLED_OUTPUT', NULL, 'DISALLOW');

            INSERT INTO processing_config.processing_module_outputs
                (id, processing_module_id, processing_route_version_id, output_sequence,
                 output_kind, process_material_id, sales_product_id, default_wage_rate)
            VALUES
                (@module_output_a_id, @module_id, @route_version_id, 1,
                 'PROCESS_MATERIAL', @material_a_id, NULL, 2.5),
                (@module_output_b_id, @module_id, @route_version_id, 2,
                 'PROCESS_MATERIAL', @material_b_id, NULL, 3);

            INSERT INTO procurement.procurement_batches
                (id, procurement_date, procurement_product_id, procurement_status, lifecycle_status,
                 processing_route_id, processing_route_version_id,
                 completed_at, completed_by_account_id, closed_at, closed_by_account_id,
                 row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@batch_id, @work_date, @procurement_product_id, 'OPEN', 'ACTIVE',
                 @route_id, @route_version_id,
                 NULL, NULL, NULL, NULL,
                 1, @now, @actor_id, NULL, NULL);

            INSERT INTO processing.processing_executions
                (id, work_date, employee_id, procurement_batch_id, processing_route_version_id,
                 processing_module_id, execution_mode_snapshot, negative_inventory_policy_snapshot,
                 processing_source_kind, supplier_id, recorded_at, recorded_by_account_id,
                 row_version, deleted_at, deleted_by_account_id)
            VALUES
                (@execution_1_id, @work_date, @employee_id, @batch_id, @route_version_id,
                 @module_id, 'POOLED_OUTPUT', 'DISALLOW', NULL, NULL, @now, @actor_id,
                 1, NULL, NULL),
                (@execution_2_id, @work_date, @employee_id, @batch_id, @route_version_id,
                 @module_id, 'POOLED_OUTPUT', 'DISALLOW', NULL, NULL, @now, @actor_id,
                 1, NULL, NULL);

            INSERT INTO processing.processing_execution_outputs
                (id, processing_execution_id, processing_module_output_id, output_kind_snapshot,
                 configured_wage_rate_snapshot, observed_scale_reading, actual_container_count,
                 tare_weight_snapshot, derived_net_quantity, completed_quantity,
                 packaging_weight_snapshot, source_consumption_quantity)
            VALUES
                (@output_a1_id, @execution_1_id, @module_output_a_id, 'PROCESS_MATERIAL',
                 2.5, 3.3, NULL, NULL, 3.3, NULL, NULL, NULL),
                (@output_b1_id, @execution_1_id, @module_output_b_id, 'PROCESS_MATERIAL',
                 3, 2.0, NULL, NULL, 2.0, NULL, NULL, NULL),
                (@output_a2_id, @execution_2_id, @module_output_a_id, 'PROCESS_MATERIAL',
                 2.5, 1.2, NULL, NULL, 1.2, NULL, NULL, NULL);

            INSERT INTO sales.sales
                (id, sales_date, customer_id, status, confirmed_at, confirmed_by_account_id,
                 row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@sales_id, @work_date, @customer_id, 'DRAFT', NULL, NULL,
                 1, @now, @actor_id, NULL, NULL);

            INSERT INTO sales_handling.sales_packaging_items
                (id, name_zh_tw, name_th_th, active, row_version,
                 created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@packaging_item_id, 'P6 V5 packaging', NULL, true, 1,
                 @now, @actor_id, NULL, NULL);

            INSERT INTO sales_handling.sales_packaging_work_records
                (id, sales_id, work_date, employee_id, sales_packaging_item_id,
                 confirmed_wage_thb, recorded_at, recorded_by_account_id,
                 row_version, deleted_at, deleted_by_account_id)
            VALUES
                (@work_record_1_id, @sales_id, @work_date, @employee_id, @packaging_item_id,
                 100, @now, @actor_id, 1, NULL, NULL),
                (@work_record_2_id, @sales_id, @work_date, @employee_id, @packaging_item_id,
                 50, @now, @actor_id, 1, NULL, NULL);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("employee_id", scenario.EmployeeId),
            ("customer_id", scenario.CustomerId),
            ("procurement_product_id", scenario.ProcurementProductId),
            ("route_id", scenario.ProcessingRouteId),
            ("route_version_id", scenario.ProcessingRouteVersionId),
            ("module_id", scenario.ProcessingModuleId),
            ("material_a_id", scenario.ProcessMaterialAId),
            ("material_b_id", scenario.ProcessMaterialBId),
            ("module_output_a_id", scenario.ModuleOutputAId),
            ("module_output_b_id", scenario.ModuleOutputBId),
            ("batch_id", scenario.ProcurementBatchId),
            ("execution_1_id", scenario.ProcessingExecution1Id),
            ("execution_2_id", scenario.ProcessingExecution2Id),
            ("output_a1_id", scenario.ProcessingOutputA1Id),
            ("output_a2_id", scenario.ProcessingOutputA2Id),
            ("output_b1_id", scenario.ProcessingOutputB1Id),
            ("sales_id", scenario.SalesId),
            ("packaging_item_id", scenario.PackagingItemId),
            ("work_record_1_id", scenario.PackagingWorkRecord1Id),
            ("work_record_2_id", scenario.PackagingWorkRecord2Id),
            ("work_date", scenario.WorkDate),
            ("now", now));

        return scenario;
    }

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

            DELETE FROM finance.payable_outstanding_positions
            WHERE payable_id IN (
                SELECT p.id FROM finance.payables p
                JOIN labor.employee_daily_wages w ON w.id = p.employee_daily_wage_id
                WHERE w.work_date = @work_date AND w.employee_id = @employee_id);
            DELETE FROM finance.payable_obligation_items
            WHERE employee_daily_wage_id IN (
                SELECT id FROM labor.employee_daily_wages
                WHERE work_date = @work_date AND employee_id = @employee_id);
            DELETE FROM finance.payables
            WHERE employee_daily_wage_id IN (
                SELECT id FROM labor.employee_daily_wages
                WHERE work_date = @work_date AND employee_id = @employee_id);

            DELETE FROM labor.processing_wage_component_sources
            WHERE processing_wage_component_id IN (
                SELECT id FROM labor.processing_wage_components
                WHERE employee_daily_wage_id IN (
                    SELECT id FROM labor.employee_daily_wages
                    WHERE work_date = @work_date AND employee_id = @employee_id));
            DELETE FROM labor.sales_packaging_wage_components
            WHERE employee_daily_wage_id IN (
                SELECT id FROM labor.employee_daily_wages
                WHERE work_date = @work_date AND employee_id = @employee_id);
            DELETE FROM labor.processing_wage_components
            WHERE employee_daily_wage_id IN (
                SELECT id FROM labor.employee_daily_wages
                WHERE work_date = @work_date AND employee_id = @employee_id);
            DELETE FROM labor.employee_daily_wages
            WHERE work_date = @work_date AND employee_id = @employee_id;

            DELETE FROM sales_handling.sales_packaging_work_records
            WHERE id = @work_record_1_id OR id = @work_record_2_id;
            DELETE FROM sales_handling.sales_packaging_items WHERE id = @packaging_item_id;
            DELETE FROM sales.sales WHERE id = @sales_id;
            DELETE FROM party.customers WHERE id = @customer_id;

            DELETE FROM processing.processing_execution_outputs
            WHERE id = @output_a1_id OR id = @output_a2_id OR id = @output_b1_id;
            DELETE FROM processing.processing_executions
            WHERE id = @execution_1_id OR id = @execution_2_id;
            DELETE FROM procurement.procurement_batches WHERE id = @batch_id;
            DELETE FROM processing_config.processing_module_outputs
            WHERE id = @module_output_a_id OR id = @module_output_b_id;
            DELETE FROM processing_config.processing_modules WHERE id = @module_id;
            DELETE FROM processing_config.process_materials
            WHERE id = @material_a_id OR id = @material_b_id;
            DELETE FROM processing_config.processing_route_versions WHERE id = @route_version_id;
            DELETE FROM processing_config.processing_routes WHERE id = @route_id;
            DELETE FROM product.procurement_products WHERE id = @procurement_product_id;
            DELETE FROM party.employees WHERE id = @employee_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds.ToArray()),
            ("work_date", scenario.WorkDate),
            ("employee_id", scenario.EmployeeId),
            ("work_record_1_id", scenario.PackagingWorkRecord1Id),
            ("work_record_2_id", scenario.PackagingWorkRecord2Id),
            ("packaging_item_id", scenario.PackagingItemId),
            ("sales_id", scenario.SalesId),
            ("customer_id", scenario.CustomerId),
            ("output_a1_id", scenario.ProcessingOutputA1Id),
            ("output_a2_id", scenario.ProcessingOutputA2Id),
            ("output_b1_id", scenario.ProcessingOutputB1Id),
            ("execution_1_id", scenario.ProcessingExecution1Id),
            ("execution_2_id", scenario.ProcessingExecution2Id),
            ("batch_id", scenario.ProcurementBatchId),
            ("module_output_a_id", scenario.ModuleOutputAId),
            ("module_output_b_id", scenario.ModuleOutputBId),
            ("module_id", scenario.ProcessingModuleId),
            ("material_a_id", scenario.ProcessMaterialAId),
            ("material_b_id", scenario.ProcessMaterialBId),
            ("route_version_id", scenario.ProcessingRouteVersionId),
            ("route_id", scenario.ProcessingRouteId),
            ("procurement_product_id", scenario.ProcurementProductId),
            ("actor_id", scenario.ActorAccountId));
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
        Guid EmployeeId,
        Guid CustomerId,
        Guid ProcurementProductId,
        Guid ProcessingRouteId,
        Guid ProcessingRouteVersionId,
        Guid ProcessingModuleId,
        Guid ProcessMaterialAId,
        Guid ProcessMaterialBId,
        Guid ModuleOutputAId,
        Guid ModuleOutputBId,
        Guid ProcurementBatchId,
        Guid ProcessingExecution1Id,
        Guid ProcessingExecution2Id,
        Guid ProcessingOutputA1Id,
        Guid ProcessingOutputA2Id,
        Guid ProcessingOutputB1Id,
        Guid SalesId,
        Guid PackagingItemId,
        Guid PackagingWorkRecord1Id,
        Guid PackagingWorkRecord2Id,
        DateOnly WorkDate);
}
