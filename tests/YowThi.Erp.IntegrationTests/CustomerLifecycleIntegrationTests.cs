using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Party;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class CustomerLifecycleIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Soft_delete_with_historical_sale_dependency_preserves_sale_then_restore_reexposes_customer_with_audit_and_replay()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedCustomerWithSaleAsync(active: true, cancellationToken);
        var softDeleteCommandId = Guid.CreateVersion7();
        var restoreCommandId = Guid.CreateVersion7();

        try
        {
            var softDeleteExecution = new SoftDeleteCustomerExecution(
                CommandId.From(softDeleteCommandId),
                Hash(1),
                ActorAccountId.From(scenario.ActorAccountId),
                new SoftDeleteCustomerCommand(scenario.CustomerId, 1));
            var softDelete = await ExecuteSoftDeleteAsync(softDeleteExecution, cancellationToken);

            Assert.True(softDelete.IsSuccess);
            Assert.Equal(new CustomerLifecycleResult(scenario.CustomerId, 2, true), softDelete.Value);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM party.customers
                    WHERE id = @customer_id
                      AND deleted_at IS NOT NULL
                      AND deleted_by_account_id = @actor_id
                      AND active = true
                      AND row_version = 2;
                    """,
                    cancellationToken,
                    ("customer_id", scenario.CustomerId),
                    ("actor_id", scenario.ActorAccountId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM sales.sales WHERE id = @sales_id AND customer_id = @customer_id;",
                    cancellationToken,
                    ("sales_id", scenario.SalesId),
                    ("customer_id", scenario.CustomerId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.audit_event_subjects s
                    JOIN audit.audit_events e ON e.id = s.audit_event_id
                    WHERE e.command_id = @command_id
                      AND e.command_type = 'SoftDeleteCustomer'
                      AND e.event_kind = 'DATA_LIFECYCLE'
                      AND s.subject_kind = 'party.customer'
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
            Assert.Equal(CustomerLifecycleErrorCodes.IdempotencyKeyReused, changedHash.Error.Code);

            var restoreExecution = new RestoreCustomerExecution(
                CommandId.From(restoreCommandId),
                Hash(3),
                ActorAccountId.From(scenario.ActorAccountId),
                new RestoreCustomerCommand(scenario.CustomerId, 2));
            var restore = await ExecuteRestoreAsync(restoreExecution, cancellationToken);

            Assert.True(restore.IsSuccess);
            Assert.Equal(new CustomerLifecycleResult(scenario.CustomerId, 3, false), restore.Value);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM party.customers
                    WHERE id = @customer_id
                      AND deleted_at IS NULL
                      AND deleted_by_account_id IS NULL
                      AND active = true
                      AND row_version = 3;
                    """,
                    cancellationToken,
                    ("customer_id", scenario.CustomerId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM sales.sales WHERE id = @sales_id AND customer_id = @customer_id;",
                    cancellationToken,
                    ("sales_id", scenario.SalesId),
                    ("customer_id", scenario.CustomerId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.audit_event_subjects s
                    JOIN audit.audit_events e ON e.id = s.audit_event_id
                    WHERE e.command_id = @command_id
                      AND e.command_type = 'RestoreCustomer'
                      AND e.event_kind = 'DATA_LIFECYCLE'
                      AND s.subject_kind = 'party.customer'
                      AND s.change_kind = 'RESTORE'
                      AND s.before_row_version = 2
                      AND s.after_row_version = 3;
                    """,
                    cancellationToken,
                    ("command_id", restoreCommandId)));
        }
        finally
        {
            await CleanupAsync(scenario, [softDeleteCommandId, restoreCommandId], cancellationToken);
        }
    }

    [Fact]
    public async Task Restore_preserves_inactive_customer_state()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedCustomerWithSaleAsync(active: false, cancellationToken);
        var softDeleteCommandId = Guid.CreateVersion7();
        var restoreCommandId = Guid.CreateVersion7();

        try
        {
            var softDelete = await ExecuteSoftDeleteAsync(
                new SoftDeleteCustomerExecution(
                    CommandId.From(softDeleteCommandId),
                    Hash(4),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new SoftDeleteCustomerCommand(scenario.CustomerId, 1)),
                cancellationToken);
            Assert.True(softDelete.IsSuccess);

            var restore = await ExecuteRestoreAsync(
                new RestoreCustomerExecution(
                    CommandId.From(restoreCommandId),
                    Hash(5),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new RestoreCustomerCommand(scenario.CustomerId, 2)),
                cancellationToken);
            Assert.True(restore.IsSuccess);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM party.customers
                    WHERE id = @customer_id
                      AND active = false
                      AND deleted_at IS NULL
                      AND deleted_by_account_id IS NULL
                      AND row_version = 3;
                    """,
                    cancellationToken,
                    ("customer_id", scenario.CustomerId)));
        }
        finally
        {
            await CleanupAsync(scenario, [softDeleteCommandId, restoreCommandId], cancellationToken);
        }
    }

    [Fact]
    public async Task Customer_lifecycle_state_and_stale_conflicts_roll_back_command_acquisition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedCustomerWithSaleAsync(active: true, cancellationToken);
        var staleCommandId = Guid.CreateVersion7();
        var restoreCurrentCommandId = Guid.CreateVersion7();

        try
        {
            var stale = await ExecuteSoftDeleteAsync(
                new SoftDeleteCustomerExecution(
                    CommandId.From(staleCommandId),
                    Hash(6),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new SoftDeleteCustomerCommand(scenario.CustomerId, 2)),
                cancellationToken);
            Assert.True(stale.IsFailure);
            Assert.Equal(CustomerLifecycleErrorCodes.StaleRowVersion, stale.Error.Code);

            var restoreCurrent = await ExecuteRestoreAsync(
                new RestoreCustomerExecution(
                    CommandId.From(restoreCurrentCommandId),
                    Hash(7),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new RestoreCustomerCommand(scenario.CustomerId, 1)),
                cancellationToken);
            Assert.True(restoreCurrent.IsFailure);
            Assert.Equal(CustomerLifecycleErrorCodes.NotDeleted, restoreCurrent.Error.Code);

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
            await CleanupAsync(scenario, [staleCommandId, restoreCurrentCommandId], cancellationToken);
        }
    }

    private static async ValueTask<ApplicationResult<CustomerLifecycleResult>> ExecuteSoftDeleteAsync(
        SoftDeleteCustomerExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<ICustomerLifecycleExecutor>()
            .SoftDeleteAsync(execution, cancellationToken);
    }

    private static async ValueTask<ApplicationResult<CustomerLifecycleResult>> ExecuteRestoreAsync(
        RestoreCustomerExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<ICustomerLifecycleExecutor>()
            .RestoreAsync(execution, cancellationToken);
    }

    private static CommandRequestHash Hash(byte value)
    {
        var bytes = new byte[CommandRequestHash.Sha256Length];
        Array.Fill(bytes, value);
        return CommandRequestHash.FromSha256(bytes);
    }

    private static async Task<Scenario> SeedCustomerWithSaleAsync(bool active, CancellationToken cancellationToken)
    {
        var scenario = new Scenario(
            ActorAccountId: Guid.CreateVersion7(),
            CustomerId: Guid.CreateVersion7(),
            SalesId: Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 V8 C5 lifecycle actor', true, NULL, NULL, @now);

            INSERT INTO party.customers
                (id, name_zh_tw, name_th_th, phone, active, row_version, created_at, created_by_account_id)
            VALUES
                (@customer_id, 'P6 V8 C5 customer', NULL, NULL, @active, 1, @now, @actor_id);

            INSERT INTO sales.sales
                (id, sales_date, customer_id, status, row_version, created_at, created_by_account_id)
            VALUES
                (@sales_id, DATE '2026-09-04', @customer_id, 'DRAFT', 1, @now, @actor_id);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("customer_id", scenario.CustomerId),
            ("sales_id", scenario.SalesId),
            ("active", active),
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
            DELETE FROM system.command_executions WHERE command_id = ANY(@command_ids);
            DELETE FROM sales.sales WHERE id = @sales_id;
            DELETE FROM party.customers WHERE id = @customer_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds.ToArray()),
            ("sales_id", scenario.SalesId),
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

    private sealed record Scenario(Guid ActorAccountId, Guid CustomerId, Guid SalesId);
}
