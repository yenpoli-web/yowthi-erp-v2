using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.SalesHandling;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class SalesPackagingItemLifecycleIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Soft_delete_preserves_historical_work_then_restore_with_audit_and_replay()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(active: true, cancellationToken);
        var softDeleteCommandId = Guid.CreateVersion7();
        var restoreCommandId = Guid.CreateVersion7();

        try
        {
            var execution = new SoftDeleteSalesPackagingItemExecution(
                CommandId.From(softDeleteCommandId),
                Hash(1),
                ActorAccountId.From(scenario.ActorAccountId),
                new SoftDeleteSalesPackagingItemCommand(scenario.ItemId, 1));
            var softDelete = await ExecuteSoftDeleteAsync(execution, cancellationToken);

            Assert.True(softDelete.IsSuccess);
            Assert.Equal(new SalesPackagingItemLifecycleResult(scenario.ItemId, 2, true), softDelete.Value);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM sales_handling.sales_packaging_items
                    WHERE id = @item_id
                      AND active = true
                      AND deleted_at IS NOT NULL
                      AND deleted_by_account_id = @actor_id
                      AND row_version = 2;
                    """,
                    cancellationToken,
                    ("item_id", scenario.ItemId),
                    ("actor_id", scenario.ActorAccountId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM sales_handling.sales_packaging_work_records
                    WHERE id = @work_record_id
                      AND sales_packaging_item_id = @item_id;
                    """,
                    cancellationToken,
                    ("work_record_id", scenario.HistoricalWorkRecordId),
                    ("item_id", scenario.ItemId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.audit_event_subjects s
                    JOIN audit.audit_events e ON e.id = s.audit_event_id
                    WHERE e.command_id = @command_id
                      AND e.command_type = 'SoftDeleteSalesPackagingItem'
                      AND e.event_kind = 'DATA_LIFECYCLE'
                      AND s.subject_kind = 'sales-handling.packaging-item'
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
            Assert.Equal(SalesPackagingItemLifecycleErrorCodes.IdempotencyKeyReused, changedHash.Error.Code);

            var restore = await ExecuteRestoreAsync(
                new RestoreSalesPackagingItemExecution(
                    CommandId.From(restoreCommandId),
                    Hash(3),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new RestoreSalesPackagingItemCommand(scenario.ItemId, 2)),
                cancellationToken);

            Assert.True(restore.IsSuccess);
            Assert.Equal(new SalesPackagingItemLifecycleResult(scenario.ItemId, 3, false), restore.Value);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM sales_handling.sales_packaging_items
                    WHERE id = @item_id
                      AND active = true
                      AND deleted_at IS NULL
                      AND deleted_by_account_id IS NULL
                      AND row_version = 3;
                    """,
                    cancellationToken,
                    ("item_id", scenario.ItemId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.audit_event_subjects s
                    JOIN audit.audit_events e ON e.id = s.audit_event_id
                    WHERE e.command_id = @command_id
                      AND e.command_type = 'RestoreSalesPackagingItem'
                      AND e.event_kind = 'DATA_LIFECYCLE'
                      AND s.subject_kind = 'sales-handling.packaging-item'
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
        var scenario = await SeedScenarioAsync(active: false, cancellationToken);
        var softDeleteCommandId = Guid.CreateVersion7();
        var restoreCommandId = Guid.CreateVersion7();

        try
        {
            var softDelete = await ExecuteSoftDeleteAsync(
                new SoftDeleteSalesPackagingItemExecution(
                    CommandId.From(softDeleteCommandId),
                    Hash(4),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new SoftDeleteSalesPackagingItemCommand(scenario.ItemId, 1)),
                cancellationToken);
            Assert.True(softDelete.IsSuccess);

            var restore = await ExecuteRestoreAsync(
                new RestoreSalesPackagingItemExecution(
                    CommandId.From(restoreCommandId),
                    Hash(5),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new RestoreSalesPackagingItemCommand(scenario.ItemId, 2)),
                cancellationToken);
            Assert.True(restore.IsSuccess);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM sales_handling.sales_packaging_items
                    WHERE id = @item_id
                      AND active = false
                      AND deleted_at IS NULL
                      AND deleted_by_account_id IS NULL
                      AND row_version = 3;
                    """,
                    cancellationToken,
                    ("item_id", scenario.ItemId)));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, [softDeleteCommandId, restoreCommandId], cancellationToken);
        }
    }

    [Fact]
    public async Task Lifecycle_state_and_stale_conflicts_roll_back_command_acquisition_and_audit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(active: true, cancellationToken);
        var staleCommandId = Guid.CreateVersion7();
        var restoreCurrentCommandId = Guid.CreateVersion7();

        try
        {
            var stale = await ExecuteSoftDeleteAsync(
                new SoftDeleteSalesPackagingItemExecution(
                    CommandId.From(staleCommandId),
                    Hash(6),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new SoftDeleteSalesPackagingItemCommand(scenario.ItemId, 2)),
                cancellationToken);
            Assert.True(stale.IsFailure);
            Assert.Equal(SalesPackagingItemLifecycleErrorCodes.StaleRowVersion, stale.Error.Code);

            var restoreCurrent = await ExecuteRestoreAsync(
                new RestoreSalesPackagingItemExecution(
                    CommandId.From(restoreCurrentCommandId),
                    Hash(7),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new RestoreSalesPackagingItemCommand(scenario.ItemId, 1)),
                cancellationToken);
            Assert.True(restoreCurrent.IsFailure);
            Assert.Equal(SalesPackagingItemLifecycleErrorCodes.NotDeleted, restoreCurrent.Error.Code);

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

    [Fact]
    public async Task Soft_deleted_item_is_rejected_by_record_sales_packaging_work_without_residue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(active: true, cancellationToken);
        var softDeleteCommandId = Guid.CreateVersion7();
        var workCommandId = Guid.CreateVersion7();

        try
        {
            var softDelete = await ExecuteSoftDeleteAsync(
                new SoftDeleteSalesPackagingItemExecution(
                    CommandId.From(softDeleteCommandId),
                    Hash(8),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new SoftDeleteSalesPackagingItemCommand(scenario.ItemId, 1)),
                cancellationToken);
            Assert.True(softDelete.IsSuccess);

            var work = await ExecutePackagingAsync(
                new RecordSalesPackagingWorkExecution(
                    CommandId.From(workCommandId),
                    Hash(9),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new RecordSalesPackagingWorkCommand(
                        scenario.SaleId,
                        scenario.NewWorkDate,
                        scenario.EmployeeId,
                        scenario.ItemId,
                        10)),
                cancellationToken);

            Assert.True(work.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, work.Error.Kind);
            Assert.Equal(SalesHandlingApplicationErrorCodes.ItemInactive, work.Error.Code);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM sales_handling.sales_packaging_work_records WHERE id = @work_record_id;",
                    cancellationToken,
                    ("work_record_id", scenario.HistoricalWorkRecordId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", workCommandId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", workCommandId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.outbox_messages WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", workCommandId)));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, [softDeleteCommandId, workCommandId], cancellationToken);
        }
    }

    private static async ValueTask<ApplicationResult<SalesPackagingItemLifecycleResult>> ExecuteSoftDeleteAsync(
        SoftDeleteSalesPackagingItemExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<ISalesPackagingItemLifecycleExecutor>()
            .SoftDeleteAsync(execution, cancellationToken);
    }

    private static async ValueTask<ApplicationResult<SalesPackagingItemLifecycleResult>> ExecuteRestoreAsync(
        RestoreSalesPackagingItemExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<ISalesPackagingItemLifecycleExecutor>()
            .RestoreAsync(execution, cancellationToken);
    }

    private static async ValueTask<ApplicationResult<RecordSalesPackagingWorkResult>> ExecutePackagingAsync(
        RecordSalesPackagingWorkExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IRecordSalesPackagingWorkExecutor>()
            .ExecuteAsync(execution, cancellationToken);
    }

    private static CommandRequestHash Hash(byte value)
    {
        var bytes = new byte[CommandRequestHash.Sha256Length];
        Array.Fill(bytes, value);
        return CommandRequestHash.FromSha256(bytes);
    }

    private static async Task<Scenario> SeedScenarioAsync(bool active, CancellationToken cancellationToken)
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var scenario = new Scenario(
            ActorAccountId: Guid.CreateVersion7(),
            EmployeeId: Guid.CreateVersion7(),
            CustomerId: Guid.CreateVersion7(),
            SaleId: Guid.CreateVersion7(),
            ItemId: Guid.CreateVersion7(),
            HistoricalWorkRecordId: Guid.CreateVersion7(),
            HistoricalWorkDate: new DateOnly(2026, 9, 4),
            NewWorkDate: new DateOnly(2026, 9, 5),
            ItemName: $"P6 V8 C13 packaging item {suffix}");
        var now = DateTimeOffset.UtcNow;

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 V8 C13 lifecycle actor', true, NULL, NULL, @now);

            INSERT INTO party.employees
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, row_version, created_at, created_by_account_id)
            VALUES
                (@employee_id, 'P6 V8 C13 employee', NULL, NULL, NULL, NULL, NULL,
                 true, 1, @now, @actor_id);

            INSERT INTO party.customers
                (id, name_zh_tw, name_th_th, phone, active, row_version, created_at, created_by_account_id)
            VALUES
                (@customer_id, 'P6 V8 C13 customer', NULL, NULL, true, 1, @now, @actor_id);

            INSERT INTO sales.sales
                (id, sales_date, customer_id, status, confirmed_at, confirmed_by_account_id,
                 row_version, created_at, created_by_account_id)
            VALUES
                (@sale_id, @new_work_date, @customer_id, 'DRAFT', NULL, NULL,
                 1, @now, @actor_id);

            INSERT INTO sales_handling.sales_packaging_items
                (id, name_zh_tw, name_th_th, active, row_version, created_at, created_by_account_id)
            VALUES
                (@item_id, @item_name, NULL, @active, 1, @now, @actor_id);

            INSERT INTO sales_handling.sales_packaging_work_records
                (id, sales_id, work_date, employee_id, sales_packaging_item_id,
                 confirmed_wage_thb, recorded_at, recorded_by_account_id, row_version)
            VALUES
                (@work_record_id, @sale_id, @historical_work_date, @employee_id, @item_id,
                 0, @now, @actor_id, 1);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("employee_id", scenario.EmployeeId),
            ("customer_id", scenario.CustomerId),
            ("sale_id", scenario.SaleId),
            ("item_id", scenario.ItemId),
            ("item_name", scenario.ItemName),
            ("active", active),
            ("work_record_id", scenario.HistoricalWorkRecordId),
            ("historical_work_date", scenario.HistoricalWorkDate),
            ("new_work_date", scenario.NewWorkDate),
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
            DELETE FROM sales_handling.sales_packaging_work_records WHERE sales_packaging_item_id = @item_id;
            DELETE FROM sales.sales WHERE id = @sale_id;
            DELETE FROM sales_handling.sales_packaging_items WHERE id = @item_id;
            DELETE FROM party.customers WHERE id = @customer_id;
            DELETE FROM party.employees WHERE id = @employee_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds.ToArray()),
            ("item_id", scenario.ItemId),
            ("sale_id", scenario.SaleId),
            ("customer_id", scenario.CustomerId),
            ("employee_id", scenario.EmployeeId),
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
        Guid EmployeeId,
        Guid CustomerId,
        Guid SaleId,
        Guid ItemId,
        Guid HistoricalWorkRecordId,
        DateOnly HistoricalWorkDate,
        DateOnly NewWorkDate,
        string ItemName);
}
