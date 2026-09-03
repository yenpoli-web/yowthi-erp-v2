using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Labor;
using YowThi.Erp.Application.SalesHandling;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class RecordSalesPackagingWorkIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Draft_and_confirmed_sales_allow_work_and_same_tuple_can_be_recorded_multiple_times()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(cancellationToken);
        var draftCommandId = Guid.CreateVersion7();
        var duplicateCommandId = Guid.CreateVersion7();
        var confirmedCommandId = Guid.CreateVersion7();

        try
        {
            var draftCommand = Command(scenario.DraftSalesId, scenario, 100);
            var draftExecution = Execution(draftCommandId, scenario.ActorAccountId, Hash(1), draftCommand);
            var draftResult = await ExecuteHandlingAsync(draftExecution, cancellationToken);
            Assert.True(draftResult.IsSuccess);
            Assert.Equal(1, draftResult.Value.RowVersion);

            var replay = await ExecuteHandlingAsync(draftExecution, cancellationToken);
            Assert.True(replay.IsSuccess);
            Assert.Equal(draftResult.Value, replay.Value);

            var changedHash = await ExecuteHandlingAsync(
                Execution(draftCommandId, scenario.ActorAccountId, Hash(2), draftCommand),
                cancellationToken);
            Assert.True(changedHash.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, changedHash.Error.Kind);
            Assert.Equal(SalesHandlingApplicationErrorCodes.IdempotencyKeyReused, changedHash.Error.Code);

            var duplicateResult = await ExecuteHandlingAsync(
                Execution(duplicateCommandId, scenario.ActorAccountId, Hash(3), draftCommand),
                cancellationToken);
            Assert.True(duplicateResult.IsSuccess);
            Assert.NotEqual(draftResult.Value.SalesPackagingWorkRecordId, duplicateResult.Value.SalesPackagingWorkRecordId);

            var confirmedResult = await ExecuteHandlingAsync(
                Execution(
                    confirmedCommandId,
                    scenario.ActorAccountId,
                    Hash(4),
                    Command(scenario.ConfirmedSalesId, scenario, 75)),
                cancellationToken);
            Assert.True(confirmedResult.IsSuccess);

            Assert.Equal(
                2L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM sales_handling.sales_packaging_work_records
                    WHERE sales_id = @sales_id
                      AND work_date = @work_date
                      AND employee_id = @employee_id
                      AND sales_packaging_item_id = @item_id
                      AND deleted_at IS NULL;
                    """,
                    cancellationToken,
                    ("sales_id", scenario.DraftSalesId),
                    ("work_date", scenario.WorkDate),
                    ("employee_id", scenario.EmployeeId),
                    ("item_id", scenario.PackagingItemId)));

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM sales_handling.sales_packaging_work_records
                    WHERE sales_id = @sales_id AND deleted_at IS NULL;
                    """,
                    cancellationToken,
                    ("sales_id", scenario.ConfirmedSalesId)));

            Assert.Equal(
                3L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM system.command_executions
                    WHERE command_id = ANY(@command_ids) AND status = 'SUCCEEDED';
                    """,
                    cancellationToken,
                    ("command_ids", new[] { draftCommandId, duplicateCommandId, confirmedCommandId })));
            Assert.Equal(
                3L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.audit_events
                    WHERE command_id = ANY(@command_ids) AND command_type = 'RecordSalesPackagingWork';
                    """,
                    cancellationToken,
                    ("command_ids", new[] { draftCommandId, duplicateCommandId, confirmedCommandId })));
            Assert.Equal(
                3L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM system.outbox_messages
                    WHERE command_id = ANY(@command_ids)
                      AND message_type = 'sales-handling.work-record.recorded';
                    """,
                    cancellationToken,
                    ("command_ids", new[] { draftCommandId, duplicateCommandId, confirmedCommandId })));
        }
        finally
        {
            await CleanupScenarioAsync(
                scenario,
                new[] { draftCommandId, duplicateCommandId, confirmedCommandId },
                cancellationToken);
        }
    }

    [Fact]
    public async Task Existing_daily_wage_blocks_normal_late_packaging_work_and_rolls_back_command_identity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            await ExecuteNonQueryAsync(
                """
                INSERT INTO labor.employee_daily_wages
                    (id, work_date, employee_id, processing_wage_total_thb,
                     sales_packaging_wage_total_thb, total_wage_thb,
                     confirmed_at, confirmed_by_account_id, row_version)
                VALUES
                    (@id, @work_date, @employee_id, 0, 0, 0, @now, @actor_id, 1);
                """,
                cancellationToken,
                ("id", Guid.CreateVersion7()),
                ("work_date", scenario.WorkDate),
                ("employee_id", scenario.EmployeeId),
                ("now", DateTimeOffset.UtcNow),
                ("actor_id", scenario.ActorAccountId));

            var result = await ExecuteHandlingAsync(
                Execution(
                    commandId,
                    scenario.ActorAccountId,
                    Hash(5),
                    Command(scenario.DraftSalesId, scenario, 100)),
                cancellationToken);

            Assert.True(result.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, result.Error.Kind);
            Assert.Equal(SalesHandlingApplicationErrorCodes.DailyWageAlreadyConfirmed, result.Error.Code);
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM sales_handling.sales_packaging_work_records WHERE employee_id = @employee_id AND work_date = @work_date;",
                    cancellationToken,
                    ("employee_id", scenario.EmployeeId),
                    ("work_date", scenario.WorkDate)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", commandId)));
        }
        finally
        {
            await CleanupScenarioAsync(scenario, new[] { commandId }, cancellationToken);
        }
    }

    [Fact]
    public async Task Concurrent_handling_and_daily_wage_never_omit_a_successful_work_record_from_the_wage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(cancellationToken);
        var handlingCommandId = Guid.CreateVersion7();
        var wageCommandId = Guid.CreateVersion7();
        const long wageThb = 125;

        try
        {
            await using var blockerConnection = await OpenConnectionAsync(cancellationToken);
            await using var blockerTransaction = await blockerConnection.BeginTransactionAsync(cancellationToken);
            await using (var blockerCommand = new NpgsqlCommand(
                "SELECT id FROM party.employees WHERE id = @employee_id FOR UPDATE;",
                blockerConnection,
                blockerTransaction))
            {
                blockerCommand.Parameters.AddWithValue("employee_id", scenario.EmployeeId);
                await blockerCommand.ExecuteScalarAsync(cancellationToken);
            }

            var handlingTask = ExecuteHandlingAsync(
                Execution(
                    handlingCommandId,
                    scenario.ActorAccountId,
                    Hash(6),
                    Command(scenario.DraftSalesId, scenario, wageThb)),
                cancellationToken).AsTask();
            var wageTask = ExecuteDailyWageAsync(
                new ConfirmEmployeeDailyWageExecution(
                    CommandId.From(wageCommandId),
                    Hash(7),
                    ActorAccountId.From(scenario.ActorAccountId),
                    new ConfirmEmployeeDailyWageCommand(
                        scenario.WorkDate,
                        scenario.EmployeeId,
                        Array.Empty<ProcessingWageRateOverride>())),
                cancellationToken).AsTask();

            await Task.Delay(75, cancellationToken);
            await blockerTransaction.CommitAsync(cancellationToken);

            var handlingResult = await handlingTask;
            var wageResult = await wageTask;
            Assert.True(wageResult.IsSuccess);

            if (handlingResult.IsSuccess)
            {
                Assert.Equal(wageThb, wageResult.Value.SalesPackagingWageTotalThb);
                Assert.Equal(wageThb, wageResult.Value.TotalWageThb);
            }
            else
            {
                Assert.Equal(ApplicationErrorKind.Conflict, handlingResult.Error.Kind);
                Assert.Equal(SalesHandlingApplicationErrorCodes.DailyWageAlreadyConfirmed, handlingResult.Error.Code);
                Assert.Equal(0L, wageResult.Value.SalesPackagingWageTotalThb);
                Assert.Equal(0L, wageResult.Value.TotalWageThb);
            }

            Assert.Equal(
                wageResult.Value.SalesPackagingWageTotalThb,
                await ScalarAsync<long>(
                    "SELECT sales_packaging_wage_total_thb FROM labor.employee_daily_wages WHERE id = @wage_id;",
                    cancellationToken,
                    ("wage_id", wageResult.Value.EmployeeDailyWageId)));
        }
        finally
        {
            await CleanupScenarioAsync(
                scenario,
                new[] { handlingCommandId, wageCommandId },
                cancellationToken);
        }
    }

    private static RecordSalesPackagingWorkCommand Command(Guid salesId, Scenario scenario, long wageThb) =>
        new(salesId, scenario.WorkDate, scenario.EmployeeId, scenario.PackagingItemId, wageThb);

    private static RecordSalesPackagingWorkExecution Execution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        RecordSalesPackagingWorkCommand command) =>
        new(CommandId.From(commandId), requestHash, ActorAccountId.From(actorAccountId), command);

    private static CommandRequestHash Hash(byte value)
    {
        var bytes = new byte[CommandRequestHash.Sha256Length];
        Array.Fill(bytes, value);
        return CommandRequestHash.FromSha256(bytes);
    }

    private static async ValueTask<ApplicationResult<RecordSalesPackagingWorkResult>> ExecuteHandlingAsync(
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

    private static async Task<Scenario> SeedScenarioAsync(CancellationToken cancellationToken)
    {
        var scenario = new Scenario(
            ActorAccountId: Guid.CreateVersion7(),
            EmployeeId: Guid.CreateVersion7(),
            CustomerId: Guid.CreateVersion7(),
            DraftSalesId: Guid.CreateVersion7(),
            ConfirmedSalesId: Guid.CreateVersion7(),
            PackagingItemId: Guid.CreateVersion7(),
            WorkDate: new DateOnly(2026, 9, 3));
        var now = DateTimeOffset.UtcNow;

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 V5 handling actor', true, NULL, NULL, @now);

            INSERT INTO party.employees
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@employee_id, 'P6 V5 handling employee', NULL, NULL, NULL, NULL, NULL,
                 true, 1, @now, @actor_id, NULL, NULL);

            INSERT INTO party.customers
                (id, name_zh_tw, name_th_th, phone, active, row_version,
                 created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@customer_id, 'P6 V5 handling customer', NULL, NULL, true, 1,
                 @now, @actor_id, NULL, NULL);

            INSERT INTO sales.sales
                (id, sales_date, customer_id, status, confirmed_at, confirmed_by_account_id,
                 row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@draft_sales_id, @work_date, @customer_id, 'DRAFT', NULL, NULL,
                 1, @now, @actor_id, NULL, NULL),
                (@confirmed_sales_id, @work_date, @customer_id, 'CONFIRMED', @now, @actor_id,
                 1, @now, @actor_id, NULL, NULL);

            INSERT INTO sales_handling.sales_packaging_items
                (id, name_zh_tw, name_th_th, active, row_version,
                 created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@item_id, 'P6 V5 handling item', NULL, true, 1,
                 @now, @actor_id, NULL, NULL);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("employee_id", scenario.EmployeeId),
            ("customer_id", scenario.CustomerId),
            ("draft_sales_id", scenario.DraftSalesId),
            ("confirmed_sales_id", scenario.ConfirmedSalesId),
            ("item_id", scenario.PackagingItemId),
            ("work_date", scenario.WorkDate),
            ("now", now));

        return scenario;
    }

    private static async Task CleanupScenarioAsync(
        Scenario scenario,
        IReadOnlyCollection<Guid> commandIds,
        CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            """
            DELETE FROM audit.audit_event_subjects
            WHERE audit_event_id IN (SELECT id FROM audit.audit_events WHERE command_id = ANY(@command_ids));
            DELETE FROM audit.audit_events WHERE command_id = ANY(@command_ids);
            DELETE FROM system.outbox_messages WHERE command_id = ANY(@command_ids);
            DELETE FROM system.command_executions WHERE command_id = ANY(@command_ids);

            DELETE FROM finance.payable_outstanding_positions
            WHERE payable_id IN (
                SELECT p.id FROM finance.payables p
                JOIN labor.employee_daily_wages w ON w.id = p.employee_daily_wage_id
                WHERE w.work_date = @work_date AND w.employee_id = @employee_id);
            DELETE FROM finance.payable_obligation_items
            WHERE employee_daily_wage_id IN (
                SELECT id FROM labor.employee_daily_wages
                WHERE work_date = @work_date AND employee_id = @employee_id);
            DELETE FROM finance.payables
            WHERE employee_daily_wage_id IN (
                SELECT id FROM labor.employee_daily_wages
                WHERE work_date = @work_date AND employee_id = @employee_id);

            DELETE FROM labor.processing_wage_component_sources
            WHERE processing_wage_component_id IN (
                SELECT id FROM labor.processing_wage_components
                WHERE employee_daily_wage_id IN (
                    SELECT id FROM labor.employee_daily_wages
                    WHERE work_date = @work_date AND employee_id = @employee_id));
            DELETE FROM labor.sales_packaging_wage_components
            WHERE employee_daily_wage_id IN (
                SELECT id FROM labor.employee_daily_wages
                WHERE work_date = @work_date AND employee_id = @employee_id);
            DELETE FROM labor.processing_wage_components
            WHERE employee_daily_wage_id IN (
                SELECT id FROM labor.employee_daily_wages
                WHERE work_date = @work_date AND employee_id = @employee_id);
            DELETE FROM labor.employee_daily_wages
            WHERE work_date = @work_date AND employee_id = @employee_id;

            DELETE FROM sales_handling.sales_packaging_work_records
            WHERE employee_id = @employee_id AND work_date = @work_date;
            DELETE FROM sales_handling.sales_packaging_items WHERE id = @item_id;
            DELETE FROM sales.sales WHERE id = @draft_sales_id OR id = @confirmed_sales_id;
            DELETE FROM party.customers WHERE id = @customer_id;
            DELETE FROM party.employees WHERE id = @employee_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds.ToArray()),
            ("work_date", scenario.WorkDate),
            ("employee_id", scenario.EmployeeId),
            ("item_id", scenario.PackagingItemId),
            ("draft_sales_id", scenario.DraftSalesId),
            ("confirmed_sales_id", scenario.ConfirmedSalesId),
            ("customer_id", scenario.CustomerId),
            ("actor_id", scenario.ActorAccountId));
    }

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
        Guid DraftSalesId,
        Guid ConfirmedSalesId,
        Guid PackagingItemId,
        DateOnly WorkDate);
}
