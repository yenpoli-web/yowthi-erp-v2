using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Inventory;
using YowThi.Erp.Application.Procurement;
using YowThi.Erp.Domain.Inventory;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class ProcurementBatchReopenIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Reopen_closed_batch_preserves_close_audit_and_reconciliation_and_replays()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedAsync(3m, cancellationToken);
        var closeCommandId = Guid.CreateVersion7();
        var reopenCommandId = Guid.CreateVersion7();

        try
        {
            var close = await ExecuteCloseAsync(
                CloseExecution(
                    closeCommandId,
                    scenario.ActorAccountId,
                    Hash(1),
                    new CloseProcurementBatchCommand(scenario.BatchId, 1)),
                cancellationToken);
            Assert.True(close.IsSuccess);
            Assert.NotNull(close.Value.InventoryOperationId);
            Assert.Equal(2, close.Value.ClosedRowVersion);

            var reopenExecution = ReopenExecution(
                reopenCommandId,
                scenario.ActorAccountId,
                Hash(2),
                new ReopenProcurementBatchCommand(
                    scenario.BatchId,
                    2,
                    "operator correction"));
            var reopen = await ExecuteReopenAsync(reopenExecution, cancellationToken);

            Assert.True(reopen.IsSuccess);
            Assert.Equal(scenario.BatchId, reopen.Value.ProcurementBatchId);
            Assert.Equal(3, reopen.Value.ReopenedRowVersion);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM procurement.procurement_batches
                    WHERE id = @batch_id
                      AND lifecycle_status = 'ACTIVE'
                      AND closed_at IS NULL
                      AND closed_by_account_id IS NULL
                      AND row_version = 3;
                    """,
                    cancellationToken,
                    ("batch_id", scenario.BatchId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM inventory.inventory_operations
                    WHERE id = @operation_id
                      AND procurement_batch_id = @batch_id
                      AND operation_type = 'BATCH_RECONCILIATION';
                    """,
                    cancellationToken,
                    ("operation_id", close.Value.InventoryOperationId!.Value),
                    ("batch_id", scenario.BatchId)));
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
                    ("operation_id", close.Value.InventoryOperationId!.Value)));
            Assert.Equal(
                0m,
                await ScalarAsync<decimal>(
                    "SELECT balance_quantity FROM inventory.inventory_positions WHERE id = @position_id;",
                    cancellationToken,
                    ("position_id", scenario.PositionId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = @command_id AND command_type = 'CloseProcurementBatch';",
                    cancellationToken,
                    ("command_id", closeCommandId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.audit_events e
                    JOIN audit.audit_event_subjects s ON s.audit_event_id = e.id
                    WHERE e.command_id = @command_id
                      AND e.command_type = 'ReopenProcurementBatch'
                      AND e.event_kind = 'DATA_LIFECYCLE'
                      AND e.reason_text = 'operator correction'
                      AND s.subject_kind = 'procurement.batch'
                      AND s.change_kind = 'UPDATE'
                      AND s.before_row_version = 2
                      AND s.after_row_version = 3
                      AND s.change_summary -> 'lifecycleStatus' ->> 'before' = 'CLOSED'
                      AND s.change_summary -> 'lifecycleStatus' ->> 'after' = 'ACTIVE';
                    """,
                    cancellationToken,
                    ("command_id", reopenCommandId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.outbox_messages WHERE command_id = @command_id AND message_type = 'procurement.batch.reopened';",
                    cancellationToken,
                    ("command_id", reopenCommandId)));

            var replay = await ExecuteReopenAsync(reopenExecution, cancellationToken);
            Assert.True(replay.IsSuccess);
            Assert.Equal(reopen.Value, replay.Value);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", reopenCommandId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.outbox_messages WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", reopenCommandId)));

            var changedHash = await ExecuteReopenAsync(
                reopenExecution with { RequestHash = Hash(3) },
                cancellationToken);
            Assert.True(changedHash.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, changedHash.Error.Kind);
            Assert.Equal(ProcurementBatchReopenErrorCodes.IdempotencyKeyReused, changedHash.Error.Code);
        }
        finally
        {
            await CleanupAsync(scenario, [closeCommandId, reopenCommandId], cancellationToken);
        }
    }

    [Fact]
    public async Task Reopen_does_not_restore_inventory_but_allows_explicit_adjustment_afterward()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedAsync(3m, cancellationToken);
        var closeCommandId = Guid.CreateVersion7();
        var reopenCommandId = Guid.CreateVersion7();
        var adjustmentCommandId = Guid.CreateVersion7();

        try
        {
            var close = await ExecuteCloseAsync(
                CloseExecution(
                    closeCommandId,
                    scenario.ActorAccountId,
                    Hash(10),
                    new CloseProcurementBatchCommand(scenario.BatchId, 1)),
                cancellationToken);
            Assert.True(close.IsSuccess);
            Assert.Equal(0m, await PositionBalanceAsync(scenario.PositionId, cancellationToken));

            var reopen = await ExecuteReopenAsync(
                ReopenExecution(
                    reopenCommandId,
                    scenario.ActorAccountId,
                    Hash(11),
                    new ReopenProcurementBatchCommand(scenario.BatchId, 2, null)),
                cancellationToken);
            Assert.True(reopen.IsSuccess);
            Assert.Equal(0m, await PositionBalanceAsync(scenario.PositionId, cancellationToken));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM inventory.inventory_operations WHERE procurement_batch_id = @batch_id AND operation_type = 'BATCH_RECONCILIATION';",
                    cancellationToken,
                    ("batch_id", scenario.BatchId)));

            var adjustment = await ExecuteAdjustmentAsync(
                new AdjustInventoryExecution(
                    CommandId.From(adjustmentCommandId),
                    Hash(12),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new AdjustInventoryCommand(
                        ProcurementIdentity(scenario),
                        scenario.LocationId,
                        2m,
                        "physical stocktake after reopen")),
                cancellationToken);

            Assert.True(adjustment.IsSuccess);
            Assert.Equal(2m, adjustment.Value.QuantityDelta);
            Assert.Equal(2m, await PositionBalanceAsync(scenario.PositionId, cancellationToken));
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
                    ("operation_id", close.Value.InventoryOperationId!.Value)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM inventory.inventory_movements
                    WHERE procurement_batch_id = @batch_id
                      AND movement_type = 'ADJUSTMENT'
                      AND quantity_delta = 2;
                    """,
                    cancellationToken,
                    ("batch_id", scenario.BatchId)));
        }
        finally
        {
            await CleanupAsync(
                scenario,
                [closeCommandId, reopenCommandId, adjustmentCommandId],
                cancellationToken);
        }
    }

    [Fact]
    public async Task Reopen_active_stale_and_deleted_batch_fail_without_command_audit_or_outbox_residue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedAsync(0m, cancellationToken);
        var activeCommandId = Guid.CreateVersion7();
        var closeCommandId = Guid.CreateVersion7();
        var staleCommandId = Guid.CreateVersion7();
        var deletedCommandId = Guid.CreateVersion7();

        try
        {
            var active = await ExecuteReopenAsync(
                ReopenExecution(
                    activeCommandId,
                    scenario.ActorAccountId,
                    Hash(20),
                    new ReopenProcurementBatchCommand(scenario.BatchId, 1, null)),
                cancellationToken);
            Assert.True(active.IsFailure);
            Assert.Equal(ProcurementBatchReopenErrorCodes.BatchNotClosed, active.Error.Code);

            var close = await ExecuteCloseAsync(
                CloseExecution(
                    closeCommandId,
                    scenario.ActorAccountId,
                    Hash(21),
                    new CloseProcurementBatchCommand(scenario.BatchId, 1)),
                cancellationToken);
            Assert.True(close.IsSuccess);
            Assert.Equal(2, close.Value.ClosedRowVersion);

            var stale = await ExecuteReopenAsync(
                ReopenExecution(
                    staleCommandId,
                    scenario.ActorAccountId,
                    Hash(22),
                    new ReopenProcurementBatchCommand(scenario.BatchId, 1, null)),
                cancellationToken);
            Assert.True(stale.IsFailure);
            Assert.Equal(ProcurementBatchReopenErrorCodes.ConcurrentChange, stale.Error.Code);

            await ExecuteNonQueryAsync(
                """
                UPDATE procurement.procurement_batches
                SET deleted_at = @deleted_at,
                    deleted_by_account_id = @actor_id
                WHERE id = @batch_id;
                """,
                cancellationToken,
                ("deleted_at", DateTimeOffset.UtcNow),
                ("actor_id", scenario.ActorAccountId),
                ("batch_id", scenario.BatchId));

            var deleted = await ExecuteReopenAsync(
                ReopenExecution(
                    deletedCommandId,
                    scenario.ActorAccountId,
                    Hash(23),
                    new ReopenProcurementBatchCommand(scenario.BatchId, 2, null)),
                cancellationToken);
            Assert.True(deleted.IsFailure);
            Assert.Equal(ProcurementBatchReopenErrorCodes.BatchUnavailable, deleted.Error.Code);

            var failedIds = new[] { activeCommandId, staleCommandId, deletedCommandId };
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = ANY(@command_ids);",
                    cancellationToken,
                    ("command_ids", failedIds)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = ANY(@command_ids);",
                    cancellationToken,
                    ("command_ids", failedIds)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.outbox_messages WHERE command_id = ANY(@command_ids);",
                    cancellationToken,
                    ("command_ids", failedIds)));
        }
        finally
        {
            await CleanupAsync(
                scenario,
                [activeCommandId, closeCommandId, staleCommandId, deletedCommandId],
                cancellationToken);
        }
    }

    private static InventoryPositionIdentity ProcurementIdentity(Scenario scenario) =>
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

    private static CloseProcurementBatchExecution CloseExecution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        CloseProcurementBatchCommand command) =>
        new(CommandId.From(commandId), requestHash, ActorAccountId.From(actorAccountId), command);

    private static ReopenProcurementBatchExecution ReopenExecution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        ReopenProcurementBatchCommand command) =>
        new(CommandId.From(commandId), requestHash, ActorAccountId.From(actorAccountId), command);

    private static CommandRequestHash Hash(byte value)
    {
        var bytes = new byte[CommandRequestHash.Sha256Length];
        Array.Fill(bytes, value);
        return CommandRequestHash.FromSha256(bytes);
    }

    private static async ValueTask<ApplicationResult<CloseProcurementBatchResult>> ExecuteCloseAsync(
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

    private static async ValueTask<ApplicationResult<ReopenProcurementBatchResult>> ExecuteReopenAsync(
        ReopenProcurementBatchExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IReopenProcurementBatchExecutor>()
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

    private static async Task<Scenario> SeedAsync(
        decimal initialBalance,
        CancellationToken cancellationToken)
    {
        var scenario = new Scenario(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts(id,display_name,active,identity_issuer,identity_subject,created_at)
            VALUES(@actor_id,'P6 V8 reopen actor',true,NULL,NULL,@now);

            INSERT INTO infrastructure.warehouses(id,code,name_zh_tw,name_th_th,active,created_at,created_by_account_id)
            VALUES(@warehouse_id,NULL,'P6 V8 reopen warehouse',NULL,true,@now,@actor_id);

            INSERT INTO infrastructure.storage_locations(id,warehouse_id,code,name_zh_tw,name_th_th,active,created_at,created_by_account_id)
            VALUES(@location_id,@warehouse_id,NULL,'P6 V8 reopen location',NULL,true,@now,@actor_id);

            INSERT INTO party.suppliers(
                id,name_zh_tw,name_th_th,bank_name,bank_account,phone,address,
                active,row_version,created_at,created_by_account_id,deleted_at,deleted_by_account_id)
            VALUES(
                @supplier_id,'P6 V8 reopen supplier',NULL,NULL,NULL,NULL,NULL,
                true,1,@now,@actor_id,NULL,NULL);

            INSERT INTO product.procurement_products(
                id,name_zh_tw,name_th_th,unit_code,default_storage_location_id,
                active,row_version,created_at,created_by_account_id,deleted_at,deleted_by_account_id)
            VALUES(
                @product_id,'P6 V8 reopen product',NULL,'kg',@location_id,
                true,1,@now,@actor_id,NULL,NULL);

            INSERT INTO procurement.procurement_batches(
                id,procurement_date,procurement_product_id,procurement_status,lifecycle_status,
                processing_route_id,processing_route_version_id,completed_at,completed_by_account_id,
                closed_at,closed_by_account_id,row_version,created_at,created_by_account_id,deleted_at,deleted_by_account_id)
            VALUES(
                @batch_id,DATE '2026-09-04',@product_id,'OPEN','ACTIVE',
                NULL,NULL,NULL,NULL,NULL,NULL,1,@now,@actor_id,NULL,NULL);

            INSERT INTO inventory.inventory_positions(
                id,origin,procurement_batch_id,outsourced_supply_batch_id,inventory_object_kind,procurement_product_id,
                process_material_id,sales_product_id,storage_location_id,raw_source_kind,supplier_id,balance_quantity,row_version)
            VALUES(
                @position_id,'IN_HOUSE',@batch_id,NULL,'PROCUREMENT_PRODUCT',@product_id,
                NULL,NULL,@location_id,'SUPPLIER',@supplier_id,@balance,1);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("warehouse_id", scenario.WarehouseId),
            ("location_id", scenario.LocationId),
            ("supplier_id", scenario.SupplierId),
            ("product_id", scenario.ProcurementProductId),
            ("batch_id", scenario.BatchId),
            ("position_id", scenario.PositionId),
            ("balance", initialBalance),
            ("now", now));

        return scenario;
    }

    private static Task CleanupAsync(
        Scenario scenario,
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
            DELETE FROM inventory.inventory_operations WHERE procurement_batch_id = @batch_id OR recorded_by_account_id = @actor_id;
            DELETE FROM inventory.inventory_positions WHERE procurement_batch_id = @batch_id;
            DELETE FROM procurement.procurement_batches WHERE id = @batch_id;
            DELETE FROM product.procurement_products WHERE id = @product_id;
            DELETE FROM party.suppliers WHERE id = @supplier_id;
            DELETE FROM infrastructure.storage_locations WHERE id = @location_id;
            DELETE FROM infrastructure.warehouses WHERE id = @warehouse_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds.ToArray()),
            ("batch_id", scenario.BatchId),
            ("product_id", scenario.ProcurementProductId),
            ("supplier_id", scenario.SupplierId),
            ("location_id", scenario.LocationId),
            ("warehouse_id", scenario.WarehouseId),
            ("actor_id", scenario.ActorAccountId));

    private static Task<decimal> PositionBalanceAsync(Guid positionId, CancellationToken cancellationToken) =>
        ScalarAsync<decimal>(
            "SELECT balance_quantity FROM inventory.inventory_positions WHERE id = @position_id;",
            cancellationToken,
            ("position_id", positionId));

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
        Guid LocationId,
        Guid SupplierId,
        Guid ProcurementProductId,
        Guid BatchId,
        Guid PositionId);
}
