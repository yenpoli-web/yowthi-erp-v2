using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.DataProtection;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class OutsourcedVendorHardDeleteIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Hard_delete_physically_removes_vendor_retains_audit_and_replays_after_target_is_gone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedVendorAsync(withBatch: false, cancellationToken);
        var commandId = Guid.CreateVersion7();
        var command = new HardDeleteOutsourcedVendorCommand(scenario.OutsourcedVendorId, 1);
        var execution = Execution(commandId, scenario.ActorAccountId, Hash(1), command);

        try
        {
            var result = await ExecuteAsync(execution, cancellationToken);
            Assert.True(result.IsSuccess);
            Assert.Equal(scenario.OutsourcedVendorId, result.Value.OutsourcedVendorId);
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM party.outsourced_vendors WHERE id = @vendor_id;",
                    cancellationToken,
                    ("vendor_id", scenario.OutsourcedVendorId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.audit_event_subjects s
                    JOIN audit.audit_events e ON e.id = s.audit_event_id
                    WHERE e.command_id = @command_id
                      AND e.command_type = 'HardDeleteOutsourcedVendor'
                      AND e.event_kind = 'HARD_DELETE'
                      AND s.subject_kind = 'party.outsourced-vendor'
                      AND s.change_kind = 'HARD_DELETE'
                      AND s.subject_key ->> 'id' = @vendor_id_text
                      AND s.before_row_version = 1
                      AND s.after_row_version IS NULL;
                    """,
                    cancellationToken,
                    ("command_id", commandId),
                    ("vendor_id_text", scenario.OutsourcedVendorId.ToString())));

            var replay = await ExecuteAsync(execution, cancellationToken);
            Assert.True(replay.IsSuccess);
            Assert.Equal(result.Value, replay.Value);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", commandId)));

            var changedHash = await ExecuteAsync(
                Execution(commandId, scenario.ActorAccountId, Hash(2), command),
                cancellationToken);
            Assert.True(changedHash.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, changedHash.Error.Kind);
            Assert.Equal(OutsourcedVendorHardDeleteErrorCodes.IdempotencyKeyReused, changedHash.Error.Code);
        }
        finally
        {
            await CleanupAsync(scenario, [commandId], cancellationToken);
        }
    }

    [Fact]
    public async Task Hard_delete_blocks_any_existing_outsourced_supply_batch_without_cascade_or_command_residue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedVendorAsync(withBatch: true, cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            var result = await ExecuteAsync(
                Execution(
                    commandId,
                    scenario.ActorAccountId,
                    Hash(3),
                    new HardDeleteOutsourcedVendorCommand(scenario.OutsourcedVendorId, 1)),
                cancellationToken);

            Assert.True(result.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, result.Error.Kind);
            Assert.Equal(OutsourcedVendorHardDeleteErrorCodes.DependencyBlocked, result.Error.Code);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM party.outsourced_vendors WHERE id = @vendor_id;",
                    cancellationToken,
                    ("vendor_id", scenario.OutsourcedVendorId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM outsourced.outsourced_supply_batches
                    WHERE id = @batch_id
                      AND outsourced_vendor_id = @vendor_id
                      AND lifecycle_status = 'ACTIVE'
                      AND row_version = 1;
                    """,
                    cancellationToken,
                    ("batch_id", scenario.OutsourcedSupplyBatchId!.Value),
                    ("vendor_id", scenario.OutsourcedVendorId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", commandId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", commandId)));
        }
        finally
        {
            await CleanupAsync(scenario, [commandId], cancellationToken);
        }
    }

    [Fact]
    public async Task Hard_delete_rejects_stale_row_version_and_rolls_back_command_acquisition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedVendorAsync(withBatch: false, cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            var result = await ExecuteAsync(
                Execution(
                    commandId,
                    scenario.ActorAccountId,
                    Hash(4),
                    new HardDeleteOutsourcedVendorCommand(scenario.OutsourcedVendorId, 2)),
                cancellationToken);

            Assert.True(result.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, result.Error.Kind);
            Assert.Equal(OutsourcedVendorHardDeleteErrorCodes.StaleRowVersion, result.Error.Code);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM party.outsourced_vendors WHERE id = @vendor_id;",
                    cancellationToken,
                    ("vendor_id", scenario.OutsourcedVendorId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", commandId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", commandId)));
        }
        finally
        {
            await CleanupAsync(scenario, [commandId], cancellationToken);
        }
    }

    private static HardDeleteOutsourcedVendorExecution Execution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        HardDeleteOutsourcedVendorCommand command) =>
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

    private static async ValueTask<ApplicationResult<HardDeleteOutsourcedVendorResult>> ExecuteAsync(
        HardDeleteOutsourcedVendorExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IHardDeleteOutsourcedVendorExecutor>()
            .ExecuteAsync(execution, cancellationToken);
    }

    private static async Task<Scenario> SeedVendorAsync(
        bool withBatch,
        CancellationToken cancellationToken)
    {
        var scenario = new Scenario(
            ActorAccountId: Guid.CreateVersion7(),
            OutsourcedVendorId: Guid.CreateVersion7(),
            OutsourcedSupplyBatchId: withBatch ? Guid.CreateVersion7() : null);
        var now = DateTimeOffset.UtcNow;
        var suffix = scenario.OutsourcedVendorId.ToString("N");

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 V8 C17 outsourced vendor hard delete actor', true, NULL, NULL, @now);

            INSERT INTO party.outsourced_vendors
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, row_version, created_at, created_by_account_id)
            VALUES
                (@vendor_id, @vendor_name, NULL, NULL, NULL, NULL, NULL,
                 true, 1, @now, @actor_id);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("vendor_id", scenario.OutsourcedVendorId),
            ("vendor_name", $"P6 V8 C17 vendor {suffix}"),
            ("now", now));

        if (scenario.OutsourcedSupplyBatchId is { } batchId)
        {
            await ExecuteNonQueryAsync(
                """
                INSERT INTO outsourced.outsourced_supply_batches
                    (id, supply_date, outsourced_vendor_id, lifecycle_status,
                     row_version, created_at, created_by_account_id)
                VALUES
                    (@batch_id, DATE '2026-09-05', @vendor_id, 'ACTIVE',
                     1, @now, @actor_id);
                """,
                cancellationToken,
                ("batch_id", batchId),
                ("vendor_id", scenario.OutsourcedVendorId),
                ("actor_id", scenario.ActorAccountId),
                ("now", now));
        }

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
            DELETE FROM outsourced.outsourced_supply_batches WHERE outsourced_vendor_id = @vendor_id;
            DELETE FROM party.outsourced_vendors WHERE id = @vendor_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds.ToArray()),
            ("vendor_id", scenario.OutsourcedVendorId),
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
        Guid OutsourcedVendorId,
        Guid? OutsourcedSupplyBatchId);
}
