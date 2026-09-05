using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Infrastructure;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class WarehouseLifecycleIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Soft_delete_preserves_child_storage_location_then_restore_with_audit_and_replay()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(warehouseActive: true, cancellationToken);
        var softDeleteCommandId = Guid.CreateVersion7();
        var restoreCommandId = Guid.CreateVersion7();

        try
        {
            var execution = new SoftDeleteWarehouseExecution(
                CommandId.From(softDeleteCommandId),
                Hash(1),
                ActorAccountId.From(scenario.ActorAccountId),
                new SoftDeleteWarehouseCommand(scenario.WarehouseId, 1));

            var softDelete = await ExecuteSoftDeleteAsync(execution, cancellationToken);
            Assert.True(softDelete.IsSuccess);
            Assert.Equal(new WarehouseLifecycleResult(scenario.WarehouseId, 2, true), softDelete.Value);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM infrastructure.warehouses
                    WHERE id = @warehouse_id
                      AND active = true
                      AND deleted_at IS NOT NULL
                      AND deleted_by_account_id = @actor_id
                      AND row_version = 2;
                    """,
                    cancellationToken,
                    ("warehouse_id", scenario.WarehouseId),
                    ("actor_id", scenario.ActorAccountId)));

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM infrastructure.storage_locations
                    WHERE id = @location_id
                      AND warehouse_id = @warehouse_id
                      AND active = true
                      AND deleted_at IS NULL
                      AND deleted_by_account_id IS NULL
                      AND row_version = 1;
                    """,
                    cancellationToken,
                    ("location_id", scenario.StorageLocationId),
                    ("warehouse_id", scenario.WarehouseId)));

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.audit_event_subjects s
                    JOIN audit.audit_events e ON e.id = s.audit_event_id
                    WHERE e.command_id = @command_id
                      AND e.command_type = 'SoftDeleteWarehouse'
                      AND e.event_kind = 'DATA_LIFECYCLE'
                      AND s.subject_kind = 'infrastructure.warehouse'
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
            Assert.Equal(WarehouseLifecycleErrorCodes.IdempotencyKeyReused, changedHash.Error.Code);

            var restore = await ExecuteRestoreAsync(
                new RestoreWarehouseExecution(
                    CommandId.From(restoreCommandId),
                    Hash(3),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new RestoreWarehouseCommand(scenario.WarehouseId, 2)),
                cancellationToken);
            Assert.True(restore.IsSuccess);
            Assert.Equal(new WarehouseLifecycleResult(scenario.WarehouseId, 3, false), restore.Value);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM infrastructure.warehouses
                    WHERE id = @warehouse_id
                      AND active = true
                      AND deleted_at IS NULL
                      AND deleted_by_account_id IS NULL
                      AND row_version = 3;
                    """,
                    cancellationToken,
                    ("warehouse_id", scenario.WarehouseId)));

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM infrastructure.storage_locations
                    WHERE id = @location_id
                      AND warehouse_id = @warehouse_id
                      AND active = true
                      AND deleted_at IS NULL
                      AND row_version = 1;
                    """,
                    cancellationToken,
                    ("location_id", scenario.StorageLocationId),
                    ("warehouse_id", scenario.WarehouseId)));

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.audit_event_subjects s
                    JOIN audit.audit_events e ON e.id = s.audit_event_id
                    WHERE e.command_id = @command_id
                      AND e.command_type = 'RestoreWarehouse'
                      AND e.event_kind = 'DATA_LIFECYCLE'
                      AND s.subject_kind = 'infrastructure.warehouse'
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
            await CleanupScenarioAsync(
                scenario,
                [softDeleteCommandId, restoreCommandId],
                cancellationToken);
        }
    }

    [Fact]
    public async Task Restore_preserves_preexisting_inactive_warehouse_and_does_not_mutate_child_location()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(warehouseActive: false, cancellationToken);
        var softDeleteCommandId = Guid.CreateVersion7();
        var restoreCommandId = Guid.CreateVersion7();

        try
        {
            var softDelete = await ExecuteSoftDeleteAsync(
                new SoftDeleteWarehouseExecution(
                    CommandId.From(softDeleteCommandId),
                    Hash(4),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new SoftDeleteWarehouseCommand(scenario.WarehouseId, 1)),
                cancellationToken);
            Assert.True(softDelete.IsSuccess);

            var restore = await ExecuteRestoreAsync(
                new RestoreWarehouseExecution(
                    CommandId.From(restoreCommandId),
                    Hash(5),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new RestoreWarehouseCommand(scenario.WarehouseId, 2)),
                cancellationToken);
            Assert.True(restore.IsSuccess);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM infrastructure.warehouses
                    WHERE id = @warehouse_id
                      AND active = false
                      AND deleted_at IS NULL
                      AND deleted_by_account_id IS NULL
                      AND row_version = 3;
                    """,
                    cancellationToken,
                    ("warehouse_id", scenario.WarehouseId)));

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM infrastructure.storage_locations
                    WHERE id = @location_id
                      AND warehouse_id = @warehouse_id
                      AND active = true
                      AND deleted_at IS NULL
                      AND deleted_by_account_id IS NULL
                      AND row_version = 1;
                    """,
                    cancellationToken,
                    ("location_id", scenario.StorageLocationId),
                    ("warehouse_id", scenario.WarehouseId)));
        }
        finally
        {
            await CleanupScenarioAsync(
                scenario,
                [softDeleteCommandId, restoreCommandId],
                cancellationToken);
        }
    }

    [Fact]
    public async Task Lifecycle_state_and_stale_conflicts_roll_back_command_acquisition_and_audit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(warehouseActive: true, cancellationToken);
        var staleCommandId = Guid.CreateVersion7();
        var restoreCurrentCommandId = Guid.CreateVersion7();

        try
        {
            var stale = await ExecuteSoftDeleteAsync(
                new SoftDeleteWarehouseExecution(
                    CommandId.From(staleCommandId),
                    Hash(6),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new SoftDeleteWarehouseCommand(scenario.WarehouseId, 2)),
                cancellationToken);
            Assert.True(stale.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, stale.Error.Kind);
            Assert.Equal(WarehouseLifecycleErrorCodes.StaleRowVersion, stale.Error.Code);

            var restoreCurrent = await ExecuteRestoreAsync(
                new RestoreWarehouseExecution(
                    CommandId.From(restoreCurrentCommandId),
                    Hash(7),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new RestoreWarehouseCommand(scenario.WarehouseId, 1)),
                cancellationToken);
            Assert.True(restoreCurrent.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, restoreCurrent.Error.Kind);
            Assert.Equal(WarehouseLifecycleErrorCodes.NotDeleted, restoreCurrent.Error.Code);

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

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM infrastructure.storage_locations
                    WHERE id = @location_id
                      AND warehouse_id = @warehouse_id
                      AND active = true
                      AND deleted_at IS NULL
                      AND row_version = 1;
                    """,
                    cancellationToken,
                    ("location_id", scenario.StorageLocationId),
                    ("warehouse_id", scenario.WarehouseId)));
        }
        finally
        {
            await CleanupScenarioAsync(
                scenario,
                [staleCommandId, restoreCurrentCommandId],
                cancellationToken);
        }
    }

    private static async ValueTask<ApplicationResult<WarehouseLifecycleResult>> ExecuteSoftDeleteAsync(
        SoftDeleteWarehouseExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IWarehouseLifecycleExecutor>()
            .SoftDeleteAsync(execution, cancellationToken);
    }

    private static async ValueTask<ApplicationResult<WarehouseLifecycleResult>> ExecuteRestoreAsync(
        RestoreWarehouseExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IWarehouseLifecycleExecutor>()
            .RestoreAsync(execution, cancellationToken);
    }

    private static CommandRequestHash Hash(byte value)
    {
        var bytes = new byte[CommandRequestHash.Sha256Length];
        Array.Fill(bytes, value);
        return CommandRequestHash.FromSha256(bytes);
    }

    private static async Task<Scenario> SeedScenarioAsync(
        bool warehouseActive,
        CancellationToken cancellationToken)
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var scenario = new Scenario(
            ActorAccountId: Guid.CreateVersion7(),
            WarehouseId: Guid.CreateVersion7(),
            StorageLocationId: Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 V8 C16 warehouse actor', true, NULL, NULL, @now);

            INSERT INTO infrastructure.warehouses
                (id, code, name_zh_tw, name_th_th,
                 active, row_version, created_at, created_by_account_id)
            VALUES
                (@warehouse_id, NULL, @warehouse_name, NULL,
                 @warehouse_active, 1, @now, @actor_id);

            INSERT INTO infrastructure.storage_locations
                (id, warehouse_id, code, name_zh_tw, name_th_th,
                 active, row_version, created_at, created_by_account_id)
            VALUES
                (@location_id, @warehouse_id, NULL, @location_name, NULL,
                 true, 1, @now, @actor_id);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("warehouse_id", scenario.WarehouseId),
            ("warehouse_name", $"P6 V8 C16 warehouse {suffix}"),
            ("warehouse_active", warehouseActive),
            ("location_id", scenario.StorageLocationId),
            ("location_name", $"P6 V8 C16 location {suffix}"),
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
            DELETE FROM system.outbox_messages WHERE command_id = ANY(@command_ids);
            DELETE FROM system.command_executions WHERE command_id = ANY(@command_ids);
            DELETE FROM infrastructure.storage_locations WHERE id = @location_id;
            DELETE FROM infrastructure.warehouses WHERE id = @warehouse_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds.ToArray()),
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

    private sealed record Scenario(
        Guid ActorAccountId,
        Guid WarehouseId,
        Guid StorageLocationId);
}
