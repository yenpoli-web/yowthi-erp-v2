using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.DataProtection;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class CustomerHardDeleteIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Hard_delete_physically_removes_customer_retains_audit_and_replays_after_target_is_gone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedCustomerAsync(withSale: false, cancellationToken);
        var commandId = Guid.CreateVersion7();
        var command = new HardDeleteCustomerCommand(scenario.CustomerId, 1);
        var execution = Execution(commandId, scenario.ActorAccountId, Hash(1), command);

        try
        {
            var result = await ExecuteAsync(execution, cancellationToken);
            Assert.True(result.IsSuccess);
            Assert.Equal(scenario.CustomerId, result.Value.CustomerId);
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM party.customers WHERE id = @customer_id;",
                    cancellationToken,
                    ("customer_id", scenario.CustomerId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.audit_event_subjects s
                    JOIN audit.audit_events e ON e.id = s.audit_event_id
                    WHERE e.command_id = @command_id
                      AND e.command_type = 'HardDeleteCustomer'
                      AND e.event_kind = 'HARD_DELETE'
                      AND s.subject_kind = 'party.customer'
                      AND s.change_kind = 'HARD_DELETE'
                      AND s.subject_key ->> 'id' = @customer_id_text
                      AND s.before_row_version = 1
                      AND s.after_row_version IS NULL;
                    """,
                    cancellationToken,
                    ("command_id", commandId),
                    ("customer_id_text", scenario.CustomerId.ToString())));

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
            Assert.Equal(CustomerHardDeleteErrorCodes.IdempotencyKeyReused, changedHash.Error.Code);
        }
        finally
        {
            await CleanupAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Hard_delete_blocks_existing_sale_dependency_without_committing_command_identity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedCustomerAsync(withSale: true, cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            var result = await ExecuteAsync(
                Execution(
                    commandId,
                    scenario.ActorAccountId,
                    Hash(3),
                    new HardDeleteCustomerCommand(scenario.CustomerId, 1)),
                cancellationToken);

            Assert.True(result.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, result.Error.Kind);
            Assert.Equal(CustomerHardDeleteErrorCodes.DependencyBlocked, result.Error.Code);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM party.customers WHERE id = @customer_id;",
                    cancellationToken,
                    ("customer_id", scenario.CustomerId)));
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
            await CleanupAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Hard_delete_rejects_stale_row_version_and_rolls_back_command_acquisition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedCustomerAsync(withSale: false, cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            var result = await ExecuteAsync(
                Execution(
                    commandId,
                    scenario.ActorAccountId,
                    Hash(4),
                    new HardDeleteCustomerCommand(scenario.CustomerId, 2)),
                cancellationToken);

            Assert.True(result.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, result.Error.Kind);
            Assert.Equal(CustomerHardDeleteErrorCodes.StaleRowVersion, result.Error.Code);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM party.customers WHERE id = @customer_id;",
                    cancellationToken,
                    ("customer_id", scenario.CustomerId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", commandId)));
        }
        finally
        {
            await CleanupAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    private static HardDeleteCustomerExecution Execution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        HardDeleteCustomerCommand command) =>
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

    private static async ValueTask<ApplicationResult<HardDeleteCustomerResult>> ExecuteAsync(
        HardDeleteCustomerExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IHardDeleteCustomerExecutor>()
            .ExecuteAsync(execution, cancellationToken);
    }

    private static async Task<Scenario> SeedCustomerAsync(bool withSale, CancellationToken cancellationToken)
    {
        var scenario = new Scenario(
            ActorAccountId: Guid.CreateVersion7(),
            CustomerId: Guid.CreateVersion7(),
            SalesId: withSale ? Guid.CreateVersion7() : null);
        var now = DateTimeOffset.UtcNow;

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 V8 customer hard delete actor', true, NULL, NULL, @now);

            INSERT INTO party.customers
                (id, name_zh_tw, name_th_th, phone, active, row_version, created_at, created_by_account_id)
            VALUES
                (@customer_id, 'P6 V8 customer', NULL, NULL, true, 1, @now, @actor_id);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("customer_id", scenario.CustomerId),
            ("now", now));

        if (scenario.SalesId is { } salesId)
        {
            await ExecuteNonQueryAsync(
                """
                INSERT INTO sales.sales
                    (id, sales_date, customer_id, status, row_version, created_at, created_by_account_id)
                VALUES
                    (@sales_id, DATE '2026-09-04', @customer_id, 'DRAFT', 1, @now, @actor_id);
                """,
                cancellationToken,
                ("sales_id", salesId),
                ("customer_id", scenario.CustomerId),
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
            DELETE FROM system.command_executions WHERE command_id = ANY(@command_ids);
            DELETE FROM sales.sales WHERE customer_id = @customer_id;
            DELETE FROM party.customers WHERE id = @customer_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds.ToArray()),
            ("customer_id", scenario.CustomerId),
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

    private sealed record Scenario(Guid ActorAccountId, Guid CustomerId, Guid? SalesId);
}
