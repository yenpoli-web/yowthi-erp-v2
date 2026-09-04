using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Finance;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class ReceiptCorrectionIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Correct_receipt_downward_amends_receipt_rebuilds_outstanding_and_links_audit_with_replay()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedReceivableAsync(cancellationToken);
        var receiveCommandId = Guid.CreateVersion7();
        var correctionCommandId = Guid.CreateVersion7();

        try
        {
            var receipt = await ExecuteReceiptAsync(
                ReceiptExecution(
                    receiveCommandId,
                    scenario.ActorAccountId,
                    Hash(1),
                    new ReceiveReceivableCommand(scenario.ReceivableId, 8000, 1)),
                cancellationToken);
            Assert.True(receipt.IsSuccess);

            var correctionExecution = CorrectionExecution(
                correctionCommandId,
                scenario.ActorAccountId,
                Hash(2),
                new CorrectReceiptAmountCommand(
                    scenario.ReceivableId,
                    receipt.Value.ReceiptId,
                    5000,
                    2,
                    "entry correction"));
            var correction = await ExecuteCorrectionAsync(correctionExecution, cancellationToken);

            Assert.True(correction.IsSuccess);
            Assert.Equal(receipt.Value.ReceiptId, correction.Value.ReceiptId);
            Assert.Equal(8000, correction.Value.PreviousAmountThb);
            Assert.Equal(5000, correction.Value.CorrectedAmountThb);
            Assert.Equal(5000, correction.Value.OutstandingThb);
            Assert.Equal(3, correction.Value.OutstandingVersion);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM finance.receipts
                    WHERE id = @receipt_id
                      AND receivable_id = @receivable_id
                      AND amount_thb = 5000
                      AND confirmed_at = @confirmed_at
                      AND confirmed_by_account_id = @actor_id;
                    """,
                    cancellationToken,
                    ("receipt_id", receipt.Value.ReceiptId),
                    ("receivable_id", scenario.ReceivableId),
                    ("confirmed_at", receipt.Value.ConfirmedAt),
                    ("actor_id", scenario.ActorAccountId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM finance.receivable_outstanding_positions
                    WHERE receivable_id = @receivable_id
                      AND settlement_total_thb = 5000
                      AND outstanding_thb = 5000
                      AND row_version = 3;
                    """,
                    cancellationToken,
                    ("receivable_id", scenario.ReceivableId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.audit_events e
                    JOIN audit.audit_event_subjects s ON s.audit_event_id = e.id
                    WHERE e.command_id = @command_id
                      AND e.command_type = 'CorrectReceiptAmount'
                      AND e.event_kind = 'CORRECTION'
                      AND e.reason_text = 'entry correction'
                      AND s.subject_kind = 'finance.receipt'
                      AND s.change_kind = 'UPDATE'
                      AND s.subject_key ->> 'id' = @receipt_id_text
                      AND (s.change_summary -> 'amountThb' ->> 'before')::bigint = 8000
                      AND (s.change_summary -> 'amountThb' ->> 'after')::bigint = 5000;
                    """,
                    cancellationToken,
                    ("command_id", correctionCommandId),
                    ("receipt_id_text", receipt.Value.ReceiptId.ToString())));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.correction_links l
                    JOIN audit.audit_events correction ON correction.id = l.correction_audit_event_id
                    JOIN audit.audit_events corrected ON corrected.id = l.corrected_audit_event_id
                    WHERE correction.command_id = @correction_command_id
                      AND corrected.command_id = @receive_command_id
                      AND l.correction_mode = 'DIRECT_AMENDMENT';
                    """,
                    cancellationToken,
                    ("correction_command_id", correctionCommandId),
                    ("receive_command_id", receiveCommandId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM system.outbox_messages
                    WHERE command_id = @command_id
                      AND message_type = 'finance.receipt-corrected';
                    """,
                    cancellationToken,
                    ("command_id", correctionCommandId)));

            var replay = await ExecuteCorrectionAsync(correctionExecution, cancellationToken);
            Assert.True(replay.IsSuccess);
            Assert.Equal(correction.Value, replay.Value);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", correctionCommandId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.outbox_messages WHERE command_id = @command_id;",
                    cancellationToken,
                    ("command_id", correctionCommandId)));

            var changedHash = await ExecuteCorrectionAsync(
                correctionExecution with { RequestHash = Hash(3) },
                cancellationToken);
            Assert.True(changedHash.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, changedHash.Error.Kind);
            Assert.Equal(FinanceApplicationErrorCodes.IdempotencyKeyReused, changedHash.Error.Code);
        }
        finally
        {
            await CleanupAsync(scenario, [receiveCommandId, correctionCommandId], cancellationToken);
        }
    }

    [Fact]
    public async Task Correct_receipt_upward_within_current_outstanding_updates_settlement_projection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedReceivableAsync(cancellationToken);
        var receiveCommandId = Guid.CreateVersion7();
        var correctionCommandId = Guid.CreateVersion7();

        try
        {
            var receipt = await ExecuteReceiptAsync(
                ReceiptExecution(
                    receiveCommandId,
                    scenario.ActorAccountId,
                    Hash(10),
                    new ReceiveReceivableCommand(scenario.ReceivableId, 3000, 1)),
                cancellationToken);
            Assert.True(receipt.IsSuccess);
            Assert.Equal(7000, receipt.Value.OutstandingThb);

            var correction = await ExecuteCorrectionAsync(
                CorrectionExecution(
                    correctionCommandId,
                    scenario.ActorAccountId,
                    Hash(11),
                    new CorrectReceiptAmountCommand(
                        scenario.ReceivableId,
                        receipt.Value.ReceiptId,
                        6000,
                        2,
                        null)),
                cancellationToken);

            Assert.True(correction.IsSuccess);
            Assert.Equal(3000, correction.Value.PreviousAmountThb);
            Assert.Equal(6000, correction.Value.CorrectedAmountThb);
            Assert.Equal(4000, correction.Value.OutstandingThb);
            Assert.Equal(3, correction.Value.OutstandingVersion);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM finance.receivable_outstanding_positions
                    WHERE receivable_id = @receivable_id
                      AND settlement_total_thb = 6000
                      AND outstanding_thb = 4000
                      AND row_version = 3;
                    """,
                    cancellationToken,
                    ("receivable_id", scenario.ReceivableId)));
        }
        finally
        {
            await CleanupAsync(scenario, [receiveCommandId, correctionCommandId], cancellationToken);
        }
    }

    [Fact]
    public async Task Receipt_correction_overcollection_stale_version_and_no_change_roll_back_without_mutation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedReceivableAsync(cancellationToken);
        var receiveCommandId = Guid.CreateVersion7();
        var overCommandId = Guid.CreateVersion7();
        var staleCommandId = Guid.CreateVersion7();
        var noChangeCommandId = Guid.CreateVersion7();

        try
        {
            var receipt = await ExecuteReceiptAsync(
                ReceiptExecution(
                    receiveCommandId,
                    scenario.ActorAccountId,
                    Hash(20),
                    new ReceiveReceivableCommand(scenario.ReceivableId, 8000, 1)),
                cancellationToken);
            Assert.True(receipt.IsSuccess);

            var over = await ExecuteCorrectionAsync(
                CorrectionExecution(
                    overCommandId,
                    scenario.ActorAccountId,
                    Hash(21),
                    new CorrectReceiptAmountCommand(
                        scenario.ReceivableId,
                        receipt.Value.ReceiptId,
                        11000,
                        2,
                        null)),
                cancellationToken);
            Assert.True(over.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, over.Error.Kind);
            Assert.Equal(FinanceApplicationErrorCodes.ReceiptCorrectionExceedsOutstanding, over.Error.Code);

            var stale = await ExecuteCorrectionAsync(
                CorrectionExecution(
                    staleCommandId,
                    scenario.ActorAccountId,
                    Hash(22),
                    new CorrectReceiptAmountCommand(
                        scenario.ReceivableId,
                        receipt.Value.ReceiptId,
                        5000,
                        1,
                        null)),
                cancellationToken);
            Assert.True(stale.IsFailure);
            Assert.Equal(FinanceApplicationErrorCodes.OutstandingChanged, stale.Error.Code);

            var noChange = await ExecuteCorrectionAsync(
                CorrectionExecution(
                    noChangeCommandId,
                    scenario.ActorAccountId,
                    Hash(23),
                    new CorrectReceiptAmountCommand(
                        scenario.ReceivableId,
                        receipt.Value.ReceiptId,
                        8000,
                        2,
                        null)),
                cancellationToken);
            Assert.True(noChange.IsFailure);
            Assert.Equal(ApplicationErrorKind.Validation, noChange.Error.Kind);
            Assert.Equal(FinanceApplicationErrorCodes.ReceiptCorrectionNoChange, noChange.Error.Code);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM finance.receipts
                    WHERE id = @receipt_id
                      AND amount_thb = 8000;
                    """,
                    cancellationToken,
                    ("receipt_id", receipt.Value.ReceiptId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM finance.receivable_outstanding_positions
                    WHERE receivable_id = @receivable_id
                      AND settlement_total_thb = 8000
                      AND outstanding_thb = 2000
                      AND row_version = 2;
                    """,
                    cancellationToken,
                    ("receivable_id", scenario.ReceivableId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = ANY(@command_ids);",
                    cancellationToken,
                    ("command_ids", new[] { overCommandId, staleCommandId, noChangeCommandId })));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM audit.audit_events WHERE command_id = ANY(@command_ids);",
                    cancellationToken,
                    ("command_ids", new[] { overCommandId, staleCommandId, noChangeCommandId })));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.outbox_messages WHERE command_id = ANY(@command_ids);",
                    cancellationToken,
                    ("command_ids", new[] { overCommandId, staleCommandId, noChangeCommandId })));
        }
        finally
        {
            await CleanupAsync(
                scenario,
                [receiveCommandId, overCommandId, staleCommandId, noChangeCommandId],
                cancellationToken);
        }
    }

    private static ReceiveReceivableExecution ReceiptExecution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        ReceiveReceivableCommand command) =>
        new(CommandId.From(commandId), requestHash, ActorAccountId.From(actorAccountId), command);

    private static CorrectReceiptAmountExecution CorrectionExecution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        CorrectReceiptAmountCommand command) =>
        new(CommandId.From(commandId), requestHash, ActorAccountId.From(actorAccountId), command);

    private static CommandRequestHash Hash(byte value)
    {
        var bytes = new byte[CommandRequestHash.Sha256Length];
        Array.Fill(bytes, value);
        return CommandRequestHash.FromSha256(bytes);
    }

    private static async ValueTask<ApplicationResult<ReceiveReceivableResult>> ExecuteReceiptAsync(
        ReceiveReceivableExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IReceiveReceivableExecutor>()
            .ExecuteAsync(execution, cancellationToken);
    }

    private static async ValueTask<ApplicationResult<CorrectReceiptAmountResult>> ExecuteCorrectionAsync(
        CorrectReceiptAmountExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<ICorrectReceiptAmountExecutor>()
            .ExecuteAsync(execution, cancellationToken);
    }

    private static async Task<Scenario> SeedReceivableAsync(CancellationToken cancellationToken)
    {
        var scenario = new Scenario(
            ActorAccountId: Guid.CreateVersion7(),
            CustomerId: Guid.CreateVersion7(),
            SalesId: Guid.CreateVersion7(),
            ReceivableId: Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;
        var date = new DateOnly(2026, 9, 4);

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 V8 receipt correction actor', true, NULL, NULL, @now);

            INSERT INTO party.customers
                (id, name_zh_tw, name_th_th, phone, active, row_version,
                 created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@customer_id, 'P6 V8 receipt correction customer', NULL, NULL, true, 1,
                 @now, @actor_id, NULL, NULL);

            INSERT INTO sales.sales
                (id, sales_date, customer_id, status, confirmed_at, confirmed_by_account_id,
                 row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@sales_id, @date, @customer_id, 'CONFIRMED', @now, @actor_id,
                 1, @now, @actor_id, NULL, NULL);

            INSERT INTO finance.receivables
                (id, sales_id, created_at, row_version)
            VALUES
                (@receivable_id, @sales_id, @now, 1);

            INSERT INTO finance.receivable_outstanding_positions
                (receivable_id, original_obligation_thb, adjustment_total_thb,
                 settlement_total_thb, outstanding_thb, row_version, updated_at)
            VALUES
                (@receivable_id, 10000, 0, 0, 10000, 1, @now);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("customer_id", scenario.CustomerId),
            ("sales_id", scenario.SalesId),
            ("receivable_id", scenario.ReceivableId),
            ("date", date),
            ("now", now));

        return scenario;
    }

    private static Task CleanupAsync(
        Scenario scenario,
        IReadOnlyCollection<Guid> commandIds,
        CancellationToken cancellationToken) =>
        ExecuteNonQueryAsync(
            """
            DELETE FROM audit.correction_links
            WHERE correction_audit_event_id IN (
                    SELECT id FROM audit.audit_events WHERE command_id = ANY(@command_ids))
               OR corrected_audit_event_id IN (
                    SELECT id FROM audit.audit_events WHERE command_id = ANY(@command_ids));
            DELETE FROM audit.audit_event_subjects
            WHERE audit_event_id IN (SELECT id FROM audit.audit_events WHERE command_id = ANY(@command_ids));
            DELETE FROM audit.audit_events WHERE command_id = ANY(@command_ids);
            DELETE FROM system.outbox_messages WHERE command_id = ANY(@command_ids);
            DELETE FROM system.command_executions WHERE command_id = ANY(@command_ids);
            DELETE FROM finance.receipts WHERE receivable_id = @receivable_id;
            DELETE FROM finance.receivable_outstanding_positions WHERE receivable_id = @receivable_id;
            DELETE FROM finance.receivables WHERE id = @receivable_id;
            DELETE FROM sales.sales WHERE id = @sales_id;
            DELETE FROM party.customers WHERE id = @customer_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds.ToArray()),
            ("receivable_id", scenario.ReceivableId),
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

    private sealed record Scenario(
        Guid ActorAccountId,
        Guid CustomerId,
        Guid SalesId,
        Guid ReceivableId);
}
