using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Labor;
using YowThi.Erp.Application.Processing;
using YowThi.Erp.Domain.Processing;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class ConfirmProcessingExecutionIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Source_tracked_execution_replay_inventory_audit_and_outbox_are_atomic()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            await InsertProcurementProductPositionAsync(
                scenario,
                scenario.PrimaryLocationId,
                100m,
                ProcessingSourceKind.SUPPLIER,
                scenario.SupplierId,
                cancellationToken);

            var command = new ConfirmProcessingExecutionCommand(
                scenario.WorkDate,
                scenario.EmployeeId,
                scenario.ProcurementBatchId,
                scenario.SourceTrackedModuleId,
                new ProcessingSourceSelection(ProcessingSourceKind.SUPPLIER, scenario.SupplierId),
                new ProcessingScaleMeasurement(10m, null),
                null,
                new[]
                {
                    new ProcessingOutputMeasurement(
                        scenario.SourceTrackedOutputId,
                        8.5m,
                        null,
                        null,
                        null),
                });
            var execution = Execution(commandId, scenario.ActorAccountId, Hash(1), command);

            var result = await ExecuteAsync(execution, cancellationToken);
            Assert.True(result.IsSuccess);
            Assert.Equal(1L, result.Value.ProcessingExecutionRowVersion);

            var replay = await ExecuteAsync(execution, cancellationToken);
            Assert.True(replay.IsSuccess);
            Assert.Equal(result.Value, replay.Value);

            var changedHash = await ExecuteAsync(
                Execution(commandId, scenario.ActorAccountId, Hash(2), command),
                cancellationToken);
            Assert.True(changedHash.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, changedHash.Error.Kind);
            Assert.Equal(ProcessingApplicationErrorCodes.IdempotencyKeyReused, changedHash.Error.Code);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM processing.processing_executions
                    WHERE id = @execution_id
                      AND work_date = @work_date
                      AND employee_id = @employee_id
                      AND procurement_batch_id = @batch_id
                      AND processing_route_version_id = @route_version_id
                      AND processing_module_id = @module_id
                      AND execution_mode_snapshot = 'SOURCE_TRACKED'
                      AND processing_source_kind = 'SUPPLIER'
                      AND supplier_id = @supplier_id;
                    """,
                    cancellationToken,
                    ("execution_id", result.Value.ProcessingExecutionId),
                    ("work_date", scenario.WorkDate),
                    ("employee_id", scenario.EmployeeId),
                    ("batch_id", scenario.ProcurementBatchId),
                    ("route_version_id", scenario.RouteVersionId),
                    ("module_id", scenario.SourceTrackedModuleId),
                    ("supplier_id", scenario.SupplierId)));

            Assert.Equal(
                10m,
                await ScalarAsync<decimal>(
                    "SELECT consumed_quantity FROM processing.processing_execution_inputs WHERE processing_execution_id = @execution_id AND consumption_basis = 'SCALE_NET';",
                    cancellationToken,
                    ("execution_id", result.Value.ProcessingExecutionId)));
            Assert.Equal(
                8.5m,
                await ScalarAsync<decimal>(
                    "SELECT derived_net_quantity FROM processing.processing_execution_outputs WHERE processing_execution_id = @execution_id AND processing_module_output_id = @output_id;",
                    cancellationToken,
                    ("execution_id", result.Value.ProcessingExecutionId),
                    ("output_id", scenario.SourceTrackedOutputId)));

            Assert.Equal(
                90m,
                await ReadPositionBalanceAsync(
                    scenario,
                    "PROCUREMENT_PRODUCT",
                    scenario.ProcurementProductId,
                    scenario.PrimaryLocationId,
                    "SUPPLIER",
                    scenario.SupplierId,
                    cancellationToken));
            Assert.Equal(
                8.5m,
                await ReadPositionBalanceAsync(
                    scenario,
                    "PROCESS_MATERIAL",
                    scenario.MaterialAId,
                    scenario.PrimaryLocationId,
                    null,
                    null,
                    cancellationToken));

            Assert.Equal(
                2L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM inventory.inventory_movements WHERE inventory_operation_id = @operation_id;",
                    cancellationToken,
                    ("operation_id", result.Value.InventoryOperationId)));
            Assert.Equal(
                -10m,
                await ScalarAsync<decimal>(
                    "SELECT quantity_delta FROM inventory.inventory_movements WHERE inventory_operation_id = @operation_id AND movement_type = 'PROCESS_CONSUME';",
                    cancellationToken,
                    ("operation_id", result.Value.InventoryOperationId)));
            Assert.Equal(
                8.5m,
                await ScalarAsync<decimal>(
                    "SELECT quantity_delta FROM inventory.inventory_movements WHERE inventory_operation_id = @operation_id AND movement_type = 'PROCESS_PRODUCE';",
                    cancellationToken,
                    ("operation_id", result.Value.InventoryOperationId)));

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id AND status = 'SUCCEEDED';",
                    cancellationToken,
                    ("command_id", commandId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = @command_id AND command_type = 'ConfirmProcessingExecution';",
                    cancellationToken,
                    ("command_id", commandId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.outbox_messages WHERE command_id = @command_id AND message_type = 'processing.execution.confirmed';",
                    cancellationToken,
                    ("command_id", commandId)));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Pooled_output_consumes_the_sum_of_output_quantity_without_source_selection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            await InsertProcessMaterialPositionAsync(
                scenario,
                scenario.MaterialAId,
                scenario.PrimaryLocationId,
                20m,
                cancellationToken);

            var command = new ConfirmProcessingExecutionCommand(
                scenario.WorkDate,
                scenario.EmployeeId,
                scenario.ProcurementBatchId,
                scenario.PooledModuleId,
                null,
                null,
                null,
                new[]
                {
                    new ProcessingOutputMeasurement(
                        scenario.PooledOutputId,
                        7.5m,
                        null,
                        null,
                        null),
                });

            var result = await ExecuteAsync(
                Execution(commandId, scenario.ActorAccountId, Hash(3), command),
                cancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Equal(
                7.5m,
                await ScalarAsync<decimal>(
                    "SELECT consumed_quantity FROM processing.processing_execution_inputs WHERE processing_execution_id = @execution_id AND consumption_basis = 'OUTPUT_QUANTITY';",
                    cancellationToken,
                    ("execution_id", result.Value.ProcessingExecutionId)));
            Assert.Equal(
                12.5m,
                await ReadPositionBalanceAsync(
                    scenario,
                    "PROCESS_MATERIAL",
                    scenario.MaterialAId,
                    scenario.PrimaryLocationId,
                    null,
                    null,
                    cancellationToken));
            Assert.Equal(
                7.5m,
                await ReadPositionBalanceAsync(
                    scenario,
                    "PROCESS_MATERIAL",
                    scenario.MaterialBId,
                    scenario.PrimaryLocationId,
                    null,
                    null,
                    cancellationToken));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Final_packaging_consumes_completed_quantity_times_packaging_weight()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            await InsertProcessMaterialPositionAsync(
                scenario,
                scenario.MaterialBId,
                scenario.PrimaryLocationId,
                5m,
                cancellationToken);

            var command = new ConfirmProcessingExecutionCommand(
                scenario.WorkDate,
                scenario.EmployeeId,
                scenario.ProcurementBatchId,
                scenario.FinalPackagingModuleId,
                null,
                null,
                null,
                new[]
                {
                    new ProcessingOutputMeasurement(
                        scenario.FinalPackagingOutputId,
                        null,
                        null,
                        6m,
                        null),
                });

            var result = await ExecuteAsync(
                Execution(commandId, scenario.ActorAccountId, Hash(4), command),
                cancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Equal(
                3m,
                await ScalarAsync<decimal>(
                    "SELECT consumed_quantity FROM processing.processing_execution_inputs WHERE processing_execution_id = @execution_id AND consumption_basis = 'PACKAGING_WEIGHT';",
                    cancellationToken,
                    ("execution_id", result.Value.ProcessingExecutionId)));
            Assert.Equal(
                3m,
                await ScalarAsync<decimal>(
                    "SELECT source_consumption_quantity FROM processing.processing_execution_outputs WHERE processing_execution_id = @execution_id AND processing_module_output_id = @output_id;",
                    cancellationToken,
                    ("execution_id", result.Value.ProcessingExecutionId),
                    ("output_id", scenario.FinalPackagingOutputId)));
            Assert.Equal(
                2m,
                await ReadPositionBalanceAsync(
                    scenario,
                    "PROCESS_MATERIAL",
                    scenario.MaterialBId,
                    scenario.PrimaryLocationId,
                    null,
                    null,
                    cancellationToken));
            Assert.Equal(
                6m,
                await ReadPositionBalanceAsync(
                    scenario,
                    "SALES_PRODUCT",
                    scenario.SalesProductId,
                    scenario.PrimaryLocationId,
                    null,
                    null,
                    cancellationToken));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Ambiguous_input_location_requires_explicit_choice_and_rolls_back_command_acquisition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            await InsertProcessMaterialPositionAsync(
                scenario,
                scenario.MaterialAId,
                scenario.PrimaryLocationId,
                10m,
                cancellationToken);
            await InsertProcessMaterialPositionAsync(
                scenario,
                scenario.MaterialAId,
                scenario.SecondaryLocationId,
                10m,
                cancellationToken);

            var command = new ConfirmProcessingExecutionCommand(
                scenario.WorkDate,
                scenario.EmployeeId,
                scenario.ProcurementBatchId,
                scenario.PooledModuleId,
                null,
                null,
                null,
                new[]
                {
                    new ProcessingOutputMeasurement(
                        scenario.PooledOutputId,
                        5m,
                        null,
                        null,
                        null),
                });

            var result = await ExecuteAsync(
                Execution(commandId, scenario.ActorAccountId, Hash(5), command),
                cancellationToken);

            Assert.True(result.IsFailure);
            Assert.Equal(ApplicationErrorKind.Validation, result.Error.Kind);
            Assert.Equal(ProcessingApplicationErrorCodes.InputLocationRequired, result.Error.Code);
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", commandId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM processing.processing_executions WHERE procurement_batch_id = @batch_id;",
                    cancellationToken,
                    ("batch_id", scenario.ProcurementBatchId)));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Insufficient_inventory_blocks_non_final_processing_without_new_facts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            await InsertProcessMaterialPositionAsync(
                scenario,
                scenario.MaterialAId,
                scenario.PrimaryLocationId,
                2m,
                cancellationToken);

            var command = new ConfirmProcessingExecutionCommand(
                scenario.WorkDate,
                scenario.EmployeeId,
                scenario.ProcurementBatchId,
                scenario.PooledModuleId,
                null,
                null,
                null,
                new[]
                {
                    new ProcessingOutputMeasurement(
                        scenario.PooledOutputId,
                        3m,
                        null,
                        null,
                        null),
                });

            var result = await ExecuteAsync(
                Execution(commandId, scenario.ActorAccountId, Hash(6), command),
                cancellationToken);

            Assert.True(result.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, result.Error.Kind);
            Assert.Equal(ProcessingApplicationErrorCodes.InsufficientInventory, result.Error.Code);
            Assert.Equal(
                2m,
                await ReadPositionBalanceAsync(
                    scenario,
                    "PROCESS_MATERIAL",
                    scenario.MaterialAId,
                    scenario.PrimaryLocationId,
                    null,
                    null,
                    cancellationToken));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", commandId)));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Existing_daily_wage_blocks_normal_late_processing_and_rolls_back_command_identity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            await ExecuteNonQueryAsync(
                """
                INSERT INTO labor.employee_daily_wages
                    (id, work_date, employee_id, processing_wage_total_thb,
                     sales_packaging_wage_total_thb, total_wage_thb,
                     confirmed_at, confirmed_by_account_id, row_version)
                VALUES
                    (@id, @work_date, @employee_id, 0, 0, 0, @now, @actor_id, 1);
                """,
                cancellationToken,
                ("id", Guid.CreateVersion7()),
                ("work_date", scenario.WorkDate),
                ("employee_id", scenario.EmployeeId),
                ("now", DateTimeOffset.UtcNow),
                ("actor_id", scenario.ActorAccountId));

            var command = new ConfirmProcessingExecutionCommand(
                scenario.WorkDate,
                scenario.EmployeeId,
                scenario.ProcurementBatchId,
                scenario.PooledModuleId,
                null,
                null,
                null,
                new[]
                {
                    new ProcessingOutputMeasurement(
                        scenario.PooledOutputId,
                        1m,
                        null,
                        null,
                        null),
                });

            var result = await ExecuteAsync(
                Execution(commandId, scenario.ActorAccountId, Hash(7), command),
                cancellationToken);

            Assert.True(result.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, result.Error.Kind);
            Assert.Equal(LaborApplicationErrorCodes.DailyWageAlreadyConfirmed, result.Error.Code);
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", commandId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM processing.processing_executions WHERE procurement_batch_id = @batch_id;",
                    cancellationToken,
                    ("batch_id", scenario.ProcurementBatchId)));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Processing_transaction_lifecycle_closure_rebuilds_inventory_labor_finance_and_preserves_audit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(cancellationToken);
        var commandIds = Enumerable.Range(0, 13).Select(_ => Guid.CreateVersion7()).ToArray();

        try
        {
            await InsertProcurementProductPositionAsync(
                scenario,
                scenario.PrimaryLocationId,
                100m,
                ProcessingSourceKind.SUPPLIER,
                scenario.SupplierId,
                cancellationToken);

            var firstCommand = new ConfirmProcessingExecutionCommand(
                scenario.WorkDate,
                scenario.EmployeeId,
                scenario.ProcurementBatchId,
                scenario.SourceTrackedModuleId,
                new ProcessingSourceSelection(ProcessingSourceKind.SUPPLIER, scenario.SupplierId),
                new ProcessingScaleMeasurement(10m, null),
                null,
                new[] { new ProcessingOutputMeasurement(scenario.SourceTrackedOutputId, 8.5m, null, null, null) });
            var first = await ExecuteAsync(Execution(commandIds[0], scenario.ActorAccountId, Hash(20), firstCommand), cancellationToken);
            Assert.True(first.IsSuccess);

            var secondCommand = new ConfirmProcessingExecutionCommand(
                scenario.WorkDate,
                scenario.EmployeeId,
                scenario.ProcurementBatchId,
                scenario.SourceTrackedModuleId,
                new ProcessingSourceSelection(ProcessingSourceKind.SUPPLIER, scenario.SupplierId),
                new ProcessingScaleMeasurement(5m, null),
                null,
                new[] { new ProcessingOutputMeasurement(scenario.SourceTrackedOutputId, 4m, null, null, null) });
            var second = await ExecuteAsync(Execution(commandIds[1], scenario.ActorAccountId, Hash(21), secondCommand), cancellationToken);
            Assert.True(second.IsSuccess);

            var firstOutputId = await ScalarAsync<Guid>(
                "SELECT id FROM processing.processing_execution_outputs WHERE processing_execution_id = @id;",
                cancellationToken,
                ("id", first.Value.ProcessingExecutionId));
            var secondOutputId = await ScalarAsync<Guid>(
                "SELECT id FROM processing.processing_execution_outputs WHERE processing_execution_id = @id;",
                cancellationToken,
                ("id", second.Value.ProcessingExecutionId));

            Assert.Equal(85m, await ReadPositionBalanceAsync(
                scenario, "PROCUREMENT_PRODUCT", scenario.ProcurementProductId, scenario.PrimaryLocationId,
                "SUPPLIER", scenario.SupplierId, cancellationToken));
            Assert.Equal(12.5m, await ReadPositionBalanceAsync(
                scenario, "PROCESS_MATERIAL", scenario.MaterialAId, scenario.PrimaryLocationId,
                null, null, cancellationToken));

            var executionSoft = await ExecuteWithServicesAsync(
                (services, token) => services.GetRequiredService<IProcessingTransactionLifecycleExecutor>().SoftDeleteExecutionAsync(
                    new SoftDeleteProcessingExecutionExecution(
                        CommandId.From(commandIds[2]), Hash(22), ActorAccountId.From(scenario.ActorAccountId),
                        new SoftDeleteProcessingExecutionCommand(first.Value.ProcessingExecutionId, 1)), token),
                cancellationToken);
            Assert.True(executionSoft.IsSuccess);
            Assert.Equal(2, executionSoft.Value.RowVersion);

            var executionRestore = await ExecuteWithServicesAsync(
                (services, token) => services.GetRequiredService<IProcessingTransactionLifecycleExecutor>().RestoreExecutionAsync(
                    new RestoreProcessingExecutionExecution(
                        CommandId.From(commandIds[3]), Hash(23), ActorAccountId.From(scenario.ActorAccountId),
                        new RestoreProcessingExecutionCommand(first.Value.ProcessingExecutionId, 2)), token),
                cancellationToken);
            Assert.True(executionRestore.IsSuccess);
            Assert.Equal(3, executionRestore.Value.RowVersion);

            var inputSoft = await ExecuteWithServicesAsync(
                (services, token) => services.GetRequiredService<IProcessingTransactionLifecycleExecutor>().SoftDeleteInputAsync(
                    new SoftDeleteProcessingExecutionInputExecution(
                        CommandId.From(commandIds[4]), Hash(24), ActorAccountId.From(scenario.ActorAccountId),
                        new SoftDeleteProcessingExecutionInputCommand(first.Value.ProcessingExecutionId, 1)), token),
                cancellationToken);
            Assert.True(inputSoft.IsSuccess);
            Assert.Equal(2, inputSoft.Value.RowVersion);

            var inputRestore = await ExecuteWithServicesAsync(
                (services, token) => services.GetRequiredService<IProcessingTransactionLifecycleExecutor>().RestoreInputAsync(
                    new RestoreProcessingExecutionInputExecution(
                        CommandId.From(commandIds[5]), Hash(25), ActorAccountId.From(scenario.ActorAccountId),
                        new RestoreProcessingExecutionInputCommand(first.Value.ProcessingExecutionId, 2)), token),
                cancellationToken);
            Assert.True(inputRestore.IsSuccess);
            Assert.Equal(3, inputRestore.Value.RowVersion);

            var outputSoft = await ExecuteWithServicesAsync(
                (services, token) => services.GetRequiredService<IProcessingTransactionLifecycleExecutor>().SoftDeleteOutputAsync(
                    new SoftDeleteProcessingExecutionOutputExecution(
                        CommandId.From(commandIds[6]), Hash(26), ActorAccountId.From(scenario.ActorAccountId),
                        new SoftDeleteProcessingExecutionOutputCommand(firstOutputId, 1)), token),
                cancellationToken);
            Assert.True(outputSoft.IsSuccess);
            Assert.Equal(2, outputSoft.Value.RowVersion);

            var outputRestore = await ExecuteWithServicesAsync(
                (services, token) => services.GetRequiredService<IProcessingTransactionLifecycleExecutor>().RestoreOutputAsync(
                    new RestoreProcessingExecutionOutputExecution(
                        CommandId.From(commandIds[7]), Hash(27), ActorAccountId.From(scenario.ActorAccountId),
                        new RestoreProcessingExecutionOutputCommand(firstOutputId, 2)), token),
                cancellationToken);
            Assert.True(outputRestore.IsSuccess);
            Assert.Equal(3, outputRestore.Value.RowVersion);

            var wage = await ExecuteWithServicesAsync(
                (services, token) => services.GetRequiredService<IConfirmEmployeeDailyWageExecutor>().ExecuteAsync(
                    new ConfirmEmployeeDailyWageExecution(
                        CommandId.From(commandIds[8]), Hash(28), ActorAccountId.From(scenario.ActorAccountId),
                        new ConfirmEmployeeDailyWageCommand(
                            scenario.WorkDate,
                            scenario.EmployeeId,
                            Array.Empty<ProcessingWageRateOverride>())), token),
                cancellationToken);
            Assert.True(wage.IsSuccess);
            Assert.Equal(15L, wage.Value.ProcessingWageTotalThb);
            Assert.Equal(15L, wage.Value.TotalWageThb);
            Assert.Equal(15L, await ScalarAsync<long>(
                "SELECT outstanding_thb FROM finance.payable_outstanding_positions WHERE payable_id = @id;",
                cancellationToken, ("id", wage.Value.PayableId)));

            var hardOutput = await ExecuteWithServicesAsync(
                (services, token) => services.GetRequiredService<IHardDeleteProcessingTransactionExecutor>().HardDeleteOutputAsync(
                    new HardDeleteProcessingExecutionOutputExecution(
                        CommandId.From(commandIds[9]), Hash(29), ActorAccountId.From(scenario.ActorAccountId),
                        new HardDeleteProcessingExecutionOutputCommand(firstOutputId, 3)), token),
                cancellationToken);
            Assert.True(hardOutput.IsSuccess);
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM processing.processing_execution_outputs WHERE id = @id;",
                cancellationToken, ("id", firstOutputId)));
            Assert.Equal(4m, await ReadPositionBalanceAsync(
                scenario, "PROCESS_MATERIAL", scenario.MaterialAId, scenario.PrimaryLocationId,
                null, null, cancellationToken));
            Assert.Equal(4m, await ScalarAsync<decimal>(
                "SELECT aggregated_quantity FROM labor.processing_wage_components WHERE employee_daily_wage_id = @id;",
                cancellationToken, ("id", wage.Value.EmployeeDailyWageId)));
            Assert.Equal(5L, await ScalarAsync<long>(
                "SELECT processing_wage_total_thb FROM labor.employee_daily_wages WHERE id = @id;",
                cancellationToken, ("id", wage.Value.EmployeeDailyWageId)));
            Assert.Equal(5L, await ScalarAsync<long>(
                "SELECT amount_thb FROM finance.payable_obligation_items WHERE payable_id = @id;",
                cancellationToken, ("id", wage.Value.PayableId)));
            Assert.Equal(5L, await ScalarAsync<long>(
                "SELECT outstanding_thb FROM finance.payable_outstanding_positions WHERE payable_id = @id;",
                cancellationToken, ("id", wage.Value.PayableId)));

            var hardInput = await ExecuteWithServicesAsync(
                (services, token) => services.GetRequiredService<IHardDeleteProcessingTransactionExecutor>().HardDeleteInputAsync(
                    new HardDeleteProcessingExecutionInputExecution(
                        CommandId.From(commandIds[10]), Hash(30), ActorAccountId.From(scenario.ActorAccountId),
                        new HardDeleteProcessingExecutionInputCommand(first.Value.ProcessingExecutionId, 3)), token),
                cancellationToken);
            Assert.True(hardInput.IsSuccess);
            Assert.Equal(95m, await ReadPositionBalanceAsync(
                scenario, "PROCUREMENT_PRODUCT", scenario.ProcurementProductId, scenario.PrimaryLocationId,
                "SUPPLIER", scenario.SupplierId, cancellationToken));

            var hardFirstExecution = await ExecuteWithServicesAsync(
                (services, token) => services.GetRequiredService<IHardDeleteProcessingTransactionExecutor>().HardDeleteExecutionAsync(
                    new HardDeleteProcessingExecutionExecution(
                        CommandId.From(commandIds[11]), Hash(31), ActorAccountId.From(scenario.ActorAccountId),
                        new HardDeleteProcessingExecutionCommand(first.Value.ProcessingExecutionId, 3)), token),
                cancellationToken);
            Assert.True(hardFirstExecution.IsSuccess);
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM inventory.inventory_operations WHERE id = @id;",
                cancellationToken, ("id", first.Value.InventoryOperationId)));

            var secondHardExecution = new HardDeleteProcessingExecutionExecution(
                CommandId.From(commandIds[12]), Hash(32), ActorAccountId.From(scenario.ActorAccountId),
                new HardDeleteProcessingExecutionCommand(second.Value.ProcessingExecutionId, 1));
            var hardSecondExecution = await ExecuteWithServicesAsync(
                (services, token) => services.GetRequiredService<IHardDeleteProcessingTransactionExecutor>().HardDeleteExecutionAsync(
                    secondHardExecution, token),
                cancellationToken);
            Assert.True(hardSecondExecution.IsSuccess);

            var hardSecondReplay = await ExecuteWithServicesAsync(
                (services, token) => services.GetRequiredService<IHardDeleteProcessingTransactionExecutor>().HardDeleteExecutionAsync(
                    secondHardExecution, token),
                cancellationToken);
            Assert.True(hardSecondReplay.IsSuccess);
            Assert.Equal(hardSecondExecution.Value, hardSecondReplay.Value);

            Assert.Equal(100m, await ReadPositionBalanceAsync(
                scenario, "PROCUREMENT_PRODUCT", scenario.ProcurementProductId, scenario.PrimaryLocationId,
                "SUPPLIER", scenario.SupplierId, cancellationToken));
            Assert.Equal(0m, await ReadPositionBalanceAsync(
                scenario, "PROCESS_MATERIAL", scenario.MaterialAId, scenario.PrimaryLocationId,
                null, null, cancellationToken));
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT processing_wage_total_thb FROM labor.employee_daily_wages WHERE id = @id;",
                cancellationToken, ("id", wage.Value.EmployeeDailyWageId)));
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT amount_thb FROM finance.payable_obligation_items WHERE payable_id = @id;",
                cancellationToken, ("id", wage.Value.PayableId)));
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT outstanding_thb FROM finance.payable_outstanding_positions WHERE payable_id = @id;",
                cancellationToken, ("id", wage.Value.PayableId)));
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM processing.processing_execution_outputs WHERE id = @id;",
                cancellationToken, ("id", secondOutputId)));
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM inventory.inventory_operations WHERE id = @id;",
                cancellationToken, ("id", second.Value.InventoryOperationId)));
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM system.outbox_messages WHERE command_id = ANY(@ids) AND message_type = 'processing.execution.confirmed';",
                cancellationToken, ("ids", new[] { commandIds[0], commandIds[1] })));
            Assert.Equal(4L, await ScalarAsync<long>(
                "SELECT count(*) FROM audit.audit_events WHERE command_id = ANY(@ids) AND event_kind = 'HARD_DELETE';",
                cancellationToken, ("ids", new[] { commandIds[9], commandIds[10], commandIds[11], commandIds[12] })));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, commandIds, cancellationToken);
        }
    }

    private static async ValueTask<TResult> ExecuteWithServicesAsync<TResult>(
        Func<IServiceProvider, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await operation(scope.ServiceProvider, cancellationToken);
    }

    private static ConfirmProcessingExecutionExecution Execution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        ConfirmProcessingExecutionCommand command) =>
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

    private static async ValueTask<ApplicationResult<ConfirmProcessingExecutionResult>> ExecuteAsync(
        ConfirmProcessingExecutionExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<IConfirmProcessingExecutionExecutor>();
        return await executor.ExecuteAsync(execution, cancellationToken);
    }

    private static async Task<Scenario> SeedScenarioAsync(CancellationToken cancellationToken)
    {
        var scenario = new Scenario(
            ActorAccountId: Guid.CreateVersion7(),
            EmployeeId: Guid.CreateVersion7(),
            SupplierId: Guid.CreateVersion7(),
            WarehouseId: Guid.CreateVersion7(),
            PrimaryLocationId: Guid.CreateVersion7(),
            SecondaryLocationId: Guid.CreateVersion7(),
            ProcurementProductId: Guid.CreateVersion7(),
            SalesProductGroupId: Guid.CreateVersion7(),
            SalesProductId: Guid.CreateVersion7(),
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
            ProcurementBatchId: Guid.CreateVersion7(),
            WorkDate: new DateOnly(2026, 9, 2));

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 V3 actor', true, NULL, NULL, @now);

            INSERT INTO party.employees
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, created_at, created_by_account_id)
            VALUES
                (@employee_id, 'P6 V3 employee', NULL, NULL, NULL, NULL, NULL,
                 true, @now, @actor_id);

            INSERT INTO party.suppliers
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, created_at, created_by_account_id)
            VALUES
                (@supplier_id, 'P6 V3 supplier', NULL, NULL, NULL, NULL, NULL,
                 true, @now, @actor_id);

            INSERT INTO infrastructure.warehouses
                (id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@warehouse_id, NULL, 'P6 V3 warehouse', NULL, true, @now, @actor_id);

            INSERT INTO infrastructure.storage_locations
                (id, warehouse_id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@primary_location_id, @warehouse_id, NULL, 'P6 V3 primary location', NULL, true, @now, @actor_id),
                (@secondary_location_id, @warehouse_id, NULL, 'P6 V3 secondary location', NULL, true, @now, @actor_id);

            INSERT INTO product.procurement_products
                (id, name_zh_tw, name_th_th, unit_code, default_storage_location_id,
                 active, created_at, created_by_account_id)
            VALUES
                (@procurement_product_id, 'P6 V3 procurement product', NULL, 'kg', @primary_location_id,
                 true, @now, @actor_id);

            INSERT INTO product.sales_product_groups
                (id, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@sales_product_group_id, 'P6 V3 sales group', NULL, true, @now, @actor_id);

            INSERT INTO product.sales_products
                (id, sales_product_group_id, name_zh_tw, name_th_th, pricing_basis,
                 packaging_weight, sales_weight, default_storage_location_id,
                 active, created_at, created_by_account_id)
            VALUES
                (@sales_product_id, @sales_product_group_id, 'P6 V3 sales product', NULL, 'UNIT_BASED',
                 0.5, NULL, @primary_location_id,
                 true, @now, @actor_id);

            INSERT INTO processing_config.processing_routes
                (id, procurement_product_id, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@route_id, @procurement_product_id, 'P6 V3 route', NULL, true, @now, @actor_id);

            INSERT INTO processing_config.processing_route_versions
                (id, processing_route_id, version_number, status)
            VALUES
                (@route_version_id, @route_id, 1, 'ACTIVE');

            INSERT INTO processing_config.route_input_configs
                (processing_route_version_id, uses_container, container_id, default_container_count, default_storage_location_id)
            VALUES
                (@route_version_id, false, NULL, NULL, @primary_location_id);

            INSERT INTO processing_config.process_materials
                (id, processing_route_version_id, name_zh_tw, name_th_th,
                 uses_container, container_id, default_container_count, default_storage_location_id,
                 active, created_at, created_by_account_id)
            VALUES
                (@material_a_id, @route_version_id, 'P6 V3 material A', NULL,
                 false, NULL, NULL, @primary_location_id,
                 true, @now, @actor_id),
                (@material_b_id, @route_version_id, 'P6 V3 material B', NULL,
                 false, NULL, NULL, @primary_location_id,
                 true, @now, @actor_id);

            INSERT INTO processing_config.processing_modules
                (id, processing_route_version_id, name_zh_tw, name_th_th,
                 execution_mode, input_process_material_id, negative_inventory_policy)
            VALUES
                (@source_module_id, @route_version_id, 'P6 V3 source tracked', NULL,
                 'SOURCE_TRACKED', NULL, 'CONFIGURED'),
                (@pooled_module_id, @route_version_id, 'P6 V3 pooled output', NULL,
                 'POOLED_OUTPUT', @material_a_id, 'CONFIGURED'),
                (@final_module_id, @route_version_id, 'P6 V3 final packaging', NULL,
                 'FINAL_PACKAGING', @material_b_id, 'CONFIGURED');

            INSERT INTO processing_config.processing_module_outputs
                (id, processing_module_id, processing_route_version_id, output_sequence,
                 output_kind, process_material_id, sales_product_id, default_wage_rate)
            VALUES
                (@source_output_id, @source_module_id, @route_version_id, 1,
                 'PROCESS_MATERIAL', @material_a_id, NULL, 1.25),
                (@pooled_output_id, @pooled_module_id, @route_version_id, 1,
                 'PROCESS_MATERIAL', @material_b_id, NULL, 1.5),
                (@final_output_id, @final_module_id, @route_version_id, 1,
                 'SALES_PRODUCT', NULL, @sales_product_id, 2.0);

            INSERT INTO procurement.procurement_batches
                (id, procurement_date, procurement_product_id, procurement_status,
                 lifecycle_status, processing_route_id, processing_route_version_id,
                 created_at, created_by_account_id)
            VALUES
                (@batch_id, @work_date, @procurement_product_id, 'OPEN',
                 'ACTIVE', @route_id, @route_version_id,
                 @now, @actor_id);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("employee_id", scenario.EmployeeId),
            ("supplier_id", scenario.SupplierId),
            ("warehouse_id", scenario.WarehouseId),
            ("primary_location_id", scenario.PrimaryLocationId),
            ("secondary_location_id", scenario.SecondaryLocationId),
            ("procurement_product_id", scenario.ProcurementProductId),
            ("sales_product_group_id", scenario.SalesProductGroupId),
            ("sales_product_id", scenario.SalesProductId),
            ("route_id", scenario.RouteId),
            ("route_version_id", scenario.RouteVersionId),
            ("material_a_id", scenario.MaterialAId),
            ("material_b_id", scenario.MaterialBId),
            ("source_module_id", scenario.SourceTrackedModuleId),
            ("source_output_id", scenario.SourceTrackedOutputId),
            ("pooled_module_id", scenario.PooledModuleId),
            ("pooled_output_id", scenario.PooledOutputId),
            ("final_module_id", scenario.FinalPackagingModuleId),
            ("final_output_id", scenario.FinalPackagingOutputId),
            ("batch_id", scenario.ProcurementBatchId),
            ("work_date", scenario.WorkDate),
            ("now", DateTimeOffset.UtcNow));

        return scenario;
    }

    private static Task InsertProcurementProductPositionAsync(
        Scenario scenario,
        Guid locationId,
        decimal balance,
        ProcessingSourceKind sourceKind,
        Guid? supplierId,
        CancellationToken cancellationToken) =>
        ExecuteNonQueryAsync(
            """
            INSERT INTO inventory.inventory_positions
                (id, origin, procurement_batch_id, outsourced_supply_batch_id,
                 inventory_object_kind, procurement_product_id, process_material_id, sales_product_id,
                 storage_location_id, raw_source_kind, supplier_id, balance_quantity)
            VALUES
                (@id, 'IN_HOUSE', @batch_id, NULL,
                 'PROCUREMENT_PRODUCT', @product_id, NULL, NULL,
                 @location_id, @raw_source_kind, @supplier_id, @balance);
            """,
            cancellationToken,
            ("id", Guid.CreateVersion7()),
            ("batch_id", scenario.ProcurementBatchId),
            ("product_id", scenario.ProcurementProductId),
            ("location_id", locationId),
            ("raw_source_kind", sourceKind.ToString()),
            ("supplier_id", (object?)supplierId ?? DBNull.Value),
            ("balance", balance));

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

    private static async Task<decimal> ReadPositionBalanceAsync(
        Scenario scenario,
        string objectKind,
        Guid objectId,
        Guid locationId,
        string? rawSourceKind,
        Guid? supplierId,
        CancellationToken cancellationToken)
    {
        var objectPredicate = objectKind switch
        {
            "PROCUREMENT_PRODUCT" => "procurement_product_id = @object_id AND process_material_id IS NULL AND sales_product_id IS NULL",
            "PROCESS_MATERIAL" => "procurement_product_id IS NULL AND process_material_id = @object_id AND sales_product_id IS NULL",
            "SALES_PRODUCT" => "procurement_product_id IS NULL AND process_material_id IS NULL AND sales_product_id = @object_id",
            _ => throw new ArgumentOutOfRangeException(nameof(objectKind), objectKind, null),
        };

        var rawPredicate = rawSourceKind is null
            ? "raw_source_kind IS NULL AND supplier_id IS NULL"
            : "raw_source_kind = @raw_source_kind AND ((@supplier_id IS NULL AND supplier_id IS NULL) OR supplier_id = @supplier_id)";

        return await ScalarAsync<decimal>(
            $"""
            SELECT balance_quantity
            FROM inventory.inventory_positions
            WHERE origin = 'IN_HOUSE'
              AND procurement_batch_id = @batch_id
              AND inventory_object_kind = @object_kind
              AND {objectPredicate}
              AND storage_location_id = @location_id
              AND {rawPredicate};
            """,
            cancellationToken,
            ("batch_id", scenario.ProcurementBatchId),
            ("object_kind", objectKind),
            ("object_id", objectId),
            ("location_id", locationId),
            ("raw_source_kind", (object?)rawSourceKind ?? DBNull.Value),
            ("supplier_id", (object?)supplierId ?? DBNull.Value));
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
                SELECT payable.id
                FROM finance.payables payable
                JOIN labor.employee_daily_wages wage ON wage.id = payable.employee_daily_wage_id
                WHERE wage.work_date = @work_date AND wage.employee_id = @employee_id);
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

            DELETE FROM labor.employee_daily_wages
            WHERE work_date = @work_date AND employee_id = @employee_id;

            DELETE FROM procurement.procurement_batches WHERE id = @batch_id;

            DELETE FROM processing_config.processing_module_outputs
            WHERE processing_route_version_id = @route_version_id;
            DELETE FROM processing_config.processing_modules
            WHERE processing_route_version_id = @route_version_id;
            DELETE FROM processing_config.process_materials
            WHERE processing_route_version_id = @route_version_id;
            DELETE FROM processing_config.route_input_configs
            WHERE processing_route_version_id = @route_version_id;
            DELETE FROM processing_config.processing_route_versions WHERE id = @route_version_id;
            DELETE FROM processing_config.processing_routes WHERE id = @route_id;

            DELETE FROM product.sales_products WHERE id = @sales_product_id;
            DELETE FROM product.sales_product_groups WHERE id = @sales_product_group_id;
            DELETE FROM product.procurement_products WHERE id = @procurement_product_id;
            DELETE FROM party.suppliers WHERE id = @supplier_id;
            DELETE FROM party.employees WHERE id = @employee_id;
            DELETE FROM infrastructure.storage_locations
            WHERE id = @primary_location_id OR id = @secondary_location_id;
            DELETE FROM infrastructure.warehouses WHERE id = @warehouse_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds.ToArray()),
            ("work_date", scenario.WorkDate),
            ("batch_id", scenario.ProcurementBatchId),
            ("route_version_id", scenario.RouteVersionId),
            ("route_id", scenario.RouteId),
            ("sales_product_id", scenario.SalesProductId),
            ("sales_product_group_id", scenario.SalesProductGroupId),
            ("procurement_product_id", scenario.ProcurementProductId),
            ("supplier_id", scenario.SupplierId),
            ("employee_id", scenario.EmployeeId),
            ("primary_location_id", scenario.PrimaryLocationId),
            ("secondary_location_id", scenario.SecondaryLocationId),
            ("warehouse_id", scenario.WarehouseId),
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
        Guid SupplierId,
        Guid WarehouseId,
        Guid PrimaryLocationId,
        Guid SecondaryLocationId,
        Guid ProcurementProductId,
        Guid SalesProductGroupId,
        Guid SalesProductId,
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
        Guid ProcurementBatchId,
        DateOnly WorkDate);
}
