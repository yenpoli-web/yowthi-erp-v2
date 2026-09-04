using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Party;
using YowThi.Erp.Application.Procurement;
using YowThi.Erp.Domain.Procurement;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class FarmerLifecycleIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Soft_delete_with_historical_procurement_dependency_hides_farmer_then_restore_reexposes_it_with_audit_and_replay()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedFarmerWithProcurementHistoryAsync(active: true, cancellationToken);
        var softDeleteCommandId = Guid.CreateVersion7();
        var restoreCommandId = Guid.CreateVersion7();

        try
        {
            var before = await ReadFarmerOptionsAsync(scenario.FarmerName, cancellationToken);
            Assert.Contains(before.Items, item => item.Id == scenario.FarmerId);

            var softDeleteExecution = new SoftDeleteFarmerExecution(
                CommandId.From(softDeleteCommandId),
                Hash(1),
                ActorAccountId.From(scenario.ActorAccountId),
                new SoftDeleteFarmerCommand(scenario.FarmerId, 1));
            var softDelete = await ExecuteSoftDeleteAsync(softDeleteExecution, cancellationToken);

            Assert.True(softDelete.IsSuccess);
            Assert.Equal(new FarmerLifecycleResult(scenario.FarmerId, 2, true), softDelete.Value);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM party.farmers
                    WHERE id = @farmer_id
                      AND deleted_at IS NOT NULL
                      AND deleted_by_account_id = @actor_id
                      AND active = true
                      AND row_version = 2;
                    """,
                    cancellationToken,
                    ("farmer_id", scenario.FarmerId),
                    ("actor_id", scenario.ActorAccountId)));

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM procurement.procurement_entries
                    WHERE id = @entry_id
                      AND source_type = 'FARMER'
                      AND farmer_id = @farmer_id
                      AND supplier_id IS NULL;
                    """,
                    cancellationToken,
                    ("entry_id", scenario.ProcurementEntryId),
                    ("farmer_id", scenario.FarmerId)));

            var hidden = await ReadFarmerOptionsAsync(scenario.FarmerName, cancellationToken);
            Assert.DoesNotContain(hidden.Items, item => item.Id == scenario.FarmerId);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.audit_event_subjects s
                    JOIN audit.audit_events e ON e.id = s.audit_event_id
                    WHERE e.command_id = @command_id
                      AND e.command_type = 'SoftDeleteFarmer'
                      AND e.event_kind = 'DATA_LIFECYCLE'
                      AND s.subject_kind = 'party.farmer'
                      AND s.change_kind = 'SOFT_DELETE'
                      AND s.before_row_version = 1
                      AND s.after_row_version = 2;
                    """,
                    cancellationToken,
                    ("command_id", softDeleteCommandId)));

            var replay = await ExecuteSoftDeleteAsync(softDeleteExecution, cancellationToken);
            Assert.True(replay.IsSuccess);
            Assert.Equal(softDelete.Value, replay.Value);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", softDeleteCommandId)));

            var changedHash = await ExecuteSoftDeleteAsync(
                softDeleteExecution with { RequestHash = Hash(2) },
                cancellationToken);
            Assert.True(changedHash.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, changedHash.Error.Kind);
            Assert.Equal(FarmerLifecycleErrorCodes.IdempotencyKeyReused, changedHash.Error.Code);

            var restoreExecution = new RestoreFarmerExecution(
                CommandId.From(restoreCommandId),
                Hash(3),
                ActorAccountId.From(scenario.ActorAccountId),
                new RestoreFarmerCommand(scenario.FarmerId, 2));
            var restore = await ExecuteRestoreAsync(restoreExecution, cancellationToken);

            Assert.True(restore.IsSuccess);
            Assert.Equal(new FarmerLifecycleResult(scenario.FarmerId, 3, false), restore.Value);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM party.farmers
                    WHERE id = @farmer_id
                      AND deleted_at IS NULL
                      AND deleted_by_account_id IS NULL
                      AND active = true
                      AND row_version = 3;
                    """,
                    cancellationToken,
                    ("farmer_id", scenario.FarmerId)));

            var visibleAgain = await ReadFarmerOptionsAsync(scenario.FarmerName, cancellationToken);
            Assert.Contains(visibleAgain.Items, item => item.Id == scenario.FarmerId);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.audit_event_subjects s
                    JOIN audit.audit_events e ON e.id = s.audit_event_id
                    WHERE e.command_id = @command_id
                      AND e.command_type = 'RestoreFarmer'
                      AND e.event_kind = 'DATA_LIFECYCLE'
                      AND s.subject_kind = 'party.farmer'
                      AND s.change_kind = 'RESTORE'
                      AND s.before_row_version = 2
                      AND s.after_row_version = 3;
                    """,
                    cancellationToken,
                    ("command_id", restoreCommandId)));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, [softDeleteCommandId, restoreCommandId], cancellationToken);
        }
    }

    [Fact]
    public async Task Restore_preserves_inactive_state_and_farmer_remains_unavailable_for_new_procurement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedFarmerWithProcurementHistoryAsync(active: false, cancellationToken);
        var softDeleteCommandId = Guid.CreateVersion7();
        var restoreCommandId = Guid.CreateVersion7();

        try
        {
            var softDelete = await ExecuteSoftDeleteAsync(
                new SoftDeleteFarmerExecution(
                    CommandId.From(softDeleteCommandId),
                    Hash(4),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new SoftDeleteFarmerCommand(scenario.FarmerId, 1)),
                cancellationToken);
            Assert.True(softDelete.IsSuccess);

            var restore = await ExecuteRestoreAsync(
                new RestoreFarmerExecution(
                    CommandId.From(restoreCommandId),
                    Hash(5),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new RestoreFarmerCommand(scenario.FarmerId, 2)),
                cancellationToken);
            Assert.True(restore.IsSuccess);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM party.farmers
                    WHERE id = @farmer_id
                      AND active = false
                      AND deleted_at IS NULL
                      AND deleted_by_account_id IS NULL
                      AND row_version = 3;
                    """,
                    cancellationToken,
                    ("farmer_id", scenario.FarmerId)));

            var options = await ReadFarmerOptionsAsync(scenario.FarmerName, cancellationToken);
            Assert.DoesNotContain(options.Items, item => item.Id == scenario.FarmerId);
        }
        finally
        {
            await CleanupScenarioAsync(scenario, [softDeleteCommandId, restoreCommandId], cancellationToken);
        }
    }

    [Fact]
    public async Task Lifecycle_state_and_stale_conflicts_roll_back_command_acquisition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedFarmerWithProcurementHistoryAsync(active: true, cancellationToken);
        var staleCommandId = Guid.CreateVersion7();
        var restoreCurrentCommandId = Guid.CreateVersion7();

        try
        {
            var stale = await ExecuteSoftDeleteAsync(
                new SoftDeleteFarmerExecution(
                    CommandId.From(staleCommandId),
                    Hash(6),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new SoftDeleteFarmerCommand(scenario.FarmerId, 2)),
                cancellationToken);
            Assert.True(stale.IsFailure);
            Assert.Equal(FarmerLifecycleErrorCodes.StaleRowVersion, stale.Error.Code);

            var restoreCurrent = await ExecuteRestoreAsync(
                new RestoreFarmerExecution(
                    CommandId.From(restoreCurrentCommandId),
                    Hash(7),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new RestoreFarmerCommand(scenario.FarmerId, 1)),
                cancellationToken);
            Assert.True(restoreCurrent.IsFailure);
            Assert.Equal(FarmerLifecycleErrorCodes.NotDeleted, restoreCurrent.Error.Code);

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
            await CleanupScenarioAsync(scenario, [staleCommandId, restoreCurrentCommandId], cancellationToken);
        }
    }

    private static async ValueTask<ApplicationResult<FarmerLifecycleResult>> ExecuteSoftDeleteAsync(
        SoftDeleteFarmerExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IFarmerLifecycleExecutor>()
            .SoftDeleteAsync(execution, cancellationToken);
    }

    private static async ValueTask<ApplicationResult<FarmerLifecycleResult>> ExecuteRestoreAsync(
        RestoreFarmerExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IFarmerLifecycleExecutor>()
            .RestoreAsync(execution, cancellationToken);
    }

    private static async ValueTask<ProcurementEntryOptionPage<ProcurementSourceOption>> ReadFarmerOptionsAsync(
        string search,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IProcurementEntryOptionsReader>()
            .GetSourcesAsync(
                ProcurementSourceType.FARMER,
                new ProcurementEntryOptionsQuery("zh-TW", search, 0, 100),
                cancellationToken);
    }

    private static CommandRequestHash Hash(byte value)
    {
        var bytes = new byte[CommandRequestHash.Sha256Length];
        Array.Fill(bytes, value);
        return CommandRequestHash.FromSha256(bytes);
    }

    private static async Task<Scenario> SeedFarmerWithProcurementHistoryAsync(
        bool active,
        CancellationToken cancellationToken)
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var scenario = new Scenario(
            ActorAccountId: Guid.CreateVersion7(),
            FarmerId: Guid.CreateVersion7(),
            WarehouseId: Guid.CreateVersion7(),
            LocationId: Guid.CreateVersion7(),
            ProcurementProductId: Guid.CreateVersion7(),
            ProcurementBatchId: Guid.CreateVersion7(),
            ProcurementEntryId: Guid.CreateVersion7(),
            FarmerName: $"P6 V8 C11 farmer {suffix}");
        var now = DateTimeOffset.UtcNow;

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 V8 C11 lifecycle actor', true, NULL, NULL, @now);

            INSERT INTO party.farmers
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, row_version, created_at, created_by_account_id)
            VALUES
                (@farmer_id, @farmer_name, NULL, NULL, NULL, NULL, NULL,
                 @active, 1, @now, @actor_id);

            INSERT INTO infrastructure.warehouses
                (id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@warehouse_id, NULL, 'P6 V8 C11 warehouse', NULL, true, @now, @actor_id);

            INSERT INTO infrastructure.storage_locations
                (id, warehouse_id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@location_id, @warehouse_id, NULL, 'P6 V8 C11 location', NULL, true, @now, @actor_id);

            INSERT INTO product.procurement_products
                (id, name_zh_tw, name_th_th, unit_code, default_storage_location_id,
                 active, created_at, created_by_account_id)
            VALUES
                (@product_id, 'P6 V8 C11 product', NULL, 'kg', @location_id,
                 true, @now, @actor_id);

            INSERT INTO procurement.procurement_batches
                (id, procurement_date, procurement_product_id, procurement_status,
                 lifecycle_status, created_at, created_by_account_id)
            VALUES
                (@batch_id, DATE '2026-09-04', @product_id, 'OPEN',
                 'ACTIVE', @now, @actor_id);

            INSERT INTO procurement.procurement_entries
                (id, procurement_batch_id, source_type, supplier_id, farmer_id,
                 net_quantity, unit_code_snapshot, unit_price, amount_thb,
                 company_pickup, recorded_at, recorded_by_account_id, row_version)
            VALUES
                (@entry_id, @batch_id, 'FARMER', NULL, @farmer_id,
                 1, 'kg', 1, 1,
                 false, @now, @actor_id, 1);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("farmer_id", scenario.FarmerId),
            ("farmer_name", scenario.FarmerName),
            ("active", active),
            ("warehouse_id", scenario.WarehouseId),
            ("location_id", scenario.LocationId),
            ("product_id", scenario.ProcurementProductId),
            ("batch_id", scenario.ProcurementBatchId),
            ("entry_id", scenario.ProcurementEntryId),
            ("now", now));

        return scenario;
    }

    private static Task CleanupScenarioAsync(
        Scenario scenario,
        IReadOnlyCollection<Guid> commandIds,
        CancellationToken cancellationToken) =>
        ExecuteNonQueryAsync(
            """
            DELETE FROM audit.audit_event_subjects
            WHERE audit_event_id IN (SELECT id FROM audit.audit_events WHERE command_id = ANY(@command_ids));
            DELETE FROM audit.audit_events WHERE command_id = ANY(@command_ids);
            DELETE FROM system.command_executions WHERE command_id = ANY(@command_ids);
            DELETE FROM procurement.procurement_entries WHERE id = @entry_id;
            DELETE FROM procurement.procurement_batches WHERE id = @batch_id;
            DELETE FROM product.procurement_products WHERE id = @product_id;
            DELETE FROM party.farmers WHERE id = @farmer_id;
            DELETE FROM infrastructure.storage_locations WHERE id = @location_id;
            DELETE FROM infrastructure.warehouses WHERE id = @warehouse_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds.ToArray()),
            ("entry_id", scenario.ProcurementEntryId),
            ("batch_id", scenario.ProcurementBatchId),
            ("product_id", scenario.ProcurementProductId),
            ("farmer_id", scenario.FarmerId),
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

    private sealed record Scenario(
        Guid ActorAccountId,
        Guid FarmerId,
        Guid WarehouseId,
        Guid LocationId,
        Guid ProcurementProductId,
        Guid ProcurementBatchId,
        Guid ProcurementEntryId,
        string FarmerName);
}
