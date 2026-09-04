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

public sealed class SalesAllocationCorrectionIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Complete_replacement_appends_revision_compensates_inventory_repoints_current_allocation_and_replays()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedConfirmedScenarioAsync(8m, 4m, 10m, cancellationToken);
        var commandId = Guid.CreateVersion7();
        var command = new CorrectSalesAllocationCommand(
            scenario.SalesId,
            2,
            SalesAllocationCorrectionMode.COMPLETE_REPLACEMENT,
            new[]
            {
                new SalesAllocationCorrectionInput(
                    scenario.SalesDetailId,
                    InventoryOrigin.IN_HOUSE,
                    scenario.ProcurementBatchId,
                    null,
                    8m),
            });
        var execution = CorrectionExecution(commandId, scenario.ActorAccountId, Hash(20), command);

        try
        {
            var result = await ExecuteCorrectionAsync(execution, cancellationToken);
            Assert.True(result.IsSuccess);
            Assert.Equal(scenario.SalesId, result.Value.SalesId);
            Assert.Equal(3L, result.Value.SalesRowVersion);
            Assert.Equal(1, result.Value.RevisionNumber);

            var replay = await ExecuteCorrectionAsync(execution, cancellationToken);
            Assert.True(replay.IsSuccess);
            Assert.Equal(result.Value, replay.Value);

            var changedHash = await ExecuteCorrectionAsync(
                CorrectionExecution(commandId, scenario.ActorAccountId, Hash(21), command),
                cancellationToken);
            Assert.True(changedHash.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, changedHash.Error.Kind);
            Assert.Equal(SalesApplicationErrorCodes.IdempotencyKeyReused, changedHash.Error.Code);

            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    "SELECT revision_number FROM sales.sales_allocation_revisions WHERE id = @revision_id;",
                    cancellationToken,
                    ("revision_id", result.Value.AllocationRevisionId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM sales.sales_allocation_revision_items WHERE sales_allocation_revision_id = @revision_id;",
                    cancellationToken,
                    ("revision_id", result.Value.AllocationRevisionId)));
            Assert.True(
                await ScalarAsync<bool>(
                    "SELECT manual_override FROM sales.sales_allocation_revision_items WHERE sales_allocation_revision_id = @revision_id;",
                    cancellationToken,
                    ("revision_id", result.Value.AllocationRevisionId)));
            Assert.Equal(
                "IN_HOUSE",
                await ScalarAsync<string>(
                    "SELECT origin FROM sales.sales_allocation_revision_items WHERE sales_allocation_revision_id = @revision_id;",
                    cancellationToken,
                    ("revision_id", result.Value.AllocationRevisionId)));
            Assert.Equal(
                8m,
                await ScalarAsync<decimal>(
                    "SELECT allocated_quantity FROM sales.sales_allocation_revision_items WHERE sales_allocation_revision_id = @revision_id;",
                    cancellationToken,
                    ("revision_id", result.Value.AllocationRevisionId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM sales.sales_allocations a
                    JOIN sales.sales_allocation_revision_items i
                      ON i.id = a.sales_allocation_revision_item_id
                    WHERE a.sales_detail_id = @detail_id
                      AND i.sales_allocation_revision_id = @revision_id;
                    """,
                    cancellationToken,
                    ("detail_id", scenario.SalesDetailId),
                    ("revision_id", result.Value.AllocationRevisionId)));

            Assert.Equal(
                2L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM inventory.inventory_movements WHERE inventory_operation_id = @operation_id AND movement_type = 'SALES_ALLOCATION_ADJUSTMENT';",
                    cancellationToken,
                    ("operation_id", result.Value.InventoryOperationId)));
            Assert.Equal(
                0m,
                await ScalarAsync<decimal>(
                    "SELECT sum(quantity_delta) FROM inventory.inventory_movements WHERE inventory_operation_id = @operation_id;",
                    cancellationToken,
                    ("operation_id", result.Value.InventoryOperationId)));
            Assert.Equal(
                4m,
                await ScalarAsync<decimal>(
                    "SELECT max(quantity_delta) FROM inventory.inventory_movements WHERE inventory_operation_id = @operation_id;",
                    cancellationToken,
                    ("operation_id", result.Value.InventoryOperationId)));
            Assert.Equal(
                -4m,
                await ScalarAsync<decimal>(
                    "SELECT min(quantity_delta) FROM inventory.inventory_movements WHERE inventory_operation_id = @operation_id;",
                    cancellationToken,
                    ("operation_id", result.Value.InventoryOperationId)));

            Assert.Equal(
                4m,
                await ScalarAsync<decimal>(
                    "SELECT balance_quantity FROM inventory.inventory_positions WHERE id = @position_id;",
                    cancellationToken,
                    ("position_id", scenario.OutsourcedPositionId)));
            Assert.Equal(
                2m,
                await ScalarAsync<decimal>(
                    "SELECT balance_quantity FROM inventory.inventory_positions WHERE id = @position_id;",
                    cancellationToken,
                    ("position_id", scenario.InHousePositionId)));
            Assert.Equal(
                3L,
                await ScalarAsync<long>(
                    "SELECT row_version FROM sales.sales WHERE id = @sales_id;",
                    cancellationToken,
                    ("sales_id", scenario.SalesId)));

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = @command_id AND command_type = 'CorrectSalesAllocation' AND event_kind = 'CORRECTION';",
                    cancellationToken,
                    ("command_id", commandId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.correction_links l
                    JOIN audit.audit_events correction ON correction.id = l.correction_audit_event_id
                    JOIN audit.audit_events corrected ON corrected.id = l.corrected_audit_event_id
                    WHERE correction.command_id = @correction_command_id
                      AND corrected.command_id = @confirm_command_id
                      AND l.correction_mode = 'COMPENSATION';
                    """,
                    cancellationToken,
                    ("correction_command_id", commandId),
                    ("confirm_command_id", scenario.ConfirmCommandId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.outbox_messages WHERE command_id = @command_id AND message_type = 'sales.allocation-revised';",
                    cancellationToken,
                    ("command_id", commandId)));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Override_and_reallocate_keeps_explicit_override_then_auto_allocates_remainder_without_unnecessary_inventory_delta()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedConfirmedScenarioAsync(8m, 4m, 10m, cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            var result = await ExecuteCorrectionAsync(
                CorrectionExecution(
                    commandId,
                    scenario.ActorAccountId,
                    Hash(22),
                    new CorrectSalesAllocationCommand(
                        scenario.SalesId,
                        2,
                        SalesAllocationCorrectionMode.OVERRIDE_AND_REALLOCATE,
                        new[]
                        {
                            new SalesAllocationCorrectionInput(
                                scenario.SalesDetailId,
                                InventoryOrigin.IN_HOUSE,
                                scenario.ProcurementBatchId,
                                null,
                                2m),
                        })),
                cancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.Value.RevisionNumber);
            Assert.Equal(
                3L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM sales.sales_allocation_revision_items WHERE sales_allocation_revision_id = @revision_id;",
                    cancellationToken,
                    ("revision_id", result.Value.AllocationRevisionId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM sales.sales_allocation_revision_items WHERE sales_allocation_revision_id = @revision_id AND manual_override = true AND origin = 'IN_HOUSE' AND allocated_quantity = 2;",
                    cancellationToken,
                    ("revision_id", result.Value.AllocationRevisionId)));
            Assert.Equal(
                4m,
                await ScalarAsync<decimal>(
                    "SELECT sum(allocated_quantity) FROM sales.sales_allocation_revision_items WHERE sales_allocation_revision_id = @revision_id AND origin = 'OUTSOURCED' AND manual_override = false;",
                    cancellationToken,
                    ("revision_id", result.Value.AllocationRevisionId)));
            Assert.Equal(
                2m,
                await ScalarAsync<decimal>(
                    "SELECT sum(allocated_quantity) FROM sales.sales_allocation_revision_items WHERE sales_allocation_revision_id = @revision_id AND origin = 'IN_HOUSE' AND manual_override = false;",
                    cancellationToken,
                    ("revision_id", result.Value.AllocationRevisionId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM inventory.inventory_movements WHERE inventory_operation_id = @operation_id;",
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
        }
        finally
        {
            await CleanupScenarioAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Stale_version_and_incomplete_complete_replacement_roll_back_command_identity_and_revision()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedConfirmedScenarioAsync(8m, 4m, 10m, cancellationToken);
        var staleCommandId = Guid.CreateVersion7();
        var invalidCommandId = Guid.CreateVersion7();

        try
        {
            var stale = await ExecuteCorrectionAsync(
                CorrectionExecution(
                    staleCommandId,
                    scenario.ActorAccountId,
                    Hash(23),
                    new CorrectSalesAllocationCommand(
                        scenario.SalesId,
                        1,
                        SalesAllocationCorrectionMode.OVERRIDE_AND_REALLOCATE,
                        Array.Empty<SalesAllocationCorrectionInput>())),
                cancellationToken);
            Assert.True(stale.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, stale.Error.Kind);
            Assert.Equal(SalesApplicationErrorCodes.StaleRowVersion, stale.Error.Code);

            var invalid = await ExecuteCorrectionAsync(
                CorrectionExecution(
                    invalidCommandId,
                    scenario.ActorAccountId,
                    Hash(24),
                    new CorrectSalesAllocationCommand(
                        scenario.SalesId,
                        2,
                        SalesAllocationCorrectionMode.COMPLETE_REPLACEMENT,
                        new[]
                        {
                            new SalesAllocationCorrectionInput(
                                scenario.SalesDetailId,
                                InventoryOrigin.IN_HOUSE,
                                scenario.ProcurementBatchId,
                                null,
                                7m),
                        })),
                cancellationToken);
            Assert.True(invalid.IsFailure);
            Assert.Equal(ApplicationErrorKind.Validation, invalid.Error.Kind);
            Assert.Equal(SalesAllocationCorrectionErrorCodes.InvalidInput, invalid.Error.Code);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM sales.sales_allocation_revisions WHERE sales_id = @sales_id;",
                    cancellationToken,
                    ("sales_id", scenario.SalesId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = ANY(@command_ids);",
                    cancellationToken,
                    ("command_ids", new[] { staleCommandId, invalidCommandId })));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, new[] { staleCommandId, invalidCommandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Correction_that_would_write_inventory_back_into_closed_source_batch_is_blocked_without_reopen_or_partial_commit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedConfirmedScenarioAsync(8m, 4m, 10m, cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            await ExecuteNonQueryAsync(
                """
                UPDATE outsourced.outsourced_supply_batches
                SET lifecycle_status = 'CLOSED',
                    closed_at = @now,
                    closed_by_account_id = @actor_id,
                    row_version = row_version + 1
                WHERE id = @batch_id;
                """,
                cancellationToken,
                ("now", DateTimeOffset.UtcNow),
                ("actor_id", scenario.ActorAccountId),
                ("batch_id", scenario.OutsourcedSupplyBatchId));

            var result = await ExecuteCorrectionAsync(
                CorrectionExecution(
                    commandId,
                    scenario.ActorAccountId,
                    Hash(25),
                    new CorrectSalesAllocationCommand(
                        scenario.SalesId,
                        2,
                        SalesAllocationCorrectionMode.COMPLETE_REPLACEMENT,
                        new[]
                        {
                            new SalesAllocationCorrectionInput(
                                scenario.SalesDetailId,
                                InventoryOrigin.IN_HOUSE,
                                scenario.ProcurementBatchId,
                                null,
                                8m),
                        })),
                cancellationToken);

            Assert.True(result.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, result.Error.Kind);
            Assert.Equal(SalesAllocationCorrectionErrorCodes.LifecycleBlocked, result.Error.Code);
            Assert.Equal(
                "CLOSED",
                await ScalarAsync<string>(
                    "SELECT lifecycle_status FROM outsourced.outsourced_supply_batches WHERE id = @batch_id;",
                    cancellationToken,
                    ("batch_id", scenario.OutsourcedSupplyBatchId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM sales.sales_allocation_revisions WHERE sales_id = @sales_id;",
                    cancellationToken,
                    ("sales_id", scenario.SalesId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", commandId)));
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
        }
        finally
        {
            await CleanupScenarioAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    private static async Task<Scenario> SeedConfirmedScenarioAsync(
        decimal salesQuantity,
        decimal outsourcedBalance,
        decimal inHouseBalance,
        CancellationToken cancellationToken)
    {
        var scenario = new Scenario(
            ActorAccountId: Guid.CreateVersion7(),
            WarehouseId: Guid.CreateVersion7(),
            StorageLocationId: Guid.CreateVersion7(),
            SalesProductGroupId: Guid.CreateVersion7(),
            SalesProductId: Guid.CreateVersion7(),
            ProcurementProductId: Guid.CreateVersion7(),
            VendorId: Guid.CreateVersion7(),
            CustomerId: Guid.CreateVersion7(),
            OutsourcedSupplyBatchId: Guid.CreateVersion7(),
            ProcurementBatchId: Guid.CreateVersion7(),
            OutsourcedPositionId: Guid.CreateVersion7(),
            InHousePositionId: Guid.CreateVersion7(),
            SalesId: Guid.CreateVersion7(),
            SalesDetailId: Guid.CreateVersion7(),
            ConfirmCommandId: Guid.CreateVersion7(),
            ConfirmResult: null!);
        var amountThb = checked((long)decimal.Floor(salesQuantity * 50m));
        var now = DateTimeOffset.UtcNow;

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 V8 sales correction actor', true, NULL, NULL, @now);

            INSERT INTO infrastructure.warehouses
                (id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@warehouse_id, NULL, 'P6 V8 sales correction warehouse', NULL, true, @now, @actor_id);

            INSERT INTO infrastructure.storage_locations
                (id, warehouse_id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@location_id, @warehouse_id, NULL, 'P6 V8 sales correction location', NULL, true, @now, @actor_id);

            INSERT INTO product.sales_product_groups
                (id, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@sales_product_group_id, 'P6 V8 sales correction group', NULL, true, @now, @actor_id);

            INSERT INTO product.sales_products
                (id, sales_product_group_id, name_zh_tw, name_th_th, pricing_basis,
                 packaging_weight, sales_weight, default_storage_location_id,
                 active, created_at, created_by_account_id)
            VALUES
                (@sales_product_id, @sales_product_group_id, 'P6 V8 sales correction product', NULL, 'UNIT_BASED',
                 NULL, NULL, @location_id, true, @now, @actor_id);

            INSERT INTO product.procurement_products
                (id, name_zh_tw, name_th_th, unit_code, default_storage_location_id,
                 active, created_at, created_by_account_id)
            VALUES
                (@procurement_product_id, 'P6 V8 procurement product', NULL, 'kg', @location_id,
                 true, @now, @actor_id);

            INSERT INTO party.outsourced_vendors
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, created_at, created_by_account_id)
            VALUES
                (@vendor_id, 'P6 V8 vendor', NULL, NULL, NULL, NULL, NULL,
                 true, @now, @actor_id);

            INSERT INTO party.customers
                (id, name_zh_tw, name_th_th, phone, active, created_at, created_by_account_id)
            VALUES
                (@customer_id, 'P6 V8 customer', NULL, NULL, true, @now, @actor_id);

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
                 'ACTIVE', NULL, NULL, NULL, NULL, NULL, NULL,
                 @now, @actor_id, NULL, NULL);

            INSERT INTO sales.sales
                (id, sales_date, customer_id, status, confirmed_at, confirmed_by_account_id,
                 row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@sales_id, DATE '2026-09-04', @customer_id, 'DRAFT', NULL, NULL,
                 1, @now, @actor_id, NULL, NULL);

            INSERT INTO sales.sales_details
                (id, sales_id, line_number, sales_product_id, quantity,
                 pricing_basis_snapshot, sales_weight_snapshot, unit_price, amount_thb,
                 row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@detail_id, @sales_id, 1, @sales_product_id, @quantity,
                 'UNIT_BASED', NULL, 50, @amount_thb,
                 1, @now, @actor_id, NULL, NULL);

            INSERT INTO inventory.inventory_positions
                (id, origin, procurement_batch_id, outsourced_supply_batch_id,
                 inventory_object_kind, procurement_product_id, process_material_id,
                 sales_product_id, storage_location_id, raw_source_kind, supplier_id,
                 balance_quantity, row_version)
            VALUES
                (@outsourced_position_id, 'OUTSOURCED', NULL, @outsourced_batch_id,
                 'SALES_PRODUCT', NULL, NULL, @sales_product_id, @location_id, NULL, NULL,
                 @outsourced_balance, 1),
                (@in_house_position_id, 'IN_HOUSE', @procurement_batch_id, NULL,
                 'SALES_PRODUCT', NULL, NULL, @sales_product_id, @location_id, NULL, NULL,
                 @in_house_balance, 1);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("warehouse_id", scenario.WarehouseId),
            ("location_id", scenario.StorageLocationId),
            ("sales_product_group_id", scenario.SalesProductGroupId),
            ("sales_product_id", scenario.SalesProductId),
            ("procurement_product_id", scenario.ProcurementProductId),
            ("vendor_id", scenario.VendorId),
            ("customer_id", scenario.CustomerId),
            ("outsourced_batch_id", scenario.OutsourcedSupplyBatchId),
            ("procurement_batch_id", scenario.ProcurementBatchId),
            ("sales_id", scenario.SalesId),
            ("detail_id", scenario.SalesDetailId),
            ("quantity", salesQuantity),
            ("amount_thb", amountThb),
            ("outsourced_position_id", scenario.OutsourcedPositionId),
            ("in_house_position_id", scenario.InHousePositionId),
            ("outsourced_balance", outsourcedBalance),
            ("in_house_balance", inHouseBalance),
            ("now", now));

        var confirmResult = await ExecuteConfirmAsync(
            new ConfirmSalesExecution(
                CommandId.From(scenario.ConfirmCommandId),
                Hash(19),
                ActorAccountId.From(scenario.ActorAccountId),
                new ConfirmSalesCommand(
                    scenario.SalesId,
                    1,
                    Array.Empty<SalesManualAllocationOverride>())),
            cancellationToken);
        Assert.True(confirmResult.IsSuccess);
        Assert.Equal(2L, confirmResult.Value.SalesRowVersion);
        return scenario with { ConfirmResult = confirmResult.Value };
    }

    private static CorrectSalesAllocationExecution CorrectionExecution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        CorrectSalesAllocationCommand command) =>
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

    private static async ValueTask<ApplicationResult<ConfirmSalesResult>> ExecuteConfirmAsync(
        ConfirmSalesExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IConfirmSalesExecutor>()
            .ExecuteAsync(execution, cancellationToken);
    }

    private static async ValueTask<ApplicationResult<CorrectSalesAllocationResult>> ExecuteCorrectionAsync(
        CorrectSalesAllocationExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<ICorrectSalesAllocationExecutor>()
            .ExecuteAsync(execution, cancellationToken);
    }

    private static async Task CleanupScenarioAsync(
        Scenario scenario,
        IReadOnlyCollection<Guid> correctionCommandIds,
        CancellationToken cancellationToken)
    {
        var commandIds = correctionCommandIds.Append(scenario.ConfirmCommandId).ToArray();
        await ExecuteNonQueryAsync(
            """
            DELETE FROM audit.correction_links
            WHERE correction_audit_event_id IN (SELECT id FROM audit.audit_events WHERE command_id = ANY(@command_ids))
               OR corrected_audit_event_id IN (SELECT id FROM audit.audit_events WHERE command_id = ANY(@command_ids));
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
            WHERE inventory_operation_id IN (
                SELECT o.id
                FROM inventory.inventory_operations o
                LEFT JOIN sales.sales_allocation_revisions r ON r.id = o.sales_allocation_revision_id
                WHERE o.sales_id = @sales_id OR r.sales_id = @sales_id);
            DELETE FROM inventory.inventory_operations
            WHERE sales_id = @sales_id
               OR sales_allocation_revision_id IN (SELECT id FROM sales.sales_allocation_revisions WHERE sales_id = @sales_id);

            DELETE FROM sales.sales_allocations WHERE sales_detail_id = @detail_id;
            DELETE FROM sales.sales_allocation_revision_items WHERE sales_id = @sales_id;
            DELETE FROM sales.sales_allocation_revisions WHERE sales_id = @sales_id;
            DELETE FROM sales.sales_details WHERE sales_id = @sales_id;
            DELETE FROM sales.sales WHERE id = @sales_id;

            DELETE FROM inventory.inventory_positions WHERE id = @outsourced_position_id OR id = @in_house_position_id;
            DELETE FROM outsourced.outsourced_supply_batches WHERE id = @outsourced_batch_id;
            DELETE FROM procurement.procurement_batches WHERE id = @procurement_batch_id;
            DELETE FROM product.sales_products WHERE id = @sales_product_id;
            DELETE FROM product.sales_product_groups WHERE id = @sales_product_group_id;
            DELETE FROM product.procurement_products WHERE id = @procurement_product_id;
            DELETE FROM party.customers WHERE id = @customer_id;
            DELETE FROM party.outsourced_vendors WHERE id = @vendor_id;
            DELETE FROM infrastructure.storage_locations WHERE id = @location_id;
            DELETE FROM infrastructure.warehouses WHERE id = @warehouse_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds),
            ("sales_id", scenario.SalesId),
            ("detail_id", scenario.SalesDetailId),
            ("outsourced_position_id", scenario.OutsourcedPositionId),
            ("in_house_position_id", scenario.InHousePositionId),
            ("outsourced_batch_id", scenario.OutsourcedSupplyBatchId),
            ("procurement_batch_id", scenario.ProcurementBatchId),
            ("sales_product_id", scenario.SalesProductId),
            ("sales_product_group_id", scenario.SalesProductGroupId),
            ("procurement_product_id", scenario.ProcurementProductId),
            ("customer_id", scenario.CustomerId),
            ("vendor_id", scenario.VendorId),
            ("location_id", scenario.StorageLocationId),
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
        Guid WarehouseId,
        Guid StorageLocationId,
        Guid SalesProductGroupId,
        Guid SalesProductId,
        Guid ProcurementProductId,
        Guid VendorId,
        Guid CustomerId,
        Guid OutsourcedSupplyBatchId,
        Guid ProcurementBatchId,
        Guid OutsourcedPositionId,
        Guid InHousePositionId,
        Guid SalesId,
        Guid SalesDetailId,
        Guid ConfirmCommandId,
        ConfirmSalesResult ConfirmResult);
}