using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Procurement;
using YowThi.Erp.Domain.Procurement;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class ConfirmProcurementEntryIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Supplier_farmer_replay_idempotency_and_company_pickup_paths_are_atomic()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(productHasDefaultLocation: true, cancellationToken);
        var supplierCommandId = Guid.CreateVersion7();
        var farmerCommandId = Guid.CreateVersion7();
        var commandIds = new[] { supplierCommandId, farmerCommandId };

        try
        {
            var supplierCommand = new ConfirmProcurementEntryCommand(
                scenario.ProcurementDate,
                scenario.ProductId,
                ProcurementSourceType.SUPPLIER,
                scenario.SupplierId,
                null,
                125.5m,
                18.25m,
                false);

            var supplierExecution = Execution(
                supplierCommandId,
                scenario.ActorAccountId,
                Hash(1),
                supplierCommand);

            var supplierResult = await ExecuteAsync(supplierExecution, cancellationToken);
            Assert.True(supplierResult.IsSuccess);
            Assert.Equal(2290L, supplierResult.Value.AmountThb);
            Assert.Equal(scenario.DefaultLocationId, supplierResult.Value.ReceiptStorageLocationId);
            Assert.Null(supplierResult.Value.CompanyPickupTransportBasisId);
            Assert.Equal(1L, supplierResult.Value.ProcurementEntryRowVersion);

            var replay = await ExecuteAsync(supplierExecution, cancellationToken);
            Assert.True(replay.IsSuccess);
            Assert.Equal(supplierResult.Value, replay.Value);

            var changedHashResult = await ExecuteAsync(
                Execution(supplierCommandId, scenario.ActorAccountId, Hash(2), supplierCommand),
                cancellationToken);
            Assert.True(changedHashResult.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, changedHashResult.Error.Kind);
            Assert.Equal(ProcurementApplicationErrorCodes.IdempotencyKeyReused, changedHashResult.Error.Code);

            var changedActorResult = await ExecuteAsync(
                Execution(supplierCommandId, scenario.SecondActorAccountId, Hash(1), supplierCommand),
                cancellationToken);
            Assert.True(changedActorResult.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, changedActorResult.Error.Kind);
            Assert.Equal(ProcurementApplicationErrorCodes.IdempotencyKeyReused, changedActorResult.Error.Code);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM procurement.procurement_entries WHERE id = @id;",
                    cancellationToken,
                    ("id", supplierResult.Value.ProcurementEntryId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM inventory.inventory_operations WHERE id = @id AND operation_type = 'PROCUREMENT_RECEIPT';",
                    cancellationToken,
                    ("id", supplierResult.Value.InventoryOperationId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM inventory.inventory_movements m
                    JOIN inventory.inventory_operations o ON o.id = m.inventory_operation_id
                    WHERE o.id = @operation_id
                      AND m.movement_type = 'PURCHASE_RECEIPT'
                      AND m.origin = 'IN_HOUSE'
                      AND m.procurement_batch_id = @batch_id
                      AND m.procurement_product_id = @product_id
                      AND m.storage_location_id = @location_id
                      AND m.raw_source_kind = 'SUPPLIER'
                      AND m.supplier_id = @supplier_id
                      AND m.quantity_delta = 125.5;
                    """,
                    cancellationToken,
                    ("operation_id", supplierResult.Value.InventoryOperationId),
                    ("batch_id", supplierResult.Value.ProcurementBatchId),
                    ("product_id", scenario.ProductId),
                    ("location_id", scenario.DefaultLocationId),
                    ("supplier_id", scenario.SupplierId)));
            Assert.Equal(
                125.5m,
                await ScalarAsync<decimal>(
                    """
                    SELECT balance_quantity
                    FROM inventory.inventory_positions
                    WHERE procurement_batch_id = @batch_id
                      AND procurement_product_id = @product_id
                      AND storage_location_id = @location_id
                      AND raw_source_kind = 'SUPPLIER'
                      AND supplier_id = @supplier_id;
                    """,
                    cancellationToken,
                    ("batch_id", supplierResult.Value.ProcurementBatchId),
                    ("product_id", scenario.ProductId),
                    ("location_id", scenario.DefaultLocationId),
                    ("supplier_id", scenario.SupplierId)));
            Assert.Equal(
                "PROCUREMENT_SUPPLIER",
                await ScalarAsync<string>(
                    "SELECT payable_kind FROM finance.payables WHERE id = @id;",
                    cancellationToken,
                    ("id", supplierResult.Value.PayableId)));
            Assert.Equal(
                2290L,
                await ScalarAsync<long>(
                    "SELECT amount_thb FROM finance.payable_obligation_items WHERE procurement_entry_id = @entry_id;",
                    cancellationToken,
                    ("entry_id", supplierResult.Value.ProcurementEntryId)));
            Assert.Equal(
                2290L,
                await ScalarAsync<long>(
                    "SELECT outstanding_thb FROM finance.payable_outstanding_positions WHERE payable_id = @payable_id;",
                    cancellationToken,
                    ("payable_id", supplierResult.Value.PayableId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id AND status = 'SUCCEEDED';",
                    cancellationToken,
                    ("command_id", supplierCommandId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = @command_id AND command_type = 'ConfirmProcurementEntry';",
                    cancellationToken,
                    ("command_id", supplierCommandId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.outbox_messages WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", supplierCommandId)));

            await ExecuteNonQueryAsync(
                "UPDATE product.procurement_products SET default_storage_location_id = @location_id WHERE id = @product_id;",
                cancellationToken,
                ("location_id", scenario.ExplicitLocationId),
                ("product_id", scenario.ProductId));

            var farmerCommand = new ConfirmProcurementEntryCommand(
                scenario.ProcurementDate,
                scenario.ProductId,
                ProcurementSourceType.FARMER,
                null,
                scenario.FarmerId,
                20m,
                7.5m,
                true);

            var farmerResult = await ExecuteAsync(
                Execution(farmerCommandId, scenario.ActorAccountId, Hash(3), farmerCommand),
                cancellationToken);

            Assert.True(farmerResult.IsSuccess);
            Assert.Equal(supplierResult.Value.ProcurementBatchId, farmerResult.Value.ProcurementBatchId);
            Assert.Equal(scenario.DefaultLocationId, farmerResult.Value.ReceiptStorageLocationId);
            Assert.Equal(150L, farmerResult.Value.AmountThb);
            Assert.NotNull(farmerResult.Value.CompanyPickupTransportBasisId);
            Assert.NotEqual(supplierResult.Value.PayableId, farmerResult.Value.PayableId);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM inventory.inventory_movements m
                    JOIN inventory.inventory_operations o ON o.id = m.inventory_operation_id
                    WHERE o.id = @operation_id
                      AND m.raw_source_kind = 'FARMERS_COMBINED'
                      AND m.supplier_id IS NULL
                      AND m.storage_location_id = @location_id
                      AND m.quantity_delta = 20;
                    """,
                    cancellationToken,
                    ("operation_id", farmerResult.Value.InventoryOperationId),
                    ("location_id", scenario.DefaultLocationId)));
            Assert.Equal(
                "PROCUREMENT_FARMER",
                await ScalarAsync<string>(
                    "SELECT payable_kind FROM finance.payables WHERE id = @id;",
                    cancellationToken,
                    ("id", farmerResult.Value.PayableId)));
            Assert.Equal(
                20m,
                await ScalarAsync<decimal>(
                    "SELECT applicable_quantity FROM finance.company_pickup_transport_bases WHERE id = @id;",
                    cancellationToken,
                    ("id", farmerResult.Value.CompanyPickupTransportBasisId!.Value)));
            Assert.Equal(
                2L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM finance.payables
                    WHERE procurement_batch_id = @batch_id
                      AND payable_kind IN ('PROCUREMENT_SUPPLIER', 'PROCUREMENT_FARMER');
                    """,
                    cancellationToken,
                    ("batch_id", supplierResult.Value.ProcurementBatchId)));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, commandIds, cancellationToken);
        }
    }

    [Fact]
    public async Task Procurement_workspace_reader_returns_one_batch_with_supplier_and_farmer_details()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(productHasDefaultLocation: true, cancellationToken);
        var supplierCommandId = Guid.CreateVersion7();
        var farmerCommandId = Guid.CreateVersion7();
        var commandIds = new[] { supplierCommandId, farmerCommandId };

        try
        {
            var supplierResult = await ExecuteAsync(
                Execution(
                    supplierCommandId,
                    scenario.ActorAccountId,
                    Hash(10),
                    new ConfirmProcurementEntryCommand(
                        scenario.ProcurementDate,
                        scenario.ProductId,
                        ProcurementSourceType.SUPPLIER,
                        scenario.SupplierId,
                        null,
                        12.5m,
                        10m,
                        false)),
                cancellationToken);
            await ExecuteNonQueryAsync(
                "UPDATE product.procurement_products SET default_storage_location_id = @location_id WHERE id = @product_id;",
                cancellationToken,
                ("location_id", scenario.ExplicitLocationId),
                ("product_id", scenario.ProductId));

            var farmerResult = await ExecuteAsync(
                Execution(
                    farmerCommandId,
                    scenario.ActorAccountId,
                    Hash(11),
                    new ConfirmProcurementEntryCommand(
                        scenario.ProcurementDate,
                        scenario.ProductId,
                        ProcurementSourceType.FARMER,
                        null,
                        scenario.FarmerId,
                        4m,
                        8m,
                        true)),
                cancellationToken);

            Assert.True(supplierResult.IsSuccess);
            Assert.True(farmerResult.IsSuccess);
            Assert.Equal(supplierResult.Value.ProcurementBatchId, farmerResult.Value.ProcurementBatchId);

            var services = new ServiceCollection();
            services.AddErpPersistence(GetConnectionString());
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var reader = scope.ServiceProvider.GetRequiredService<IProcurementWorkspaceReader>();

            var page = await reader.GetBatchesAsync(
                new ProcurementBatchListQuery("zh-TW", "P6 product", 0, 100),
                cancellationToken);
            Assert.Contains(page.Items, item => item.Id == supplierResult.Value.ProcurementBatchId);

            var workspace = await reader.GetBatchAsync(
                supplierResult.Value.ProcurementBatchId,
                "zh-TW",
                cancellationToken);

            Assert.NotNull(workspace);
            Assert.Equal(scenario.ProcurementDate, workspace.ProcurementDate);
            Assert.Equal(scenario.ProductId, workspace.ProcurementProductId);
            Assert.Equal("P6 product", workspace.ProcurementProductDisplayName);
            Assert.Equal("kg", workspace.UnitCode);
            Assert.Equal(scenario.DefaultLocationId, workspace.ReceiptStorageLocationId);
            Assert.Equal(scenario.WarehouseId, workspace.WarehouseId);
            Assert.Equal("P6 warehouse", workspace.WarehouseDisplayName);
            Assert.Equal("OPEN", workspace.ProcurementStatus);
            Assert.Equal("ACTIVE", workspace.LifecycleStatus);
            Assert.Equal(2, workspace.Entries.Count);

            var supplierEntry = Assert.Single(
                workspace.Entries,
                item => item.Id == supplierResult.Value.ProcurementEntryId);
            Assert.Equal("SUPPLIER", supplierEntry.SourceType);
            Assert.Equal(scenario.SupplierId, supplierEntry.SourceId);
            Assert.Equal("P6 supplier", supplierEntry.SourceDisplayName);
            Assert.Equal(12.5m, supplierEntry.NetQuantity);

            var farmerEntry = Assert.Single(
                workspace.Entries,
                item => item.Id == farmerResult.Value.ProcurementEntryId);
            Assert.Equal("FARMER", farmerEntry.SourceType);
            Assert.Equal(scenario.FarmerId, farmerEntry.SourceId);
            Assert.Equal("P6 farmer", farmerEntry.SourceDisplayName);
            Assert.Equal(4m, farmerEntry.NetQuantity);

            Assert.Null(await reader.GetBatchAsync(Guid.CreateVersion7(), "zh-TW", cancellationToken));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, commandIds, cancellationToken);
        }
    }

    [Fact]
    public async Task Missing_default_receipt_location_blocks_new_batch_without_persisting_command()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(productHasDefaultLocation: false, cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            var command = new ConfirmProcurementEntryCommand(
                scenario.ProcurementDate,
                scenario.ProductId,
                ProcurementSourceType.SUPPLIER,
                scenario.SupplierId,
                null,
                10m,
                4m,
                false);

            var result = await ExecuteAsync(
                Execution(commandId, scenario.ActorAccountId, Hash(4), command),
                cancellationToken);

            Assert.True(result.IsFailure);
            Assert.Equal(ApplicationErrorKind.Validation, result.Error.Kind);
            Assert.Equal(ProcurementApplicationErrorCodes.ReceiptLocationRequired, result.Error.Code);
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", commandId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM procurement.procurement_batches
                    WHERE procurement_product_id = @product_id;
                    """,
                    cancellationToken,
                    ("product_id", scenario.ProductId)));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Completed_procurement_batch_blocks_normal_late_entry_without_new_facts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(productHasDefaultLocation: true, cancellationToken);
        var commandId = Guid.CreateVersion7();
        var batchId = Guid.CreateVersion7();

        try
        {
            await InsertBatchAsync(
                batchId,
                scenario,
                procurementStatus: "COMPLETED",
                completed: true,
                cancellationToken);

            var command = new ConfirmProcurementEntryCommand(
                scenario.ProcurementDate,
                scenario.ProductId,
                ProcurementSourceType.SUPPLIER,
                scenario.SupplierId,
                null,
                10m,
                4m,
                false);

            var result = await ExecuteAsync(
                Execution(commandId, scenario.ActorAccountId, Hash(5), command),
                cancellationToken);

            Assert.True(result.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, result.Error.Kind);
            Assert.Equal(ProcurementApplicationErrorCodes.BatchCompleted, result.Error.Code);
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM procurement.procurement_entries WHERE procurement_batch_id = @batch_id;",
                    cancellationToken,
                    ("batch_id", batchId)));
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
    public async Task Concurrent_same_date_product_commands_resolve_one_procurement_batch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(productHasDefaultLocation: true, cancellationToken);
        var firstCommandId = Guid.CreateVersion7();
        var secondCommandId = Guid.CreateVersion7();

        try
        {
            var firstCommand = new ConfirmProcurementEntryCommand(
                scenario.ProcurementDate,
                scenario.ProductId,
                ProcurementSourceType.SUPPLIER,
                scenario.SupplierId,
                null,
                11m,
                2m,
                false);
            var secondCommand = new ConfirmProcurementEntryCommand(
                scenario.ProcurementDate,
                scenario.ProductId,
                ProcurementSourceType.SUPPLIER,
                scenario.SecondSupplierId,
                null,
                13m,
                3m,
                false);

            var results = await Task.WhenAll(
                ExecuteAsync(Execution(firstCommandId, scenario.ActorAccountId, Hash(6), firstCommand), cancellationToken).AsTask(),
                ExecuteAsync(Execution(secondCommandId, scenario.ActorAccountId, Hash(7), secondCommand), cancellationToken).AsTask());

            Assert.All(results, result => Assert.True(result.IsSuccess));
            Assert.Equal(results[0].Value.ProcurementBatchId, results[1].Value.ProcurementBatchId);
            Assert.All(results, result => Assert.Equal(scenario.DefaultLocationId, result.Value.ReceiptStorageLocationId));
            Assert.Equal(
                scenario.DefaultLocationId,
                await ScalarAsync<Guid>(
                    "SELECT receipt_storage_location_id FROM procurement.procurement_batches WHERE id = @batch_id;",
                    cancellationToken,
                    ("batch_id", results[0].Value.ProcurementBatchId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM procurement.procurement_batches
                    WHERE procurement_date = @procurement_date
                      AND procurement_product_id = @product_id;
                    """,
                    cancellationToken,
                    ("procurement_date", scenario.ProcurementDate),
                    ("product_id", scenario.ProductId)));
            Assert.Equal(
                2L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM procurement.procurement_entries WHERE procurement_batch_id = @batch_id;",
                    cancellationToken,
                    ("batch_id", results[0].Value.ProcurementBatchId)));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, new[] { firstCommandId, secondCommandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Late_outstanding_overflow_rolls_back_procurement_inventory_finance_audit_outbox_and_command_execution()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(productHasDefaultLocation: true, cancellationToken);
        var commandId = Guid.CreateVersion7();
        var batchId = Guid.CreateVersion7();
        var payableId = Guid.CreateVersion7();

        try
        {
            await InsertBatchAsync(batchId, scenario, "OPEN", completed: false, cancellationToken);
            await ExecuteNonQueryAsync(
                """
                INSERT INTO finance.payables
                    (id, payable_kind, procurement_batch_id, supplier_id, farmer_id,
                     outsourced_supply_detail_id, employee_daily_wage_id, created_at)
                VALUES
                    (@payable_id, 'PROCUREMENT_SUPPLIER', @batch_id, @supplier_id, NULL, NULL, NULL, @now);

                INSERT INTO finance.payable_outstanding_positions
                    (payable_id, original_obligation_thb, adjustment_total_thb, settlement_total_thb,
                     outstanding_thb, row_version, updated_at)
                VALUES
                    (@payable_id, @max_value, 0, 0, @max_value, 1, @now);
                """,
                cancellationToken,
                ("payable_id", payableId),
                ("batch_id", batchId),
                ("supplier_id", scenario.SupplierId),
                ("max_value", long.MaxValue),
                ("now", DateTimeOffset.UtcNow));

            var command = new ConfirmProcurementEntryCommand(
                scenario.ProcurementDate,
                scenario.ProductId,
                ProcurementSourceType.SUPPLIER,
                scenario.SupplierId,
                null,
                1m,
                1m,
                false);

            var exception = await Assert.ThrowsAsync<PostgresException>(async () =>
                await ExecuteAsync(
                    Execution(commandId, scenario.ActorAccountId, Hash(8), command),
                    cancellationToken));

            Assert.Equal(PostgresErrorCodes.NumericValueOutOfRange, exception.SqlState);
            Assert.Equal(
                long.MaxValue,
                await ScalarAsync<long>(
                    "SELECT outstanding_thb FROM finance.payable_outstanding_positions WHERE payable_id = @payable_id;",
                    cancellationToken,
                    ("payable_id", payableId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM procurement.procurement_entries WHERE procurement_batch_id = @batch_id;",
                    cancellationToken,
                    ("batch_id", batchId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM inventory.inventory_operations WHERE procurement_entry_id IS NOT NULL AND procurement_entry_id IN (SELECT id FROM procurement.procurement_entries WHERE procurement_batch_id = @batch_id);",
                    cancellationToken,
                    ("batch_id", batchId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM finance.payable_obligation_items WHERE payable_id = @payable_id;",
                    cancellationToken,
                    ("payable_id", payableId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", commandId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.outbox_messages WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", commandId)));
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
    public async Task Zero_net_quantity_is_blocked_as_to_verify_without_database_work()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var commandId = Guid.CreateVersion7();
        var accountId = Guid.CreateVersion7();
        var supplierId = Guid.CreateVersion7();
        var command = new ConfirmProcurementEntryCommand(
            new DateOnly(2026, 9, 2),
            Guid.CreateVersion7(),
            ProcurementSourceType.SUPPLIER,
            supplierId,
            null,
            0m,
            1m,
            false);

        var result = await ExecuteAsync(
            Execution(commandId, accountId, Hash(9), command),
            cancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(ApplicationErrorKind.Validation, result.Error.Kind);
        Assert.Equal(ProcurementApplicationErrorCodes.ZeroNetQuantityUnverified, result.Error.Code);
    }

    private static ConfirmProcurementEntryExecution Execution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        ConfirmProcurementEntryCommand command) =>
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

    private static async ValueTask<ApplicationResult<ConfirmProcurementEntryResult>> ExecuteAsync(
        ConfirmProcurementEntryExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<IConfirmProcurementEntryExecutor>();
        return await executor.ExecuteAsync(execution, cancellationToken);
    }

    private static async Task<Scenario> SeedScenarioAsync(
        bool productHasDefaultLocation,
        CancellationToken cancellationToken)
    {
        var scenario = new Scenario(
            ActorAccountId: Guid.CreateVersion7(),
            SecondActorAccountId: Guid.CreateVersion7(),
            WarehouseId: Guid.CreateVersion7(),
            DefaultLocationId: Guid.CreateVersion7(),
            ExplicitLocationId: Guid.CreateVersion7(),
            ProductId: Guid.CreateVersion7(),
            SupplierId: Guid.CreateVersion7(),
            SecondSupplierId: Guid.CreateVersion7(),
            FarmerId: Guid.CreateVersion7(),
            ProcurementDate: new DateOnly(2026, 9, 2));

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 actor', true, NULL, NULL, @now),
                (@second_actor_id, 'P6 second actor', true, NULL, NULL, @now);

            INSERT INTO infrastructure.warehouses
                (id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@warehouse_id, NULL, 'P6 warehouse', NULL, true, @now, @actor_id);

            INSERT INTO infrastructure.storage_locations
                (id, warehouse_id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@default_location_id, @warehouse_id, NULL, 'P6 default location', NULL, true, @now, @actor_id),
                (@explicit_location_id, @warehouse_id, NULL, 'P6 explicit location', NULL, true, @now, @actor_id);

            INSERT INTO product.procurement_products
                (id, name_zh_tw, name_th_th, unit_code, default_storage_location_id,
                 active, created_at, created_by_account_id)
            VALUES
                (@product_id, 'P6 product', NULL, 'kg', @default_location_id,
                 true, @now, @actor_id);

            INSERT INTO party.suppliers
                (id, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@supplier_id, 'P6 supplier', NULL, true, @now, @actor_id),
                (@second_supplier_id, 'P6 supplier 2', NULL, true, @now, @actor_id);

            INSERT INTO party.farmers
                (id, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@farmer_id, 'P6 farmer', NULL, true, @now, @actor_id);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("second_actor_id", scenario.SecondActorAccountId),
            ("warehouse_id", scenario.WarehouseId),
            ("default_location_id", scenario.DefaultLocationId),
            ("explicit_location_id", scenario.ExplicitLocationId),
            ("product_id", scenario.ProductId),
            ("supplier_id", scenario.SupplierId),
            ("second_supplier_id", scenario.SecondSupplierId),
            ("farmer_id", scenario.FarmerId),
            ("now", DateTimeOffset.UtcNow));

        if (!productHasDefaultLocation)
        {
            await ExecuteNonQueryAsync(
                "UPDATE product.procurement_products SET default_storage_location_id = NULL WHERE id = @product_id;",
                cancellationToken,
                ("product_id", scenario.ProductId));
        }

        return scenario;
    }

    private static async Task InsertBatchAsync(
        Guid batchId,
        Scenario scenario,
        string procurementStatus,
        bool completed,
        CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            """
            INSERT INTO procurement.procurement_batches
                (id, procurement_date, procurement_product_id, procurement_status, lifecycle_status,
                 processing_route_id, processing_route_version_id, completed_at, completed_by_account_id,
                 closed_at, closed_by_account_id, created_at, created_by_account_id)
            VALUES
                (@batch_id, @procurement_date, @product_id, @procurement_status, 'ACTIVE',
                 NULL, NULL, @completed_at, @completed_by, NULL, NULL, @now, @actor_id);
            """,
            cancellationToken,
            ("batch_id", batchId),
            ("procurement_date", scenario.ProcurementDate),
            ("product_id", scenario.ProductId),
            ("procurement_status", procurementStatus),
            ("completed_at", completed ? DateTimeOffset.UtcNow : DBNull.Value),
            ("completed_by", completed ? scenario.ActorAccountId : DBNull.Value),
            ("now", DateTimeOffset.UtcNow),
            ("actor_id", scenario.ActorAccountId));
    }

    private static async Task CleanupScenarioAsync(
        Scenario scenario,
        IReadOnlyCollection<Guid> commandIds,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            DELETE FROM audit.audit_event_subjects
            WHERE audit_event_id IN (SELECT id FROM audit.audit_events WHERE command_id = ANY(@command_ids));
            DELETE FROM audit.audit_events WHERE command_id = ANY(@command_ids);
            DELETE FROM system.outbox_messages WHERE command_id = ANY(@command_ids);
            DELETE FROM system.command_executions WHERE command_id = ANY(@command_ids);

            DELETE FROM finance.company_pickup_transport_bases
            WHERE procurement_entry_id IN (
                SELECT e.id
                FROM procurement.procurement_entries e
                JOIN procurement.procurement_batches b ON b.id = e.procurement_batch_id
                WHERE b.procurement_product_id = @product_id);

            DELETE FROM finance.payable_obligation_items
            WHERE procurement_entry_id IN (
                SELECT e.id
                FROM procurement.procurement_entries e
                JOIN procurement.procurement_batches b ON b.id = e.procurement_batch_id
                WHERE b.procurement_product_id = @product_id);

            DELETE FROM finance.payable_outstanding_positions
            WHERE payable_id IN (
                SELECT p.id
                FROM finance.payables p
                JOIN procurement.procurement_batches b ON b.id = p.procurement_batch_id
                WHERE b.procurement_product_id = @product_id);

            DELETE FROM finance.payables
            WHERE procurement_batch_id IN (
                SELECT id FROM procurement.procurement_batches WHERE procurement_product_id = @product_id);

            DELETE FROM inventory.inventory_movements
            WHERE inventory_operation_id IN (
                SELECT o.id
                FROM inventory.inventory_operations o
                JOIN procurement.procurement_entries e ON e.id = o.procurement_entry_id
                JOIN procurement.procurement_batches b ON b.id = e.procurement_batch_id
                WHERE b.procurement_product_id = @product_id);

            DELETE FROM inventory.inventory_operations
            WHERE procurement_entry_id IN (
                SELECT e.id
                FROM procurement.procurement_entries e
                JOIN procurement.procurement_batches b ON b.id = e.procurement_batch_id
                WHERE b.procurement_product_id = @product_id);

            DELETE FROM inventory.inventory_positions
            WHERE procurement_batch_id IN (
                SELECT id FROM procurement.procurement_batches WHERE procurement_product_id = @product_id);

            DELETE FROM procurement.procurement_entries
            WHERE procurement_batch_id IN (
                SELECT id FROM procurement.procurement_batches WHERE procurement_product_id = @product_id);

            DELETE FROM procurement.procurement_batches WHERE procurement_product_id = @product_id;
            DELETE FROM product.procurement_products WHERE id = @product_id;
            DELETE FROM infrastructure.storage_locations WHERE id = @default_location_id OR id = @explicit_location_id;
            DELETE FROM infrastructure.warehouses WHERE id = @warehouse_id;
            DELETE FROM party.suppliers WHERE id = @supplier_id OR id = @second_supplier_id;
            DELETE FROM party.farmers WHERE id = @farmer_id;
            DELETE FROM system.accounts WHERE id = @actor_id OR id = @second_actor_id;
            """,
            connection);

        command.Parameters.AddWithValue("command_ids", commandIds.ToArray());
        command.Parameters.AddWithValue("product_id", scenario.ProductId);
        command.Parameters.AddWithValue("default_location_id", scenario.DefaultLocationId);
        command.Parameters.AddWithValue("explicit_location_id", scenario.ExplicitLocationId);
        command.Parameters.AddWithValue("warehouse_id", scenario.WarehouseId);
        command.Parameters.AddWithValue("supplier_id", scenario.SupplierId);
        command.Parameters.AddWithValue("second_supplier_id", scenario.SecondSupplierId);
        command.Parameters.AddWithValue("farmer_id", scenario.FarmerId);
        command.Parameters.AddWithValue("actor_id", scenario.ActorAccountId);
        command.Parameters.AddWithValue("second_actor_id", scenario.SecondActorAccountId);
        await command.ExecuteNonQueryAsync(cancellationToken);
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
        Guid SecondActorAccountId,
        Guid WarehouseId,
        Guid DefaultLocationId,
        Guid ExplicitLocationId,
        Guid ProductId,
        Guid SupplierId,
        Guid SecondSupplierId,
        Guid FarmerId,
        DateOnly ProcurementDate);
}
