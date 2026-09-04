using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Labor;
using YowThi.Erp.Application.Party;
using YowThi.Erp.Application.Processing;
using YowThi.Erp.Application.SalesHandling;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class EmployeeLifecycleIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Soft_delete_preserves_historical_wage_hides_employee_then_restore_reexposes_with_audit_and_replay()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedEmployeeScenarioAsync(active: true, cancellationToken);
        var softDeleteCommandId = Guid.CreateVersion7();
        var restoreCommandId = Guid.CreateVersion7();

        try
        {
            var before = await ReadEmployeeOptionsAsync(scenario.EmployeeName, cancellationToken);
            Assert.Contains(before.Items, item => item.Id == scenario.EmployeeId);

            var execution = new SoftDeleteEmployeeExecution(
                CommandId.From(softDeleteCommandId),
                Hash(1),
                ActorAccountId.From(scenario.ActorAccountId),
                new SoftDeleteEmployeeCommand(scenario.EmployeeId, 1));
            var softDelete = await ExecuteSoftDeleteAsync(execution, cancellationToken);

            Assert.True(softDelete.IsSuccess);
            Assert.Equal(new EmployeeLifecycleResult(scenario.EmployeeId, 2, true), softDelete.Value);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM party.employees
                    WHERE id = @employee_id
                      AND active = true
                      AND deleted_at IS NOT NULL
                      AND deleted_by_account_id = @actor_id
                      AND row_version = 2;
                    """,
                    cancellationToken,
                    ("employee_id", scenario.EmployeeId),
                    ("actor_id", scenario.ActorAccountId)));

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM labor.employee_daily_wages
                    WHERE id = @daily_wage_id
                      AND employee_id = @employee_id;
                    """,
                    cancellationToken,
                    ("daily_wage_id", scenario.HistoricalDailyWageId),
                    ("employee_id", scenario.EmployeeId)));

            var hidden = await ReadEmployeeOptionsAsync(scenario.EmployeeName, cancellationToken);
            Assert.DoesNotContain(hidden.Items, item => item.Id == scenario.EmployeeId);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.audit_event_subjects s
                    JOIN audit.audit_events e ON e.id = s.audit_event_id
                    WHERE e.command_id = @command_id
                      AND e.command_type = 'SoftDeleteEmployee'
                      AND e.event_kind = 'DATA_LIFECYCLE'
                      AND s.subject_kind = 'party.employee'
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
            Assert.Equal(EmployeeLifecycleErrorCodes.IdempotencyKeyReused, changedHash.Error.Code);

            var restore = await ExecuteRestoreAsync(
                new RestoreEmployeeExecution(
                    CommandId.From(restoreCommandId),
                    Hash(3),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new RestoreEmployeeCommand(scenario.EmployeeId, 2)),
                cancellationToken);

            Assert.True(restore.IsSuccess);
            Assert.Equal(new EmployeeLifecycleResult(scenario.EmployeeId, 3, false), restore.Value);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM party.employees
                    WHERE id = @employee_id
                      AND active = true
                      AND deleted_at IS NULL
                      AND deleted_by_account_id IS NULL
                      AND row_version = 3;
                    """,
                    cancellationToken,
                    ("employee_id", scenario.EmployeeId)));

            var visibleAgain = await ReadEmployeeOptionsAsync(scenario.EmployeeName, cancellationToken);
            Assert.Contains(visibleAgain.Items, item => item.Id == scenario.EmployeeId);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.audit_event_subjects s
                    JOIN audit.audit_events e ON e.id = s.audit_event_id
                    WHERE e.command_id = @command_id
                      AND e.command_type = 'RestoreEmployee'
                      AND e.event_kind = 'DATA_LIFECYCLE'
                      AND s.subject_kind = 'party.employee'
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
    public async Task Restore_preserves_inactive_state_and_employee_remains_unavailable_for_processing_selection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedEmployeeScenarioAsync(active: false, cancellationToken);
        var softDeleteCommandId = Guid.CreateVersion7();
        var restoreCommandId = Guid.CreateVersion7();

        try
        {
            var softDelete = await ExecuteSoftDeleteAsync(
                new SoftDeleteEmployeeExecution(
                    CommandId.From(softDeleteCommandId),
                    Hash(4),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new SoftDeleteEmployeeCommand(scenario.EmployeeId, 1)),
                cancellationToken);
            Assert.True(softDelete.IsSuccess);

            var restore = await ExecuteRestoreAsync(
                new RestoreEmployeeExecution(
                    CommandId.From(restoreCommandId),
                    Hash(5),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new RestoreEmployeeCommand(scenario.EmployeeId, 2)),
                cancellationToken);
            Assert.True(restore.IsSuccess);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM party.employees
                    WHERE id = @employee_id
                      AND active = false
                      AND deleted_at IS NULL
                      AND deleted_by_account_id IS NULL
                      AND row_version = 3;
                    """,
                    cancellationToken,
                    ("employee_id", scenario.EmployeeId)));

            var options = await ReadEmployeeOptionsAsync(scenario.EmployeeName, cancellationToken);
            Assert.DoesNotContain(options.Items, item => item.Id == scenario.EmployeeId);
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
        var scenario = await SeedEmployeeScenarioAsync(active: true, cancellationToken);
        var staleCommandId = Guid.CreateVersion7();
        var restoreCurrentCommandId = Guid.CreateVersion7();

        try
        {
            var stale = await ExecuteSoftDeleteAsync(
                new SoftDeleteEmployeeExecution(
                    CommandId.From(staleCommandId),
                    Hash(6),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new SoftDeleteEmployeeCommand(scenario.EmployeeId, 2)),
                cancellationToken);
            Assert.True(stale.IsFailure);
            Assert.Equal(EmployeeLifecycleErrorCodes.StaleRowVersion, stale.Error.Code);

            var restoreCurrent = await ExecuteRestoreAsync(
                new RestoreEmployeeExecution(
                    CommandId.From(restoreCurrentCommandId),
                    Hash(7),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new RestoreEmployeeCommand(scenario.EmployeeId, 1)),
                cancellationToken);
            Assert.True(restoreCurrent.IsFailure);
            Assert.Equal(EmployeeLifecycleErrorCodes.NotDeleted, restoreCurrent.Error.Code);

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
    public async Task Soft_deleted_employee_is_rejected_by_sales_packaging_and_daily_wage_commands_without_residue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedEmployeeScenarioAsync(active: true, cancellationToken);
        var softDeleteCommandId = Guid.CreateVersion7();
        var packagingCommandId = Guid.CreateVersion7();
        var wageCommandId = Guid.CreateVersion7();

        try
        {
            var softDelete = await ExecuteSoftDeleteAsync(
                new SoftDeleteEmployeeExecution(
                    CommandId.From(softDeleteCommandId),
                    Hash(8),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new SoftDeleteEmployeeCommand(scenario.EmployeeId, 1)),
                cancellationToken);
            Assert.True(softDelete.IsSuccess);

            var packaging = await ExecutePackagingAsync(
                new RecordSalesPackagingWorkExecution(
                    CommandId.From(packagingCommandId),
                    Hash(9),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new RecordSalesPackagingWorkCommand(
                        scenario.SaleId,
                        scenario.WorkDate,
                        scenario.EmployeeId,
                        Guid.CreateVersion7(),
                        10)),
                cancellationToken);
            Assert.True(packaging.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, packaging.Error.Kind);
            Assert.Equal(SalesHandlingApplicationErrorCodes.EmployeeInactive, packaging.Error.Code);

            var wage = await ExecuteDailyWageAsync(
                new ConfirmEmployeeDailyWageExecution(
                    CommandId.From(wageCommandId),
                    Hash(10),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new ConfirmEmployeeDailyWageCommand(
                        scenario.WorkDate,
                        scenario.EmployeeId,
                        [])),
                cancellationToken);
            Assert.True(wage.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, wage.Error.Kind);
            Assert.Equal(LaborApplicationErrorCodes.EmployeeInactive, wage.Error.Code);

            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = ANY(@command_ids);",
                    cancellationToken,
                    ("command_ids", new[] { packagingCommandId, wageCommandId })));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = ANY(@command_ids);",
                    cancellationToken,
                    ("command_ids", new[] { packagingCommandId, wageCommandId })));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.outbox_messages WHERE command_id = ANY(@command_ids);",
                    cancellationToken,
                    ("command_ids", new[] { packagingCommandId, wageCommandId })));
        }
        finally
        {
            await CleanupScenarioAsync(
                scenario,
                [softDeleteCommandId, packagingCommandId, wageCommandId],
                cancellationToken);
        }
    }

    private static async ValueTask<ApplicationResult<EmployeeLifecycleResult>> ExecuteSoftDeleteAsync(
        SoftDeleteEmployeeExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IEmployeeLifecycleExecutor>()
            .SoftDeleteAsync(execution, cancellationToken);
    }

    private static async ValueTask<ApplicationResult<EmployeeLifecycleResult>> ExecuteRestoreAsync(
        RestoreEmployeeExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IEmployeeLifecycleExecutor>()
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

    private static async ValueTask<ApplicationResult<ConfirmEmployeeDailyWageResult>> ExecuteDailyWageAsync(
        ConfirmEmployeeDailyWageExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IConfirmEmployeeDailyWageExecutor>()
            .ExecuteAsync(execution, cancellationToken);
    }

    private static async ValueTask<ProcessingExecutionOptionPage<ProcessingEmployeeOption>> ReadEmployeeOptionsAsync(
        string search,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IProcessingExecutionOptionsReader>()
            .GetEmployeesAsync(
                new ProcessingExecutionOptionsQuery("zh-TW", search, 0, 100),
                cancellationToken);
    }

    private static CommandRequestHash Hash(byte value)
    {
        var bytes = new byte[CommandRequestHash.Sha256Length];
        Array.Fill(bytes, value);
        return CommandRequestHash.FromSha256(bytes);
    }

    private static async Task<Scenario> SeedEmployeeScenarioAsync(
        bool active,
        CancellationToken cancellationToken)
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        var workDate = new DateOnly(2026, 9, 4);
        var scenario = new Scenario(
            ActorAccountId: Guid.CreateVersion7(),
            EmployeeId: Guid.CreateVersion7(),
            CustomerId: Guid.CreateVersion7(),
            SaleId: Guid.CreateVersion7(),
            HistoricalDailyWageId: Guid.CreateVersion7(),
            EmployeeName: $"P6 V8 C12 employee {suffix}",
            WorkDate: workDate);
        var now = DateTimeOffset.UtcNow;

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 V8 C12 lifecycle actor', true, NULL, NULL, @now);

            INSERT INTO party.employees
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, row_version, created_at, created_by_account_id)
            VALUES
                (@employee_id, @employee_name, NULL, NULL, NULL, NULL, NULL,
                 @active, 1, @now, @actor_id);

            INSERT INTO party.customers
                (id, name_zh_tw, name_th_th, phone, active, row_version, created_at, created_by_account_id)
            VALUES
                (@customer_id, 'P6 V8 C12 customer', NULL, NULL, true, 1, @now, @actor_id);

            INSERT INTO sales.sales
                (id, sales_date, customer_id, status, confirmed_at, confirmed_by_account_id,
                 row_version, created_at, created_by_account_id)
            VALUES
                (@sale_id, @work_date, @customer_id, 'DRAFT', NULL, NULL,
                 1, @now, @actor_id);

            INSERT INTO labor.employee_daily_wages
                (id, work_date, employee_id, processing_wage_total_thb,
                 sales_packaging_wage_total_thb, total_wage_thb,
                 confirmed_at, confirmed_by_account_id, row_version)
            VALUES
                (@daily_wage_id, DATE '2026-09-03', @employee_id, 0,
                 0, 0, @now, @actor_id, 1);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("employee_id", scenario.EmployeeId),
            ("employee_name", scenario.EmployeeName),
            ("active", active),
            ("customer_id", scenario.CustomerId),
            ("sale_id", scenario.SaleId),
            ("daily_wage_id", scenario.HistoricalDailyWageId),
            ("work_date", workDate),
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
            DELETE FROM labor.employee_daily_wages WHERE id = @daily_wage_id;
            DELETE FROM sales.sales WHERE id = @sale_id;
            DELETE FROM party.customers WHERE id = @customer_id;
            DELETE FROM party.employees WHERE id = @employee_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds.ToArray()),
            ("daily_wage_id", scenario.HistoricalDailyWageId),
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
        Guid HistoricalDailyWageId,
        string EmployeeName,
        DateOnly WorkDate);
}
