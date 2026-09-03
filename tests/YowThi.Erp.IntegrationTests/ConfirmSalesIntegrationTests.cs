using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Sales;
using YowThi.Erp.Domain.Inventory;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class ConfirmSalesIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Auto_allocation_prefers_outsourced_then_in_house_and_commits_receivable_inventory_audit_and_replay()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(8m, 4m, 10m, cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            var command = new ConfirmSalesCommand(
                scenario.SalesId,
                1,
                Array.Empty<SalesManualAllocationOverride>());
            var execution = Execution(commandId, scenario.ActorAccountId, Hash(1), command);

            var result = await ExecuteAsync(execution, cancellationToken);
            Assert.True(result.IsSuccess);
            Assert.Equal(scenario.SalesId, result.Value.SalesId);
            Assert.Equal(2L, result.Value.SalesRowVersion);

            var replay = await ExecuteAsync(execution, cancellationToken);
            Assert.True(replay.IsSuccess);
            Assert.Equal(result.Value, replay.Value);

            var changedHash = await ExecuteAsync(
                Execution(commandId, scenario.ActorAccountId, Hash(2), command),
                cancellationToken);
            Assert.True(changedHash.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, changedHash.Error.Kind);
            Assert.Equal(SalesApplicationErrorCodes.IdempotencyKeyReused, changedHash.Error.Code);

            var changedActor = await ExecuteAsync(
                Execution(commandId, scenario.SecondActorAccountId, Hash(1), command),
                cancellationToken);
            Assert.True(changedActor.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, changedActor.Error.Kind);
            Assert.Equal(SalesApplicationErrorCodes.IdempotencyKeyReused, changedActor.Error.Code);

            Assert.Equal(
                "CONFIRMED",
                await ScalarAsync<string>(
                    "SELECT status FROM sales.sales WHERE id = @sales_id;",
                    cancellationToken,
                    ("sales_id", scenario.SalesId)));
            Assert.Equal(
                0,
                await ScalarAsync<int>(
                    "SELECT revision_number FROM sales.sales_allocation_revisions WHERE id = @revision_id AND sales_id = @sales_id;",
                    cancellationToken,
                    ("revision_id", result.Value.AllocationRevisionId),
                    ("sales_id", scenario.SalesId)));

            Assert.Equal(
                "OUTSOURCED",
                await ScalarAsync<string>(
                    "SELECT origin FROM sales.sales_allocation_revision_items WHERE sales_allocation_revision_id = @revision_id AND sequence = 1;",
                    cancellationToken,
                    ("revision_id", result.Value.AllocationRevisionId)));
            Assert.Equal(
                4m,
                await ScalarAsync<decimal>(
                    "SELECT allocated_quantity FROM sales.sales_allocation_revision_items WHERE sales_allocation_revision_id = @revision_id AND sequence = 1;",
                    cancellationToken,
                    ("revision_id", result.Value.AllocationRevisionId)));
            Assert.False(
                await ScalarAsync<bool>(
                    "SELECT manual_override FROM sales.sales_allocation_revision_items WHERE sales_allocation_revision_id = @revision_id AND sequence = 1;",
                    cancellationToken,
                    ("revision_id", result.Value.AllocationRevisionId)));

            Assert.Equal(
                "IN_HOUSE",
                await ScalarAsync<string>(
                    "SELECT origin FROM sales.sales_allocation_revision_items WHERE sales_allocation_revision_id = @revision_id AND sequence = 2;",
                    cancellationToken,
                    ("revision_id", result.Value.AllocationRevisionId)));
            Assert.Equal(
                4m,
                await ScalarAsync<decimal>(
                    "SELECT allocated_quantity FROM sales.sales_allocation_revision_items WHERE sales_allocation_revision_id = @revision_id AND sequence = 2;",
                    cancellationToken,
                    ("revision_id", result.Value.AllocationRevisionId)));

            Assert.Equal(
                2L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM sales.sales_allocations WHERE sales_detail_id = @detail_id;",
                    cancellationToken,
                    ("detail_id", scenario.SalesDetailId)));
            Assert.Equal(
                2L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM inventory.inventory_movements WHERE inventory_operation_id = @operation_id AND movement_type = 'SALES_ISSUE' AND sales_allocation_revision_item_id IS NOT NULL;",
                    cancellationToken,
                    ("operation_id", result.Value.InventoryOperationId)));
            Assert.Equal(
                -8m,
                await ScalarAsync<decimal>(
                    "SELECT sum(quantity_delta) FROM inventory.inventory_movements WHERE inventory_operation_id = @operation_id;",
                    cancellationToken,
                    ("operation_id", result.Value.InventoryOperationId)));

            Assert.Equal(
                0m,
                await ScalarAsync<decimal>(
                    "SELECT balance_quantity FROM inventory.inventory_positions WHERE id = @position_id;",
                    cancellationToken,
                    ("position_id", scenario.OutsourcedPositionId)));
            Assert.Equal(
                6m,
                await ScalarAsync<decimal>(
                    "SELECT balance_quantity FROM inventory.inventory_positions WHERE id = @position_id;",
                    cancellationToken,
                    ("position_id", scenario.InHousePositionId)));

            Assert.Equal(
                400L,
                await ScalarAsync<long>(
                    "SELECT amount_thb FROM finance.receivable_obligation_items WHERE receivable_id = @receivable_id AND sales_detail_id = @detail_id;",
                    cancellationToken,
                    ("receivable_id", result.Value.ReceivableId),
                    ("detail_id", scenario.SalesDetailId)));
            Assert.Equal(
                400L,
                await ScalarAsync<long>(
                    "SELECT outstanding_thb FROM finance.receivable_outstanding_positions WHERE receivable_id = @receivable_id;",
                    cancellationToken,
                    ("receivable_id", result.Value.ReceivableId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id AND status = 'SUCCEEDED';",
                    cancellationToken,
                    ("command_id", commandId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = @command_id AND command_type = 'ConfirmSales';",
                    cancellationToken,
                    ("command_id", commandId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.outbox_messages WHERE command_id = @command_id AND message_type = 'sales.confirmed';",
                    cancellationToken,
                    ("command_id", commandId)));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Manual_override_can_select_in_house_source_instead_of_auto_priority()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(4m, 4m, 10m, cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            var command = new ConfirmSalesCommand(
                scenario.SalesId,
                1,
                new[]
                {
                    new SalesManualAllocationOverride(
                        scenario.SalesDetailId,
                        InventoryOrigin.IN_HOUSE,
                        scenario.ProcurementBatchId,
                        null,
                        4m),
                });

            var result = await ExecuteAsync(
                Execution(commandId, scenario.ActorAccountId, Hash(3), command),
                cancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM sales.sales_allocation_revision_items WHERE sales_allocation_revision_id = @revision_id;",
                    cancellationToken,
                    ("revision_id", result.Value.AllocationRevisionId)));
            Assert.Equal(
                "IN_HOUSE",
                await ScalarAsync<string>(
                    "SELECT origin FROM sales.sales_allocation_revision_items WHERE sales_allocation_revision_id = @revision_id;",
                    cancellationToken,
                    ("revision_id", result.Value.AllocationRevisionId)));
            Assert.True(
                await ScalarAsync<bool>(
                    "SELECT manual_override FROM sales.sales_allocation_revision_items WHERE sales_allocation_revision_id = @revision_id;",
                    cancellationToken,
                    ("revision_id", result.Value.AllocationRevisionId)));
            Assert.Equal(
                4m,
                await ScalarAsync<decimal>(
                    "SELECT balance_quantity FROM inventory.inventory_positions WHERE id = @position_id;",
                    cancellationToken,
                    ("position_id", scenario.OutsourcedPositionId)));
            Assert.Equal(
                6m,
                await ScalarAsync<decimal>(
                    "SELECT balance_quantity FROM inventory.inventory_positions WHERE id = @position_id;",
                    cancellationToken,
                    ("position_id", scenario.InHousePositionId)));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Sales_001_blocks_ambiguous_source_location_without_persisting_command_or_issue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(2m, 4m, 10m, cancellationToken, addSecondOutsourcedLocation: true);
        var commandId = Guid.CreateVersion7();

        try
        {
            var result = await ExecuteAsync(
                Execution(
                    commandId,
                    scenario.ActorAccountId,
                    Hash(4),
                    new ConfirmSalesCommand(scenario.SalesId, 1, Array.Empty<SalesManualAllocationOverride>())),
                cancellationToken);

            Assert.True(result.IsFailure);
            Assert.Equal(ApplicationErrorKind.Validation, result.Error.Kind);
            Assert.Equal(SalesApplicationErrorCodes.IssueLocationRequired, result.Error.Code);
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", commandId)));
            Assert.Equal(
                "DRAFT",
                await ScalarAsync<string>(
                    "SELECT status FROM sales.sales WHERE id = @sales_id;",
                    cancellationToken,
                    ("sales_id", scenario.SalesId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM inventory.inventory_operations WHERE sales_id = @sales_id;",
                    cancellationToken,
                    ("sales_id", scenario.SalesId)));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Insufficient_stock_and_stale_sales_version_roll_back_command_acquisition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var insufficientScenario = await SeedScenarioAsync(20m, 4m, 10m, cancellationToken);
        var insufficientCommandId = Guid.CreateVersion7();

        try
        {
            var insufficient = await ExecuteAsync(
                Execution(
                    insufficientCommandId,
                    insufficientScenario.ActorAccountId,
                    Hash(5),
                    new ConfirmSalesCommand(
                        insufficientScenario.SalesId,
                        1,
                        Array.Empty<SalesManualAllocationOverride>())),
                cancellationToken);

            Assert.True(insufficient.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, insufficient.Error.Kind);
            Assert.Equal(SalesApplicationErrorCodes.InsufficientStock, insufficient.Error.Code);
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", insufficientCommandId)));
        }
        finally
        {
            await CleanupScenarioAsync(insufficientScenario, new[] { insufficientCommandId }, cancellationToken);
        }

        var staleScenario = await SeedScenarioAsync(1m, 4m, 10m, cancellationToken);
        var staleCommandId = Guid.CreateVersion7();
        try
        {
            var stale = await ExecuteAsync(
                Execution(
                    staleCommandId,
                    staleScenario.ActorAccountId,
                    Hash(6),
                    new ConfirmSalesCommand(
                        staleScenario.SalesId,
                        2,
                        Array.Empty<SalesManualAllocationOverride>())),
                cancellationToken);

            Assert.True(stale.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, stale.Error.Kind);
            Assert.Equal(SalesApplicationErrorCodes.StaleRowVersion, stale.Error.Code);
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", staleCommandId)));
        }
        finally
        {
            await CleanupScenarioAsync(staleScenario, new[] { staleCommandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Sale_without_details_is_blocked_as_invalid_confirm_input()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(0m, 4m, 10m, cancellationToken, includeSalesDetail: false);
        var commandId = Guid.CreateVersion7();

        try
        {
            var result = await ExecuteAsync(
                Execution(
                    commandId,
                    scenario.ActorAccountId,
                    Hash(7),
                    new ConfirmSalesCommand(scenario.SalesId, 1, Array.Empty<SalesManualAllocationOverride>())),
                cancellationToken);

            Assert.True(result.IsFailure);
            Assert.Equal(ApplicationErrorKind.Validation, result.Error.Kind);
            Assert.Equal(SalesApplicationErrorCodes.DetailInvalid, result.Error.Code);
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
    public async Task Repeated_sales_details_share_one_position_without_self_concurrency()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(3m, 10m, 0m, cancellationToken);
        var secondDetailId = Guid.CreateVersion7();
        var commandId = Guid.CreateVersion7();

        try
        {
            await InsertSalesDetailAsync(scenario, secondDetailId, 2, 4m, cancellationToken);

            var result = await ExecuteAsync(
                Execution(
                    commandId,
                    scenario.ActorAccountId,
                    Hash(8),
                    new ConfirmSalesCommand(
                        scenario.SalesId,
                        1,
                        Array.Empty<SalesManualAllocationOverride>())),
                cancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Equal(
                3m,
                await ScalarAsync<decimal>(
                    "SELECT balance_quantity FROM inventory.inventory_positions WHERE id = @position_id;",
                    cancellationToken,
                    ("position_id", scenario.OutsourcedPositionId)));
            Assert.Equal(
                2L,
                await ScalarAsync<long>(
                    "SELECT row_version FROM inventory.inventory_positions WHERE id = @position_id;",
                    cancellationToken,
                    ("position_id", scenario.OutsourcedPositionId)));
            Assert.Equal(
                2L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM sales.sales_allocation_revision_items WHERE sales_id = @sales_id;",
                    cancellationToken,
                    ("sales_id", scenario.SalesId)));
            Assert.Equal(
                -7m,
                await ScalarAsync<decimal>(
                    "SELECT sum(quantity_delta) FROM inventory.inventory_movements WHERE inventory_operation_id = @operation_id;",
                    cancellationToken,
                    ("operation_id", result.Value.InventoryOperationId)));
            Assert.Equal(
                350L,
                await ScalarAsync<long>(
                    "SELECT outstanding_thb FROM finance.receivable_outstanding_positions WHERE receivable_id = @receivable_id;",
                    cancellationToken,
                    ("receivable_id", result.Value.ReceivableId)));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Concurrent_sales_competing_for_one_position_allow_at_most_one_commit_and_never_negative_stock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(6m, 8m, 0m, cancellationToken);
        var competitor = await SeedCompetingSaleAsync(scenario, 6m, cancellationToken);
        var firstCommandId = Guid.CreateVersion7();
        var secondCommandId = Guid.CreateVersion7();
        var lockReleased = false;

        await using var lockConnection = await OpenConnectionAsync(cancellationToken);
        await using var lockTransaction = await lockConnection.BeginTransactionAsync(cancellationToken);
        await using (var lockCommand = new NpgsqlCommand(
                         "SELECT id FROM inventory.inventory_positions WHERE id = @position_id FOR UPDATE;",
                         lockConnection,
                         lockTransaction))
        {
            lockCommand.Parameters.AddWithValue("position_id", scenario.OutsourcedPositionId);
            Assert.NotNull(await lockCommand.ExecuteScalarAsync(cancellationToken));
        }

        var firstTask = ExecuteAsync(
                Execution(
                    firstCommandId,
                    scenario.ActorAccountId,
                    Hash(9),
                    new ConfirmSalesCommand(
                        scenario.SalesId,
                        1,
                        Array.Empty<SalesManualAllocationOverride>())),
                cancellationToken)
            .AsTask();
        var secondTask = ExecuteAsync(
                Execution(
                    secondCommandId,
                    scenario.ActorAccountId,
                    Hash(10),
                    new ConfirmSalesCommand(
                        competitor.SalesId,
                        1,
                        Array.Empty<SalesManualAllocationOverride>())),
                cancellationToken)
            .AsTask();

        try
        {
            await WaitForBlockedInventoryUpdatesAsync(2, cancellationToken);
            await lockTransaction.CommitAsync(cancellationToken);
            lockReleased = true;

            var results = await Task.WhenAll(firstTask, secondTask);
            var succeeded = results.Where(result => result.IsSuccess).ToArray();
            var failed = results.Where(result => result.IsFailure).ToArray();

            Assert.Single(succeeded);
            var losingResult = Assert.Single(failed);
            Assert.Equal(ApplicationErrorKind.Conflict, losingResult.Error.Kind);
            Assert.Equal(SalesApplicationErrorCodes.ConcurrentInventoryChange, losingResult.Error.Code);

            Assert.Equal(
                2m,
                await ScalarAsync<decimal>(
                    "SELECT balance_quantity FROM inventory.inventory_positions WHERE id = @position_id;",
                    cancellationToken,
                    ("position_id", scenario.OutsourcedPositionId)));
            Assert.Equal(
                2L,
                await ScalarAsync<long>(
                    "SELECT row_version FROM inventory.inventory_positions WHERE id = @position_id;",
                    cancellationToken,
                    ("position_id", scenario.OutsourcedPositionId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM sales.sales WHERE id = ANY(@sales_ids) AND status = 'CONFIRMED';",
                    cancellationToken,
                    ("sales_ids", new[] { scenario.SalesId, competitor.SalesId })));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM sales.sales WHERE id = ANY(@sales_ids) AND status = 'DRAFT';",
                    cancellationToken,
                    ("sales_ids", new[] { scenario.SalesId, competitor.SalesId })));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = ANY(@command_ids) AND status = 'SUCCEEDED';",
                    cancellationToken,
                    ("command_ids", new[] { firstCommandId, secondCommandId })));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM finance.receivables WHERE sales_id = ANY(@sales_ids);",
                    cancellationToken,
                    ("sales_ids", new[] { scenario.SalesId, competitor.SalesId })));
            Assert.Equal(
                -6m,
                await ScalarAsync<decimal>(
                    """
                    SELECT sum(m.quantity_delta)
                    FROM inventory.inventory_movements m
                    JOIN inventory.inventory_operations o ON o.id = m.inventory_operation_id
                    WHERE o.sales_id = ANY(@sales_ids)
                      AND m.movement_type = 'SALES_ISSUE';
                    """,
                    cancellationToken,
                    ("sales_ids", new[] { scenario.SalesId, competitor.SalesId })));
        }
        finally
        {
            if (!lockReleased)
            {
                await lockTransaction.RollbackAsync(CancellationToken.None);
            }

            try
            {
                await Task.WhenAll(firstTask, secondTask);
            }
            catch
            {
                // Preserve the original assertion/command failure while ensuring blocked work is released before cleanup.
            }

            await CleanupAdditionalSaleAsync(competitor, cancellationToken);
            await CleanupScenarioAsync(
                scenario,
                new[] { firstCommandId, secondCommandId },
                cancellationToken);
        }
    }

    private static ConfirmSalesExecution Execution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        ConfirmSalesCommand command) =>
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

    private static async ValueTask<ApplicationResult<ConfirmSalesResult>> ExecuteAsync(
        ConfirmSalesExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<IConfirmSalesExecutor>();
        return await executor.ExecuteAsync(execution, cancellationToken);
    }

    private static async Task<Scenario> SeedScenarioAsync(
        decimal salesQuantity,
        decimal outsourcedBalance,
        decimal inHouseBalance,
        CancellationToken cancellationToken,
        bool addSecondOutsourcedLocation = false,
        bool includeSalesDetail = true)
    {
        var scenario = new Scenario(
            ActorAccountId: Guid.CreateVersion7(),
            SecondActorAccountId: Guid.CreateVersion7(),
            WarehouseId: Guid.CreateVersion7(),
            StorageLocationId: Guid.CreateVersion7(),
            SecondStorageLocationId: Guid.CreateVersion7(),
            SalesProductGroupId: Guid.CreateVersion7(),
            SalesProductId: Guid.CreateVersion7(),
            ProcurementProductId: Guid.CreateVersion7(),
            VendorId: Guid.CreateVersion7(),
            CustomerId: Guid.CreateVersion7(),
            OutsourcedSupplyBatchId: Guid.CreateVersion7(),
            ProcurementBatchId: Guid.CreateVersion7(),
            OutsourcedPositionId: Guid.CreateVersion7(),
            InHousePositionId: Guid.CreateVersion7(),
            SecondOutsourcedPositionId: Guid.CreateVersion7(),
            SalesId: Guid.CreateVersion7(),
            SalesDetailId: Guid.CreateVersion7());

        var amountThb = checked((long)decimal.Floor(salesQuantity * 50m));
        var now = DateTimeOffset.UtcNow;

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 V4 actor', true, NULL, NULL, @now),
                (@second_actor_id, 'P6 V4 second actor', true, NULL, NULL, @now);

            INSERT INTO infrastructure.warehouses
                (id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@warehouse_id, NULL, 'P6 V4 warehouse', NULL, true, @now, @actor_id);

            INSERT INTO infrastructure.storage_locations
                (id, warehouse_id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@location_id, @warehouse_id, NULL, 'P6 V4 location A', NULL, true, @now, @actor_id),
                (@second_location_id, @warehouse_id, NULL, 'P6 V4 location B', NULL, true, @now, @actor_id);

            INSERT INTO product.sales_product_groups
                (id, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@sales_product_group_id, 'P6 V4 group', NULL, true, @now, @actor_id);

            INSERT INTO product.sales_products
                (id, sales_product_group_id, name_zh_tw, name_th_th, pricing_basis,
                 packaging_weight, sales_weight, default_storage_location_id,
                 active, created_at, created_by_account_id)
            VALUES
                (@sales_product_id, @sales_product_group_id, 'P6 V4 sale product', NULL, 'UNIT_BASED',
                 NULL, NULL, @location_id,
                 true, @now, @actor_id);

            INSERT INTO product.procurement_products
                (id, name_zh_tw, name_th_th, unit_code, default_storage_location_id,
                 active, created_at, created_by_account_id)
            VALUES
                (@procurement_product_id, 'P6 V4 procurement product', NULL, 'kg', @location_id,
                 true, @now, @actor_id);

            INSERT INTO party.outsourced_vendors
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, created_at, created_by_account_id)
            VALUES
                (@vendor_id, 'P6 V4 vendor', NULL, NULL, NULL, NULL, NULL,
                 true, @now, @actor_id);

            INSERT INTO party.customers
                (id, name_zh_tw, name_th_th, phone, active, created_at, created_by_account_id)
            VALUES
                (@customer_id, 'P6 V4 customer', NULL, NULL, true, @now, @actor_id);

            INSERT INTO outsourced.outsourced_supply_batches
                (id, supply_date, outsourced_vendor_id, lifecycle_status,
                 closed_at, closed_by_account_id, created_at, created_by_account_id,
                 deleted_at, deleted_by_account_id)
            VALUES
                (@outsourced_batch_id, DATE '2026-08-31', @vendor_id, 'ACTIVE',
                 NULL, NULL, @now, @actor_id, NULL, NULL);

            INSERT INTO procurement.procurement_batches
                (id, procurement_date, procurement_product_id, procurement_status,
                 lifecycle_status, processing_route_id, processing_route_version_id,
                 completed_at, completed_by_account_id, closed_at, closed_by_account_id,
                 created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@procurement_batch_id, DATE '2026-08-20', @procurement_product_id, 'OPEN',
                 'ACTIVE', NULL, NULL,
                 NULL, NULL, NULL, NULL,
                 @now, @actor_id, NULL, NULL);

            INSERT INTO sales.sales
                (id, sales_date, customer_id, status, confirmed_at, confirmed_by_account_id,
                 row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@sales_id, DATE '2026-09-03', @customer_id, 'DRAFT', NULL, NULL,
                 1, @now, @actor_id, NULL, NULL);

            INSERT INTO inventory.inventory_positions
                (id, origin, procurement_batch_id, outsourced_supply_batch_id,
                 inventory_object_kind, procurement_product_id, process_material_id,
                 sales_product_id, storage_location_id, raw_source_kind, supplier_id,
                 balance_quantity, row_version)
            VALUES
                (@outsourced_position_id, 'OUTSOURCED', NULL, @outsourced_batch_id,
                 'SALES_PRODUCT', NULL, NULL,
                 @sales_product_id, @location_id, NULL, NULL,
                 @outsourced_balance, 1),
                (@in_house_position_id, 'IN_HOUSE', @procurement_batch_id, NULL,
                 'SALES_PRODUCT', NULL, NULL,
                 @sales_product_id, @location_id, NULL, NULL,
                 @in_house_balance, 1);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("second_actor_id", scenario.SecondActorAccountId),
            ("warehouse_id", scenario.WarehouseId),
            ("location_id", scenario.StorageLocationId),
            ("second_location_id", scenario.SecondStorageLocationId),
            ("sales_product_group_id", scenario.SalesProductGroupId),
            ("sales_product_id", scenario.SalesProductId),
            ("procurement_product_id", scenario.ProcurementProductId),
            ("vendor_id", scenario.VendorId),
            ("customer_id", scenario.CustomerId),
            ("outsourced_batch_id", scenario.OutsourcedSupplyBatchId),
            ("procurement_batch_id", scenario.ProcurementBatchId),
            ("outsourced_position_id", scenario.OutsourcedPositionId),
            ("in_house_position_id", scenario.InHousePositionId),
            ("outsourced_balance", outsourcedBalance),
            ("in_house_balance", inHouseBalance),
            ("sales_id", scenario.SalesId),
            ("now", now));

        if (includeSalesDetail)
        {
            await ExecuteNonQueryAsync(
                """
                INSERT INTO sales.sales_details
                    (id, sales_id, line_number, sales_product_id, quantity,
                     pricing_basis_snapshot, sales_weight_snapshot, unit_price, amount_thb,
                     row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
                VALUES
                    (@detail_id, @sales_id, 1, @sales_product_id, @quantity,
                     'UNIT_BASED', NULL, 50, @amount_thb,
                     1, @now, @actor_id, NULL, NULL);
                """,
                cancellationToken,
                ("detail_id", scenario.SalesDetailId),
                ("sales_id", scenario.SalesId),
                ("sales_product_id", scenario.SalesProductId),
                ("quantity", salesQuantity),
                ("amount_thb", amountThb),
                ("now", now),
                ("actor_id", scenario.ActorAccountId));
        }

        if (addSecondOutsourcedLocation)
        {
            await ExecuteNonQueryAsync(
                """
                INSERT INTO inventory.inventory_positions
                    (id, origin, procurement_batch_id, outsourced_supply_batch_id,
                     inventory_object_kind, procurement_product_id, process_material_id,
                     sales_product_id, storage_location_id, raw_source_kind, supplier_id,
                     balance_quantity, row_version)
                VALUES
                    (@position_id, 'OUTSOURCED', NULL, @batch_id,
                     'SALES_PRODUCT', NULL, NULL,
                     @sales_product_id, @location_id, NULL, NULL,
                     2, 1);
                """,
                cancellationToken,
                ("position_id", scenario.SecondOutsourcedPositionId),
                ("batch_id", scenario.OutsourcedSupplyBatchId),
                ("sales_product_id", scenario.SalesProductId),
                ("location_id", scenario.SecondStorageLocationId));
        }

        return scenario;
    }

    private static async Task InsertSalesDetailAsync(
        Scenario scenario,
        Guid salesDetailId,
        int lineNumber,
        decimal quantity,
        CancellationToken cancellationToken)
    {
        var amountThb = checked((long)decimal.Floor(quantity * 50m));
        await ExecuteNonQueryAsync(
            """
            INSERT INTO sales.sales_details
                (id, sales_id, line_number, sales_product_id, quantity,
                 pricing_basis_snapshot, sales_weight_snapshot, unit_price, amount_thb,
                 row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@detail_id, @sales_id, @line_number, @sales_product_id, @quantity,
                 'UNIT_BASED', NULL, 50, @amount_thb,
                 1, @now, @actor_id, NULL, NULL);
            """,
            cancellationToken,
            ("detail_id", salesDetailId),
            ("sales_id", scenario.SalesId),
            ("line_number", lineNumber),
            ("sales_product_id", scenario.SalesProductId),
            ("quantity", quantity),
            ("amount_thb", amountThb),
            ("now", DateTimeOffset.UtcNow),
            ("actor_id", scenario.ActorAccountId));
    }

    private static async Task<AdditionalSale> SeedCompetingSaleAsync(
        Scenario scenario,
        decimal quantity,
        CancellationToken cancellationToken)
    {
        var additional = new AdditionalSale(Guid.CreateVersion7(), Guid.CreateVersion7());
        var amountThb = checked((long)decimal.Floor(quantity * 50m));

        await ExecuteNonQueryAsync(
            """
            INSERT INTO sales.sales
                (id, sales_date, customer_id, status, confirmed_at, confirmed_by_account_id,
                 row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@sales_id, DATE '2026-09-03', @customer_id, 'DRAFT', NULL, NULL,
                 1, @now, @actor_id, NULL, NULL);

            INSERT INTO sales.sales_details
                (id, sales_id, line_number, sales_product_id, quantity,
                 pricing_basis_snapshot, sales_weight_snapshot, unit_price, amount_thb,
                 row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@detail_id, @sales_id, 1, @sales_product_id, @quantity,
                 'UNIT_BASED', NULL, 50, @amount_thb,
                 1, @now, @actor_id, NULL, NULL);
            """,
            cancellationToken,
            ("sales_id", additional.SalesId),
            ("detail_id", additional.SalesDetailId),
            ("customer_id", scenario.CustomerId),
            ("sales_product_id", scenario.SalesProductId),
            ("quantity", quantity),
            ("amount_thb", amountThb),
            ("now", DateTimeOffset.UtcNow),
            ("actor_id", scenario.ActorAccountId));

        return additional;
    }

    private static async Task WaitForBlockedInventoryUpdatesAsync(
        long expectedCount,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var blocked = await ScalarAsync<long>(
                """
                SELECT count(*)
                FROM pg_stat_activity
                WHERE datname = current_database()
                  AND state = 'active'
                  AND wait_event_type = 'Lock'
                  AND query LIKE '%UPDATE inventory.inventory_positions%';
                """,
                cancellationToken);

            if (blocked >= expectedCount)
            {
                return;
            }

            await Task.Delay(25, cancellationToken);
        }

        Assert.Fail($"Expected at least {expectedCount} ConfirmSales inventory updates to be blocked concurrently.");
    }

    private static async Task CleanupAdditionalSaleAsync(
        AdditionalSale additional,
        CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            """
            DELETE FROM finance.receivable_outstanding_positions
            WHERE receivable_id IN (SELECT id FROM finance.receivables WHERE sales_id = @sales_id);
            DELETE FROM finance.receivable_obligation_items WHERE sales_id = @sales_id;
            DELETE FROM finance.receivables WHERE sales_id = @sales_id;

            DELETE FROM inventory.inventory_movements
            WHERE inventory_operation_id IN (SELECT id FROM inventory.inventory_operations WHERE sales_id = @sales_id);
            DELETE FROM inventory.inventory_operations WHERE sales_id = @sales_id;

            DELETE FROM sales.sales_allocations
            WHERE sales_detail_id IN (SELECT id FROM sales.sales_details WHERE sales_id = @sales_id);
            DELETE FROM sales.sales_allocation_revision_items WHERE sales_id = @sales_id;
            DELETE FROM sales.sales_allocation_revisions WHERE sales_id = @sales_id;
            DELETE FROM sales.sales_details WHERE sales_id = @sales_id;
            DELETE FROM sales.sales WHERE id = @sales_id;
            """,
            cancellationToken,
            ("sales_id", additional.SalesId));
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

            DELETE FROM finance.receivable_outstanding_positions
            WHERE receivable_id IN (SELECT id FROM finance.receivables WHERE sales_id = @sales_id);
            DELETE FROM finance.receivable_obligation_items WHERE sales_id = @sales_id;
            DELETE FROM finance.receivables WHERE sales_id = @sales_id;

            DELETE FROM inventory.inventory_movements
            WHERE inventory_operation_id IN (SELECT id FROM inventory.inventory_operations WHERE sales_id = @sales_id);
            DELETE FROM inventory.inventory_operations WHERE sales_id = @sales_id;

            DELETE FROM sales.sales_allocations
            WHERE sales_detail_id IN (SELECT id FROM sales.sales_details WHERE sales_id = @sales_id);
            DELETE FROM sales.sales_allocation_revision_items WHERE sales_id = @sales_id;
            DELETE FROM sales.sales_allocation_revisions WHERE sales_id = @sales_id;
            DELETE FROM sales.sales_details WHERE sales_id = @sales_id;
            DELETE FROM sales.sales WHERE id = @sales_id;

            DELETE FROM inventory.inventory_positions
            WHERE id = @outsourced_position_id
               OR id = @in_house_position_id
               OR id = @second_outsourced_position_id;
            DELETE FROM outsourced.outsourced_supply_batches WHERE id = @outsourced_batch_id;
            DELETE FROM procurement.procurement_batches WHERE id = @procurement_batch_id;

            DELETE FROM product.sales_products WHERE id = @sales_product_id;
            DELETE FROM product.sales_product_groups WHERE id = @sales_product_group_id;
            DELETE FROM product.procurement_products WHERE id = @procurement_product_id;
            DELETE FROM party.customers WHERE id = @customer_id;
            DELETE FROM party.outsourced_vendors WHERE id = @vendor_id;
            DELETE FROM infrastructure.storage_locations WHERE id = @location_id OR id = @second_location_id;
            DELETE FROM infrastructure.warehouses WHERE id = @warehouse_id;
            DELETE FROM system.accounts WHERE id = @actor_id OR id = @second_actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds.ToArray()),
            ("sales_id", scenario.SalesId),
            ("detail_id", scenario.SalesDetailId),
            ("outsourced_position_id", scenario.OutsourcedPositionId),
            ("in_house_position_id", scenario.InHousePositionId),
            ("second_outsourced_position_id", scenario.SecondOutsourcedPositionId),
            ("outsourced_batch_id", scenario.OutsourcedSupplyBatchId),
            ("procurement_batch_id", scenario.ProcurementBatchId),
            ("sales_product_id", scenario.SalesProductId),
            ("sales_product_group_id", scenario.SalesProductGroupId),
            ("procurement_product_id", scenario.ProcurementProductId),
            ("customer_id", scenario.CustomerId),
            ("vendor_id", scenario.VendorId),
            ("location_id", scenario.StorageLocationId),
            ("second_location_id", scenario.SecondStorageLocationId),
            ("warehouse_id", scenario.WarehouseId),
            ("actor_id", scenario.ActorAccountId),
            ("second_actor_id", scenario.SecondActorAccountId));
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

    private sealed record AdditionalSale(Guid SalesId, Guid SalesDetailId);

    private sealed record Scenario(
        Guid ActorAccountId,
        Guid SecondActorAccountId,
        Guid WarehouseId,
        Guid StorageLocationId,
        Guid SecondStorageLocationId,
        Guid SalesProductGroupId,
        Guid SalesProductId,
        Guid ProcurementProductId,
        Guid VendorId,
        Guid CustomerId,
        Guid OutsourcedSupplyBatchId,
        Guid ProcurementBatchId,
        Guid OutsourcedPositionId,
        Guid InHousePositionId,
        Guid SecondOutsourcedPositionId,
        Guid SalesId,
        Guid SalesDetailId);
}