using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Outsourced;
using YowThi.Erp.Application.Party;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class OutsourcedVendorLifecycleIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Soft_delete_with_historical_outsourced_batch_dependency_hides_vendor_then_restore_reexposes_it_with_audit_and_replay()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedVendorWithBatchHistoryAsync(active: true, cancellationToken);
        var softDeleteCommandId = Guid.CreateVersion7();
        var restoreCommandId = Guid.CreateVersion7();

        try
        {
            var before = await ReadVendorOptionsAsync(scenario.VendorName, cancellationToken);
            Assert.Contains(before.Items, item => item.Id == scenario.VendorId);

            var softDeleteExecution = new SoftDeleteOutsourcedVendorExecution(
                CommandId.From(softDeleteCommandId),
                Hash(1),
                ActorAccountId.From(scenario.ActorAccountId),
                new SoftDeleteOutsourcedVendorCommand(scenario.VendorId, 1));
            var softDelete = await ExecuteSoftDeleteAsync(softDeleteExecution, cancellationToken);

            Assert.True(softDelete.IsSuccess);
            Assert.Equal(new OutsourcedVendorLifecycleResult(scenario.VendorId, 2, true), softDelete.Value);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM party.outsourced_vendors
                    WHERE id = @vendor_id
                      AND deleted_at IS NOT NULL
                      AND deleted_by_account_id = @actor_id
                      AND active = true
                      AND row_version = 2;
                    """,
                    cancellationToken,
                    ("vendor_id", scenario.VendorId),
                    ("actor_id", scenario.ActorAccountId)));

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM outsourced.outsourced_supply_batches
                    WHERE id = @batch_id
                      AND outsourced_vendor_id = @vendor_id;
                    """,
                    cancellationToken,
                    ("batch_id", scenario.BatchId),
                    ("vendor_id", scenario.VendorId)));

            var hidden = await ReadVendorOptionsAsync(scenario.VendorName, cancellationToken);
            Assert.DoesNotContain(hidden.Items, item => item.Id == scenario.VendorId);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.audit_event_subjects s
                    JOIN audit.audit_events e ON e.id = s.audit_event_id
                    WHERE e.command_id = @command_id
                      AND e.command_type = 'SoftDeleteOutsourcedVendor'
                      AND e.event_kind = 'DATA_LIFECYCLE'
                      AND s.subject_kind = 'party.outsourced-vendor'
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
            Assert.Equal(OutsourcedVendorLifecycleErrorCodes.IdempotencyKeyReused, changedHash.Error.Code);

            var restoreExecution = new RestoreOutsourcedVendorExecution(
                CommandId.From(restoreCommandId),
                Hash(3),
                ActorAccountId.From(scenario.ActorAccountId),
                new RestoreOutsourcedVendorCommand(scenario.VendorId, 2));
            var restore = await ExecuteRestoreAsync(restoreExecution, cancellationToken);

            Assert.True(restore.IsSuccess);
            Assert.Equal(new OutsourcedVendorLifecycleResult(scenario.VendorId, 3, false), restore.Value);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM party.outsourced_vendors
                    WHERE id = @vendor_id
                      AND deleted_at IS NULL
                      AND deleted_by_account_id IS NULL
                      AND active = true
                      AND row_version = 3;
                    """,
                    cancellationToken,
                    ("vendor_id", scenario.VendorId)));

            var visibleAgain = await ReadVendorOptionsAsync(scenario.VendorName, cancellationToken);
            Assert.Contains(visibleAgain.Items, item => item.Id == scenario.VendorId);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.audit_event_subjects s
                    JOIN audit.audit_events e ON e.id = s.audit_event_id
                    WHERE e.command_id = @command_id
                      AND e.command_type = 'RestoreOutsourcedVendor'
                      AND e.event_kind = 'DATA_LIFECYCLE'
                      AND s.subject_kind = 'party.outsourced-vendor'
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
    public async Task Restore_preserves_inactive_state()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedVendorWithBatchHistoryAsync(active: false, cancellationToken);
        var softDeleteCommandId = Guid.CreateVersion7();
        var restoreCommandId = Guid.CreateVersion7();

        try
        {
            var softDelete = await ExecuteSoftDeleteAsync(
                new SoftDeleteOutsourcedVendorExecution(
                    CommandId.From(softDeleteCommandId),
                    Hash(4),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new SoftDeleteOutsourcedVendorCommand(scenario.VendorId, 1)),
                cancellationToken);
            Assert.True(softDelete.IsSuccess);

            var restore = await ExecuteRestoreAsync(
                new RestoreOutsourcedVendorExecution(
                    CommandId.From(restoreCommandId),
                    Hash(5),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new RestoreOutsourcedVendorCommand(scenario.VendorId, 2)),
                cancellationToken);
            Assert.True(restore.IsSuccess);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM party.outsourced_vendors
                    WHERE id = @vendor_id
                      AND active = false
                      AND deleted_at IS NULL
                      AND deleted_by_account_id IS NULL
                      AND row_version = 3;
                    """,
                    cancellationToken,
                    ("vendor_id", scenario.VendorId)));

            var options = await ReadVendorOptionsAsync(scenario.VendorName, cancellationToken);
            Assert.DoesNotContain(options.Items, item => item.Id == scenario.VendorId);
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
        var scenario = await SeedVendorWithBatchHistoryAsync(active: true, cancellationToken);
        var staleCommandId = Guid.CreateVersion7();
        var restoreCurrentCommandId = Guid.CreateVersion7();

        try
        {
            var stale = await ExecuteSoftDeleteAsync(
                new SoftDeleteOutsourcedVendorExecution(
                    CommandId.From(staleCommandId),
                    Hash(6),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new SoftDeleteOutsourcedVendorCommand(scenario.VendorId, 2)),
                cancellationToken);
            Assert.True(stale.IsFailure);
            Assert.Equal(OutsourcedVendorLifecycleErrorCodes.StaleRowVersion, stale.Error.Code);

            var restoreCurrent = await ExecuteRestoreAsync(
                new RestoreOutsourcedVendorExecution(
                    CommandId.From(restoreCurrentCommandId),
                    Hash(7),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new RestoreOutsourcedVendorCommand(scenario.VendorId, 1)),
                cancellationToken);
            Assert.True(restoreCurrent.IsFailure);
            Assert.Equal(OutsourcedVendorLifecycleErrorCodes.NotDeleted, restoreCurrent.Error.Code);

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

    private static async ValueTask<ApplicationResult<OutsourcedVendorLifecycleResult>> ExecuteSoftDeleteAsync(
        SoftDeleteOutsourcedVendorExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IOutsourcedVendorLifecycleExecutor>()
            .SoftDeleteAsync(execution, cancellationToken);
    }

    private static async ValueTask<ApplicationResult<OutsourcedVendorLifecycleResult>> ExecuteRestoreAsync(
        RestoreOutsourcedVendorExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IOutsourcedVendorLifecycleExecutor>()
            .RestoreAsync(execution, cancellationToken);
    }

    private static async ValueTask<OutsourcedSupplyDetailOptionPage<OutsourcedVendorOption>> ReadVendorOptionsAsync(
        string search,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IOutsourcedSupplyDetailOptionsReader>()
            .GetVendorsAsync(
                new OutsourcedSupplyDetailOptionsQuery("zh-TW", search, 0, 100),
                cancellationToken);
    }

    private static CommandRequestHash Hash(byte value)
    {
        var bytes = new byte[CommandRequestHash.Sha256Length];
        Array.Fill(bytes, value);
        return CommandRequestHash.FromSha256(bytes);
    }

    private static async Task<Scenario> SeedVendorWithBatchHistoryAsync(
        bool active,
        CancellationToken cancellationToken)
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var scenario = new Scenario(
            ActorAccountId: Guid.CreateVersion7(),
            VendorId: Guid.CreateVersion7(),
            BatchId: Guid.CreateVersion7(),
            VendorName: $"P6 V8 C10 vendor {suffix}");
        var now = DateTimeOffset.UtcNow;

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 V8 C10 lifecycle actor', true, NULL, NULL, @now);

            INSERT INTO party.outsourced_vendors
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, row_version, created_at, created_by_account_id)
            VALUES
                (@vendor_id, @vendor_name, NULL, NULL, NULL, NULL, NULL,
                 @active, 1, @now, @actor_id);

            INSERT INTO outsourced.outsourced_supply_batches
                (id, supply_date, outsourced_vendor_id, lifecycle_status,
                 closed_at, closed_by_account_id, row_version,
                 created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@batch_id, DATE '2026-09-04', @vendor_id, 'ACTIVE',
                 NULL, NULL, 1,
                 @now, @actor_id, NULL, NULL);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("vendor_id", scenario.VendorId),
            ("vendor_name", scenario.VendorName),
            ("active", active),
            ("batch_id", scenario.BatchId),
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
            DELETE FROM outsourced.outsourced_supply_batches WHERE id = @batch_id;
            DELETE FROM party.outsourced_vendors WHERE id = @vendor_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds.ToArray()),
            ("batch_id", scenario.BatchId),
            ("vendor_id", scenario.VendorId),
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
        Guid VendorId,
        Guid BatchId,
        string VendorName);
}
