using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Inventory;
using YowThi.Erp.Application.Outsourced;
using YowThi.Erp.Application.Procurement;
using YowThi.Erp.Domain.Inventory;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class BatchCloseCommandIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Procurement_close_reconciles_residual_inventory_replays_and_blocks_post_close_adjustment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedProcurementAsync(3m, 0m, cancellationToken);
        var closeCommandId = Guid.CreateVersion7();
        var staleCommandId = Guid.CreateVersion7();
        var adjustmentCommandId = Guid.CreateVersion7();

        try
        {
            var expectedRowVersion = await ScalarAsync<long>(
                "SELECT row_version FROM procurement.procurement_batches WHERE id = @batch_id;",
                cancellationToken,
                ("batch_id", scenario.BatchId));
            var command = new CloseProcurementBatchCommand(scenario.BatchId, expectedRowVersion);
            var execution = ProcurementCloseExecution(closeCommandId, scenario.ActorAccountId, Hash(1), command);

            var result = await ExecuteProcurementCloseAsync(execution, cancellationToken);
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value.InventoryOperationId);
            Assert.Equal(1, result.Value.ReconciledPositionCount);
            Assert.Equal(expectedRowVersion + 1, result.Value.ClosedRowVersion);

            var replay = await ExecuteProcurementCloseAsync(execution, cancellationToken);
            Assert.True(replay.IsSuccess);
            Assert.Equal(result.Value, replay.Value);

            var changedHash = await ExecuteProcurementCloseAsync(
                ProcurementCloseExecution(closeCommandId, scenario.ActorAccountId, Hash(2), command),
                cancellationToken);
            Assert.True(changedHash.IsFailure);
            Assert.Equal(ProcurementBatchCloseErrorCodes.IdempotencyKeyReused, changedHash.Error.Code);

            Assert.Equal(
                "CLOSED",
                await ScalarAsync<string>(
                    "SELECT lifecycle_status FROM procurement.procurement_batches WHERE id = @batch_id;",
                    cancellationToken,
                    ("batch_id", scenario.BatchId)));
            Assert.Equal(
                0m,
                await ScalarAsync<decimal>(
                    "SELECT balance_quantity FROM inventory.inventory_positions WHERE id = @position_id;",
                    cancellationToken,
                    ("position_id", scenario.ResidualPositionId)));
            Assert.Equal(
                -3m,
                await ScalarAsync<decimal>(
                    """
                    SELECT quantity_delta
                    FROM inventory.inventory_movements
                    WHERE inventory_operation_id = @operation_id
                      AND movement_type = 'BATCH_RECONCILIATION';
                    """,
                    cancellationToken,
                    ("operation_id", result.Value.InventoryOperationId!.Value)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = @command_id AND command_type = 'CloseProcurementBatch';",
                    cancellationToken,
                    ("command_id", closeCommandId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.outbox_messages WHERE command_id = @command_id AND message_type = 'procurement.batch.closed';",
                    cancellationToken,
                    ("command_id", closeCommandId)));

            var stale = await ExecuteProcurementCloseAsync(
                ProcurementCloseExecution(
                    staleCommandId,
                    scenario.ActorAccountId,
                    Hash(3),
                    new CloseProcurementBatchCommand(scenario.BatchId, expectedRowVersion)),
                cancellationToken);
            Assert.True(stale.IsFailure);
            Assert.Equal(ProcurementBatchCloseErrorCodes.ConcurrentChange, stale.Error.Code);

            var adjustment = await ExecuteAdjustmentAsync(
                new AdjustInventoryExecution(
                    CommandId.From(adjustmentCommandId),
                    Hash(4),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new AdjustInventoryCommand(
                        ProcurementIdentity(scenario),
                        scenario.LocationId,
                        1m,
                        "post-close acceptance probe")),
                cancellationToken);
            Assert.True(adjustment.IsFailure);
            Assert.Equal(InventoryApplicationErrorCodes.BatchClosed, adjustment.Error.Code);
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", adjustmentCommandId)));
        }
        finally
        {
            await CleanupProcurementAsync(
                scenario,
                new[] { closeCommandId, staleCommandId, adjustmentCommandId },
                cancellationToken);
        }
    }

    [Fact]
    public async Task Procurement_close_blocks_while_sellable_inventory_remains_without_reconciliation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedProcurementAsync(3m, 2m, cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            var rowVersion = await ScalarAsync<long>(
                "SELECT row_version FROM procurement.procurement_batches WHERE id = @batch_id;",
                cancellationToken,
                ("batch_id", scenario.BatchId));
            var result = await ExecuteProcurementCloseAsync(
                ProcurementCloseExecution(
                    commandId,
                    scenario.ActorAccountId,
                    Hash(5),
                    new CloseProcurementBatchCommand(scenario.BatchId, rowVersion)),
                cancellationToken);

            Assert.True(result.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, result.Error.Kind);
            Assert.Equal(ProcurementBatchCloseErrorCodes.SellableInventoryRemaining, result.Error.Code);
            Assert.Equal(
                "ACTIVE",
                await ScalarAsync<string>(
                    "SELECT lifecycle_status FROM procurement.procurement_batches WHERE id = @batch_id;",
                    cancellationToken,
                    ("batch_id", scenario.BatchId)));
            Assert.Equal(
                3m,
                await ScalarAsync<decimal>(
                    "SELECT balance_quantity FROM inventory.inventory_positions WHERE id = @position_id;",
                    cancellationToken,
                    ("position_id", scenario.ResidualPositionId)));
            Assert.Equal(
                2m,
                await ScalarAsync<decimal>(
                    "SELECT balance_quantity FROM inventory.inventory_positions WHERE id = @position_id;",
                    cancellationToken,
                    ("position_id", scenario.SellablePositionId!.Value)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM inventory.inventory_operations WHERE procurement_batch_id = @batch_id AND operation_type = 'BATCH_RECONCILIATION';",
                    cancellationToken,
                    ("batch_id", scenario.BatchId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", commandId)));
        }
        finally
        {
            await CleanupProcurementAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Outsourced_close_enforces_sellable_gate_without_reconciliation_and_replays()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var success = await SeedOutsourcedAsync(0m, cancellationToken);
        var closeCommandId = Guid.CreateVersion7();
        var staleCommandId = Guid.CreateVersion7();

        try
        {
            var expectedRowVersion = await ScalarAsync<long>(
                "SELECT row_version FROM outsourced.outsourced_supply_batches WHERE id = @batch_id;",
                cancellationToken,
                ("batch_id", success.BatchId));
            var command = new CloseOutsourcedSupplyBatchCommand(success.BatchId, expectedRowVersion);
            var execution = OutsourcedCloseExecution(closeCommandId, success.ActorAccountId, Hash(6), command);

            var result = await ExecuteOutsourcedCloseAsync(execution, cancellationToken);
            Assert.True(result.IsSuccess);
            Assert.Equal(expectedRowVersion + 1, result.Value.ClosedRowVersion);

            var replay = await ExecuteOutsourcedCloseAsync(execution, cancellationToken);
            Assert.True(replay.IsSuccess);
            Assert.Equal(result.Value, replay.Value);

            var changedHash = await ExecuteOutsourcedCloseAsync(
                OutsourcedCloseExecution(closeCommandId, success.ActorAccountId, Hash(7), command),
                cancellationToken);
            Assert.True(changedHash.IsFailure);
            Assert.Equal(OutsourcedBatchCloseErrorCodes.IdempotencyKeyReused, changedHash.Error.Code);

            Assert.Equal(
                "CLOSED",
                await ScalarAsync<string>(
                    "SELECT lifecycle_status FROM outsourced.outsourced_supply_batches WHERE id = @batch_id;",
                    cancellationToken,
                    ("batch_id", success.BatchId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM inventory.inventory_operations WHERE recorded_by_account_id = @actor_id AND operation_type = 'BATCH_RECONCILIATION';",
                    cancellationToken,
                    ("actor_id", success.ActorAccountId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = @command_id AND command_type = 'CloseOutsourcedSupplyBatch';",
                    cancellationToken,
                    ("command_id", closeCommandId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.outbox_messages WHERE command_id = @command_id AND message_type = 'outsourced.batch.closed';",
                    cancellationToken,
                    ("command_id", closeCommandId)));

            var stale = await ExecuteOutsourcedCloseAsync(
                OutsourcedCloseExecution(
                    staleCommandId,
                    success.ActorAccountId,
                    Hash(8),
                    new CloseOutsourcedSupplyBatchCommand(success.BatchId, expectedRowVersion)),
                cancellationToken);
            Assert.True(stale.IsFailure);
            Assert.Equal(OutsourcedBatchCloseErrorCodes.ConcurrentChange, stale.Error.Code);
        }
        finally
        {
            await CleanupOutsourcedAsync(success, new[] { closeCommandId, staleCommandId }, cancellationToken);
        }

        var blocked = await SeedOutsourcedAsync(2m, cancellationToken);
        var blockedCommandId = Guid.CreateVersion7();
        try
        {
            var rowVersion = await ScalarAsync<long>(
                "SELECT row_version FROM outsourced.outsourced_supply_batches WHERE id = @batch_id;",
                cancellationToken,
                ("batch_id", blocked.BatchId));
            var result = await ExecuteOutsourcedCloseAsync(
                OutsourcedCloseExecution(
                    blockedCommandId,
                    blocked.ActorAccountId,
                    Hash(9),
                    new CloseOutsourcedSupplyBatchCommand(blocked.BatchId, rowVersion)),
                cancellationToken);

            Assert.True(result.IsFailure);
            Assert.Equal(OutsourcedBatchCloseErrorCodes.SellableInventoryRemaining, result.Error.Code);
            Assert.Equal(
                "ACTIVE",
                await ScalarAsync<string>(
                    "SELECT lifecycle_status FROM outsourced.outsourced_supply_batches WHERE id = @batch_id;",
                    cancellationToken,
                    ("batch_id", blocked.BatchId)));
            Assert.Equal(
                2m,
                await ScalarAsync<decimal>(
                    "SELECT balance_quantity FROM inventory.inventory_positions WHERE id = @position_id;",
                    cancellationToken,
                    ("position_id", blocked.SellablePositionId!.Value)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", blockedCommandId)));
        }
        finally
        {
            await CleanupOutsourcedAsync(blocked, new[] { blockedCommandId }, cancellationToken);
        }
    }

    private static InventoryPositionIdentity ProcurementIdentity(ProcurementScenario scenario) =>
        new(
            InventoryOrigin.IN_HOUSE,
            scenario.BatchId,
            null,
            InventoryObjectKind.PROCUREMENT_PRODUCT,
            scenario.ProcurementProductId,
            null,
            null,
            InventoryRawSourceKind.SUPPLIER,
            scenario.SupplierId);

    private static CloseProcurementBatchExecution ProcurementCloseExecution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        CloseProcurementBatchCommand command) =>
        new(CommandId.From(commandId), requestHash, ActorAccountId.From(actorAccountId), command);

    private static CloseOutsourcedSupplyBatchExecution OutsourcedCloseExecution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        CloseOutsourcedSupplyBatchCommand command) =>
        new(CommandId.From(commandId), requestHash, ActorAccountId.From(actorAccountId), command);

    private static CommandRequestHash Hash(byte value)
    {
        var bytes = new byte[CommandRequestHash.Sha256Length];
        Array.Fill(bytes, value);
        return CommandRequestHash.FromSha256(bytes);
    }

    private static async ValueTask<ApplicationResult<CloseProcurementBatchResult>> ExecuteProcurementCloseAsync(
        CloseProcurementBatchExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICloseProcurementBatchExecutor>()
            .ExecuteAsync(execution, cancellationToken);
    }

    private static async ValueTask<ApplicationResult<CloseOutsourcedSupplyBatchResult>> ExecuteOutsourcedCloseAsync(
        CloseOutsourcedSupplyBatchExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICloseOutsourcedSupplyBatchExecutor>()
            .ExecuteAsync(execution, cancellationToken);
    }

    private static async ValueTask<ApplicationResult<AdjustInventoryResult>> ExecuteAdjustmentAsync(
        AdjustInventoryExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAdjustInventoryExecutor>()
            .ExecuteAsync(execution, cancellationToken);
    }

    private static async Task<ProcurementScenario> SeedProcurementAsync(
        decimal residualBalance,
        decimal sellableBalance,
        CancellationToken cancellationToken)
    {
        var scenario = new ProcurementScenario(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), sellableBalance == 0m ? null : Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts(id,display_name,active,identity_issuer,identity_subject,created_at)
            VALUES(@actor_id,'P6 V7 batch close actor',true,NULL,NULL,@now);
            INSERT INTO infrastructure.warehouses(id,code,name_zh_tw,name_th_th,active,created_at,created_by_account_id)
            VALUES(@warehouse_id,NULL,'P6 V7 batch close warehouse',NULL,true,@now,@actor_id);
            INSERT INTO infrastructure.storage_locations(id,warehouse_id,code,name_zh_tw,name_th_th,active,created_at,created_by_account_id)
            VALUES(@location_id,@warehouse_id,NULL,'P6 V7 batch close location',NULL,true,@now,@actor_id);
            INSERT INTO party.suppliers(id,name_zh_tw,name_th_th,bank_name,bank_account,phone,address,active,created_at,created_by_account_id)
            VALUES(@supplier_id,'P6 V7 batch close supplier',NULL,NULL,NULL,NULL,NULL,true,@now,@actor_id);
            INSERT INTO product.procurement_products(id,name_zh_tw,name_th_th,unit_code,default_storage_location_id,active,created_at,created_by_account_id)
            VALUES(@procurement_product_id,'P6 V7 batch close procurement product',NULL,'kg',@location_id,true,@now,@actor_id);
            INSERT INTO product.sales_product_groups(id,name_zh_tw,name_th_th,active,created_at,created_by_account_id)
            VALUES(@sales_product_group_id,'P6 V7 batch close group',NULL,true,@now,@actor_id);
            INSERT INTO product.sales_products(
                id,sales_product_group_id,name_zh_tw,name_th_th,pricing_basis,packaging_weight,sales_weight,
                default_storage_location_id,active,created_at,created_by_account_id)
            VALUES(@sales_product_id,@sales_product_group_id,'P6 V7 batch close sales product',NULL,'WEIGHT_BASED_UNIT',
                   NULL,5.2,@location_id,true,@now,@actor_id);
            INSERT INTO procurement.procurement_batches(
                id,procurement_date,procurement_product_id,procurement_status,lifecycle_status,
                processing_route_id,processing_route_version_id,completed_at,completed_by_account_id,
                closed_at,closed_by_account_id,created_at,created_by_account_id,deleted_at,deleted_by_account_id)
            VALUES(@batch_id,DATE '2026-09-04',@procurement_product_id,'OPEN','ACTIVE',NULL,NULL,NULL,NULL,NULL,NULL,
                   @now,@actor_id,NULL,NULL);
            INSERT INTO inventory.inventory_positions(
                id,origin,procurement_batch_id,outsourced_supply_batch_id,inventory_object_kind,procurement_product_id,
                process_material_id,sales_product_id,storage_location_id,raw_source_kind,supplier_id,balance_quantity,row_version)
            VALUES(@residual_position_id,'IN_HOUSE',@batch_id,NULL,'PROCUREMENT_PRODUCT',@procurement_product_id,
                   NULL,NULL,@location_id,'SUPPLIER',@supplier_id,@residual_balance,1);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId), ("warehouse_id", scenario.WarehouseId),
            ("location_id", scenario.LocationId), ("supplier_id", scenario.SupplierId),
            ("procurement_product_id", scenario.ProcurementProductId),
            ("sales_product_group_id", scenario.SalesProductGroupId), ("sales_product_id", scenario.SalesProductId),
            ("batch_id", scenario.BatchId), ("residual_position_id", scenario.ResidualPositionId),
            ("residual_balance", residualBalance), ("now", now));

        if (scenario.SellablePositionId is Guid sellablePositionId)
        {
            await ExecuteNonQueryAsync(
                """
                INSERT INTO inventory.inventory_positions(
                    id,origin,procurement_batch_id,outsourced_supply_batch_id,inventory_object_kind,procurement_product_id,
                    process_material_id,sales_product_id,storage_location_id,raw_source_kind,supplier_id,balance_quantity,row_version)
                VALUES(@position_id,'IN_HOUSE',@batch_id,NULL,'SALES_PRODUCT',NULL,NULL,@sales_product_id,
                       @location_id,NULL,NULL,@sellable_balance,1);
                """,
                cancellationToken,
                ("position_id", sellablePositionId), ("batch_id", scenario.BatchId),
                ("sales_product_id", scenario.SalesProductId), ("location_id", scenario.LocationId),
                ("sellable_balance", sellableBalance));
        }

        return scenario;
    }

    private static async Task<OutsourcedScenario> SeedOutsourcedAsync(
        decimal sellableBalance,
        CancellationToken cancellationToken)
    {
        var scenario = new OutsourcedScenario(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            sellableBalance == 0m ? null : Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts(id,display_name,active,identity_issuer,identity_subject,created_at)
            VALUES(@actor_id,'P6 V7 outsourced close actor',true,NULL,NULL,@now);
            INSERT INTO infrastructure.warehouses(id,code,name_zh_tw,name_th_th,active,created_at,created_by_account_id)
            VALUES(@warehouse_id,NULL,'P6 V7 outsourced close warehouse',NULL,true,@now,@actor_id);
            INSERT INTO infrastructure.storage_locations(id,warehouse_id,code,name_zh_tw,name_th_th,active,created_at,created_by_account_id)
            VALUES(@location_id,@warehouse_id,NULL,'P6 V7 outsourced close location',NULL,true,@now,@actor_id);
            INSERT INTO product.sales_product_groups(id,name_zh_tw,name_th_th,active,created_at,created_by_account_id)
            VALUES(@sales_product_group_id,'P6 V7 outsourced close group',NULL,true,@now,@actor_id);
            INSERT INTO product.sales_products(
                id,sales_product_group_id,name_zh_tw,name_th_th,pricing_basis,packaging_weight,sales_weight,
                default_storage_location_id,active,created_at,created_by_account_id)
            VALUES(@sales_product_id,@sales_product_group_id,'P6 V7 outsourced close product',NULL,'WEIGHT_BASED_UNIT',
                   NULL,5.2,@location_id,true,@now,@actor_id);
            INSERT INTO party.outsourced_vendors(id,name_zh_tw,name_th_th,bank_name,bank_account,phone,address,active,created_at,created_by_account_id)
            VALUES(@vendor_id,'P6 V7 outsourced close vendor',NULL,NULL,NULL,NULL,NULL,true,@now,@actor_id);
            INSERT INTO outsourced.outsourced_supply_batches(
                id,supply_date,outsourced_vendor_id,lifecycle_status,closed_at,closed_by_account_id,
                created_at,created_by_account_id,deleted_at,deleted_by_account_id)
            VALUES(@batch_id,DATE '2026-09-04',@vendor_id,'ACTIVE',NULL,NULL,@now,@actor_id,NULL,NULL);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId), ("warehouse_id", scenario.WarehouseId),
            ("location_id", scenario.LocationId), ("sales_product_group_id", scenario.SalesProductGroupId),
            ("sales_product_id", scenario.SalesProductId), ("vendor_id", scenario.VendorId),
            ("batch_id", scenario.BatchId), ("now", now));

        if (scenario.SellablePositionId is Guid sellablePositionId)
        {
            await ExecuteNonQueryAsync(
                """
                INSERT INTO inventory.inventory_positions(
                    id,origin,procurement_batch_id,outsourced_supply_batch_id,inventory_object_kind,procurement_product_id,
                    process_material_id,sales_product_id,storage_location_id,raw_source_kind,supplier_id,balance_quantity,row_version)
                VALUES(@position_id,'OUTSOURCED',NULL,@batch_id,'SALES_PRODUCT',NULL,NULL,@sales_product_id,
                       @location_id,NULL,NULL,@sellable_balance,1);
                """,
                cancellationToken,
                ("position_id", sellablePositionId), ("batch_id", scenario.BatchId),
                ("sales_product_id", scenario.SalesProductId), ("location_id", scenario.LocationId),
                ("sellable_balance", sellableBalance));
        }

        return scenario;
    }

    private static Task CleanupProcurementAsync(
        ProcurementScenario scenario,
        IReadOnlyCollection<Guid> commandIds,
        CancellationToken cancellationToken) =>
        ExecuteNonQueryAsync(
            """
            DELETE FROM audit.audit_event_subjects WHERE audit_event_id IN (SELECT id FROM audit.audit_events WHERE command_id = ANY(@command_ids));
            DELETE FROM audit.audit_events WHERE command_id = ANY(@command_ids);
            DELETE FROM system.outbox_messages WHERE command_id = ANY(@command_ids);
            DELETE FROM system.command_executions WHERE command_id = ANY(@command_ids);
            DELETE FROM inventory.inventory_movements WHERE procurement_batch_id = @batch_id;
            DELETE FROM inventory.inventory_operations WHERE procurement_batch_id = @batch_id;
            DELETE FROM inventory.inventory_positions WHERE procurement_batch_id = @batch_id;
            DELETE FROM procurement.procurement_batches WHERE id = @batch_id;
            DELETE FROM product.sales_products WHERE id = @sales_product_id;
            DELETE FROM product.sales_product_groups WHERE id = @sales_product_group_id;
            DELETE FROM product.procurement_products WHERE id = @procurement_product_id;
            DELETE FROM party.suppliers WHERE id = @supplier_id;
            DELETE FROM infrastructure.storage_locations WHERE id = @location_id;
            DELETE FROM infrastructure.warehouses WHERE id = @warehouse_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds.ToArray()), ("batch_id", scenario.BatchId),
            ("sales_product_id", scenario.SalesProductId), ("sales_product_group_id", scenario.SalesProductGroupId),
            ("procurement_product_id", scenario.ProcurementProductId), ("supplier_id", scenario.SupplierId),
            ("location_id", scenario.LocationId), ("warehouse_id", scenario.WarehouseId),
            ("actor_id", scenario.ActorAccountId));

    private static Task CleanupOutsourcedAsync(
        OutsourcedScenario scenario,
        IReadOnlyCollection<Guid> commandIds,
        CancellationToken cancellationToken) =>
        ExecuteNonQueryAsync(
            """
            DELETE FROM audit.audit_event_subjects WHERE audit_event_id IN (SELECT id FROM audit.audit_events WHERE command_id = ANY(@command_ids));
            DELETE FROM audit.audit_events WHERE command_id = ANY(@command_ids);
            DELETE FROM system.outbox_messages WHERE command_id = ANY(@command_ids);
            DELETE FROM system.command_executions WHERE command_id = ANY(@command_ids);
            DELETE FROM inventory.inventory_movements WHERE outsourced_supply_batch_id = @batch_id;
            DELETE FROM inventory.inventory_positions WHERE outsourced_supply_batch_id = @batch_id;
            DELETE FROM outsourced.outsourced_supply_batches WHERE id = @batch_id;
            DELETE FROM product.sales_products WHERE id = @sales_product_id;
            DELETE FROM product.sales_product_groups WHERE id = @sales_product_group_id;
            DELETE FROM party.outsourced_vendors WHERE id = @vendor_id;
            DELETE FROM infrastructure.storage_locations WHERE id = @location_id;
            DELETE FROM infrastructure.warehouses WHERE id = @warehouse_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds.ToArray()), ("batch_id", scenario.BatchId),
            ("sales_product_id", scenario.SalesProductId), ("sales_product_group_id", scenario.SalesProductGroupId),
            ("vendor_id", scenario.VendorId), ("location_id", scenario.LocationId),
            ("warehouse_id", scenario.WarehouseId), ("actor_id", scenario.ActorAccountId));

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

    private sealed record ProcurementScenario(
        Guid ActorAccountId,
        Guid WarehouseId,
        Guid LocationId,
        Guid SupplierId,
        Guid ProcurementProductId,
        Guid SalesProductGroupId,
        Guid SalesProductId,
        Guid BatchId,
        Guid ResidualPositionId,
        Guid? SellablePositionId);

    private sealed record OutsourcedScenario(
        Guid ActorAccountId,
        Guid WarehouseId,
        Guid LocationId,
        Guid SalesProductGroupId,
        Guid SalesProductId,
        Guid VendorId,
        Guid BatchId,
        Guid? SellablePositionId);
}
