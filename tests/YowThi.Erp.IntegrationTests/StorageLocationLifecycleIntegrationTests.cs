using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Infrastructure;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class StorageLocationLifecycleIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString = "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Storage_location_soft_delete_replays_rejects_stale_and_restores_with_audit()
    {
        var ct = TestContext.Current.CancellationToken;
        var actorId = Guid.CreateVersion7();
        var warehouseId = Guid.CreateVersion7();
        var storageLocationId = Guid.CreateVersion7();
        var softDeleteCommandId = Guid.CreateVersion7();
        var staleCommandId = Guid.CreateVersion7();
        var restoreCommandId = Guid.CreateVersion7();
        var commandIds = new[] { softDeleteCommandId, staleCommandId, restoreCommandId };

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES (@actor, 'Storage location lifecycle actor', true, NULL, NULL, now());

            INSERT INTO infrastructure.warehouses
                (id, code, name_zh_tw, name_th_th, active, row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@warehouse, 'WH-SL-LIFE', '儲位生命週期倉庫', 'คลังวงจรตำแหน่ง', true, 1, now(), @actor, NULL, NULL);

            INSERT INTO infrastructure.storage_locations
                (id, warehouse_id, code, name_zh_tw, name_th_th, active, row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@location, @warehouse, 'SL-LIFECYCLE', '儲位生命週期', 'ตำแหน่งวงจร', true, 1, now(), @actor, NULL, NULL);
            """,
            ct,
            ("actor", actorId),
            ("warehouse", warehouseId),
            ("location", storageLocationId));

        try
        {
            var softDeleteExecution = new SoftDeleteStorageLocationExecution(
                CommandId.From(softDeleteCommandId),
                Hash(0xA1),
                ActorAccountId.From(actorId),
                new SoftDeleteStorageLocationCommand(storageLocationId, 1));

            var softDelete = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IStorageLocationLifecycleExecutor>()
                    .SoftDeleteAsync(softDeleteExecution, token),
                ct);
            Assert.True(softDelete.IsSuccess);
            Assert.Equal(storageLocationId, softDelete.Value.StorageLocationId);
            Assert.Equal(2, softDelete.Value.RowVersion);
            Assert.True(softDelete.Value.Deleted);

            Assert.Equal(2L, await ScalarAsync<long>(
                "SELECT row_version FROM infrastructure.storage_locations WHERE id = @id;",
                ct,
                ("id", storageLocationId)));
            Assert.Equal(actorId, await ScalarAsync<Guid>(
                "SELECT deleted_by_account_id FROM infrastructure.storage_locations WHERE id = @id;",
                ct,
                ("id", storageLocationId)));
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM infrastructure.storage_locations WHERE id = @id AND deleted_at IS NOT NULL;",
                ct,
                ("id", storageLocationId)));

            var replay = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IStorageLocationLifecycleExecutor>()
                    .SoftDeleteAsync(softDeleteExecution, token),
                ct);
            Assert.True(replay.IsSuccess);
            Assert.Equal(softDelete.Value, replay.Value);

            var stale = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IStorageLocationLifecycleExecutor>()
                    .RestoreAsync(new RestoreStorageLocationExecution(
                        CommandId.From(staleCommandId),
                        Hash(0xA2),
                        ActorAccountId.From(actorId),
                        new RestoreStorageLocationCommand(storageLocationId, 1)), token),
                ct);
            Assert.True(stale.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, stale.Error.Kind);
            Assert.Equal(StorageLocationLifecycleErrorCodes.StaleRowVersion, stale.Error.Code);
            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM system.command_executions WHERE command_id = @id;",
                ct,
                ("id", staleCommandId)));

            var deletedPage = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IStorageLocationMasterReader>().GetAsync(
                    new StorageLocationMasterQuery("SL-LIFECYCLE", InfrastructureMasterStatusFilter.Deleted, warehouseId, 0, 100), token),
                ct);
            var deleted = Assert.Single(deletedPage.Items);
            Assert.Equal(storageLocationId, deleted.Id);
            Assert.NotNull(deleted.DeletedAt);
            Assert.Equal(2, deleted.RowVersion);

            var restore = await ExecuteAsync(
                (services, token) => services.GetRequiredService<IStorageLocationLifecycleExecutor>()
                    .RestoreAsync(new RestoreStorageLocationExecution(
                        CommandId.From(restoreCommandId),
                        Hash(0xA3),
                        ActorAccountId.From(actorId),
                        new RestoreStorageLocationCommand(storageLocationId, 2)), token),
                ct);
            Assert.True(restore.IsSuccess);
            Assert.Equal(3, restore.Value.RowVersion);
            Assert.False(restore.Value.Deleted);

            Assert.Equal(3L, await ScalarAsync<long>(
                "SELECT row_version FROM infrastructure.storage_locations WHERE id = @id;",
                ct,
                ("id", storageLocationId)));
            Assert.Equal(1L, await ScalarAsync<long>(
                "SELECT count(*) FROM infrastructure.storage_locations WHERE id = @id AND deleted_at IS NULL AND deleted_by_account_id IS NULL;",
                ct,
                ("id", storageLocationId)));

            Assert.Equal(2L, await ScalarAsync<long>(
                "SELECT count(*) FROM system.command_executions WHERE command_id = ANY(@ids) AND status = 'SUCCEEDED';",
                ct,
                ("ids", new[] { softDeleteCommandId, restoreCommandId })));
            Assert.Equal(2L, await ScalarAsync<long>(
                "SELECT count(*) FROM audit.audit_events WHERE command_id = ANY(@ids) AND event_kind = 'DATA_LIFECYCLE';",
                ct,
                ("ids", new[] { softDeleteCommandId, restoreCommandId })));
            Assert.Equal(2L, await ScalarAsync<long>(
                """
                SELECT count(*)
                FROM audit.audit_event_subjects s
                JOIN audit.audit_events e ON e.id = s.audit_event_id
                WHERE e.command_id = ANY(@ids)
                  AND s.subject_kind = 'infrastructure.storage-location'
                  AND s.change_kind IN ('SOFT_DELETE', 'RESTORE');
                """,
                ct,
                ("ids", new[] { softDeleteCommandId, restoreCommandId })));
        }
        finally
        {
            await ExecuteNonQueryAsync(
                """
                DELETE FROM audit.audit_event_subjects
                WHERE audit_event_id IN (SELECT id FROM audit.audit_events WHERE command_id = ANY(@command_ids));
                DELETE FROM audit.audit_events WHERE command_id = ANY(@command_ids);
                DELETE FROM system.command_executions WHERE command_id = ANY(@command_ids);
                DELETE FROM infrastructure.storage_locations WHERE id = @location;
                DELETE FROM infrastructure.warehouses WHERE id = @warehouse;
                DELETE FROM system.accounts WHERE id = @actor;
                """,
                ct,
                ("command_ids", commandIds),
                ("location", storageLocationId),
                ("warehouse", warehouseId),
                ("actor", actorId));
        }
    }

    private static async Task<TResult> ExecuteAsync<TResult>(
        Func<IServiceProvider, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken ct)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await operation(scope.ServiceProvider, ct);
    }

    private static CommandRequestHash Hash(byte value)
    {
        var bytes = new byte[CommandRequestHash.Sha256Length];
        Array.Fill(bytes, value);
        return CommandRequestHash.FromSha256(bytes);
    }

    private static async Task<T> ScalarAsync<T>(
        string sql,
        CancellationToken ct,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }
        var value = await command.ExecuteScalarAsync(ct);
        return (T)(value ?? throw new InvalidOperationException("Expected scalar result."));
    }

    private static async Task ExecuteNonQueryAsync(
        string sql,
        CancellationToken ct,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string GetConnectionString() =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable))
            ? LocalDevelopmentConnectionString
            : Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)!;

    private static async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var builder = new NpgsqlConnectionStringBuilder(GetConnectionString())
        {
            Pooling = false,
            Timeout = 5,
            CommandTimeout = 15,
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
