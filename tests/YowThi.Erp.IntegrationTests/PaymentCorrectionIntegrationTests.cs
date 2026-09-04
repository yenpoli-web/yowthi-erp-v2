using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Finance;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class PaymentCorrectionIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Correct_payment_downward_amends_payment_rebuilds_outstanding_and_links_audit_with_replay()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedPayableAsync(cancellationToken);
        var payCommandId = Guid.CreateVersion7();
        var correctionCommandId = Guid.CreateVersion7();

        try
        {
            var payment = await ExecutePaymentAsync(
                PaymentExecution(
                    payCommandId,
                    scenario.ActorAccountId,
                    Hash(1),
                    new PayPayableCommand(scenario.PayableId, 8000, 1)),
                cancellationToken);
            Assert.True(payment.IsSuccess);

            var correctionExecution = CorrectionExecution(
                correctionCommandId,
                scenario.ActorAccountId,
                Hash(2),
                new CorrectPaymentAmountCommand(
                    scenario.PayableId,
                    payment.Value.PaymentId,
                    5000,
                    2,
                    "entry correction"));
            var correction = await ExecuteCorrectionAsync(correctionExecution, cancellationToken);

            Assert.True(correction.IsSuccess);
            Assert.Equal(payment.Value.PaymentId, correction.Value.PaymentId);
            Assert.Equal(8000, correction.Value.PreviousAmountThb);
            Assert.Equal(5000, correction.Value.CorrectedAmountThb);
            Assert.Equal(5000, correction.Value.OutstandingThb);
            Assert.Equal(3, correction.Value.OutstandingVersion);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM finance.payments
                    WHERE id = @payment_id
                      AND payable_id = @payable_id
                      AND amount_thb = 5000
                      AND confirmed_at = @confirmed_at
                      AND confirmed_by_account_id = @actor_id;
                    """,
                    cancellationToken,
                    ("payment_id", payment.Value.PaymentId),
                    ("payable_id", scenario.PayableId),
                    ("confirmed_at", payment.Value.ConfirmedAt),
                    ("actor_id", scenario.ActorAccountId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM finance.payable_outstanding_positions
                    WHERE payable_id = @payable_id
                      AND settlement_total_thb = 5000
                      AND outstanding_thb = 5000
                      AND row_version = 3;
                    """,
                    cancellationToken,
                    ("payable_id", scenario.PayableId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.audit_events e
                    JOIN audit.audit_event_subjects s ON s.audit_event_id = e.id
                    WHERE e.command_id = @command_id
                      AND e.command_type = 'CorrectPaymentAmount'
                      AND e.event_kind = 'CORRECTION'
                      AND e.reason_text = 'entry correction'
                      AND s.subject_kind = 'finance.payment'
                      AND s.change_kind = 'UPDATE'
                      AND s.subject_key ->> 'id' = @payment_id_text
                      AND (s.change_summary -> 'amountThb' ->> 'before')::bigint = 8000
                      AND (s.change_summary -> 'amountThb' ->> 'after')::bigint = 5000;
                    """,
                    cancellationToken,
                    ("command_id", correctionCommandId),
                    ("payment_id_text", payment.Value.PaymentId.ToString())));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.correction_links l
                    JOIN audit.audit_events correction ON correction.id = l.correction_audit_event_id
                    JOIN audit.audit_events corrected ON corrected.id = l.corrected_audit_event_id
                    WHERE correction.command_id = @correction_command_id
                      AND corrected.command_id = @pay_command_id
                      AND l.correction_mode = 'DIRECT_AMENDMENT';
                    """,
                    cancellationToken,
                    ("correction_command_id", correctionCommandId),
                    ("pay_command_id", payCommandId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM system.outbox_messages
                    WHERE command_id = @command_id
                      AND message_type = 'finance.payment-corrected';
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
            await CleanupAsync(scenario, [payCommandId, correctionCommandId], cancellationToken);
        }
    }

    [Fact]
    public async Task Correct_payment_upward_within_current_outstanding_updates_settlement_projection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedPayableAsync(cancellationToken);
        var payCommandId = Guid.CreateVersion7();
        var correctionCommandId = Guid.CreateVersion7();

        try
        {
            var payment = await ExecutePaymentAsync(
                PaymentExecution(
                    payCommandId,
                    scenario.ActorAccountId,
                    Hash(10),
                    new PayPayableCommand(scenario.PayableId, 3000, 1)),
                cancellationToken);
            Assert.True(payment.IsSuccess);
            Assert.Equal(7000, payment.Value.OutstandingThb);

            var correction = await ExecuteCorrectionAsync(
                CorrectionExecution(
                    correctionCommandId,
                    scenario.ActorAccountId,
                    Hash(11),
                    new CorrectPaymentAmountCommand(
                        scenario.PayableId,
                        payment.Value.PaymentId,
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
                    FROM finance.payable_outstanding_positions
                    WHERE payable_id = @payable_id
                      AND settlement_total_thb = 6000
                      AND outstanding_thb = 4000
                      AND row_version = 3;
                    """,
                    cancellationToken,
                    ("payable_id", scenario.PayableId)));
        }
        finally
        {
            await CleanupAsync(scenario, [payCommandId, correctionCommandId], cancellationToken);
        }
    }

    [Fact]
    public async Task Payment_correction_overpayment_stale_version_and_no_change_roll_back_without_mutation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedPayableAsync(cancellationToken);
        var payCommandId = Guid.CreateVersion7();
        var overCommandId = Guid.CreateVersion7();
        var staleCommandId = Guid.CreateVersion7();
        var noChangeCommandId = Guid.CreateVersion7();

        try
        {
            var payment = await ExecutePaymentAsync(
                PaymentExecution(
                    payCommandId,
                    scenario.ActorAccountId,
                    Hash(20),
                    new PayPayableCommand(scenario.PayableId, 8000, 1)),
                cancellationToken);
            Assert.True(payment.IsSuccess);

            var over = await ExecuteCorrectionAsync(
                CorrectionExecution(
                    overCommandId,
                    scenario.ActorAccountId,
                    Hash(21),
                    new CorrectPaymentAmountCommand(
                        scenario.PayableId,
                        payment.Value.PaymentId,
                        11000,
                        2,
                        null)),
                cancellationToken);
            Assert.True(over.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, over.Error.Kind);
            Assert.Equal(FinanceApplicationErrorCodes.PaymentCorrectionExceedsOutstanding, over.Error.Code);

            var stale = await ExecuteCorrectionAsync(
                CorrectionExecution(
                    staleCommandId,
                    scenario.ActorAccountId,
                    Hash(22),
                    new CorrectPaymentAmountCommand(
                        scenario.PayableId,
                        payment.Value.PaymentId,
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
                    new CorrectPaymentAmountCommand(
                        scenario.PayableId,
                        payment.Value.PaymentId,
                        8000,
                        2,
                        null)),
                cancellationToken);
            Assert.True(noChange.IsFailure);
            Assert.Equal(ApplicationErrorKind.Validation, noChange.Error.Kind);
            Assert.Equal(FinanceApplicationErrorCodes.PaymentCorrectionNoChange, noChange.Error.Code);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM finance.payments
                    WHERE id = @payment_id
                      AND amount_thb = 8000;
                    """,
                    cancellationToken,
                    ("payment_id", payment.Value.PaymentId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM finance.payable_outstanding_positions
                    WHERE payable_id = @payable_id
                      AND settlement_total_thb = 8000
                      AND outstanding_thb = 2000
                      AND row_version = 2;
                    """,
                    cancellationToken,
                    ("payable_id", scenario.PayableId)));
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
                [payCommandId, overCommandId, staleCommandId, noChangeCommandId],
                cancellationToken);
        }
    }

    private static PayPayableExecution PaymentExecution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        PayPayableCommand command) =>
        new(CommandId.From(commandId), requestHash, ActorAccountId.From(actorAccountId), command);

    private static CorrectPaymentAmountExecution CorrectionExecution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        CorrectPaymentAmountCommand command) =>
        new(CommandId.From(commandId), requestHash, ActorAccountId.From(actorAccountId), command);

    private static CommandRequestHash Hash(byte value)
    {
        var bytes = new byte[CommandRequestHash.Sha256Length];
        Array.Fill(bytes, value);
        return CommandRequestHash.FromSha256(bytes);
    }

    private static async ValueTask<ApplicationResult<PayPayableResult>> ExecutePaymentAsync(
        PayPayableExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IPayPayableExecutor>()
            .ExecuteAsync(execution, cancellationToken);
    }

    private static async ValueTask<ApplicationResult<CorrectPaymentAmountResult>> ExecuteCorrectionAsync(
        CorrectPaymentAmountExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<ICorrectPaymentAmountExecutor>()
            .ExecuteAsync(execution, cancellationToken);
    }

    private static async Task<Scenario> SeedPayableAsync(CancellationToken cancellationToken)
    {
        var scenario = new Scenario(
            ActorAccountId: Guid.CreateVersion7(),
            SupplierId: Guid.CreateVersion7(),
            ProcurementProductId: Guid.CreateVersion7(),
            ProcurementBatchId: Guid.CreateVersion7(),
            PayableId: Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;
        var date = new DateOnly(2026, 9, 4);

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 V8 payment correction actor', true, NULL, NULL, @now);

            INSERT INTO party.suppliers
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@supplier_id, 'P6 V8 payment correction supplier', NULL, NULL, NULL, NULL, NULL,
                 true, 1, @now, @actor_id, NULL, NULL);

            INSERT INTO product.procurement_products
                (id, name_zh_tw, name_th_th, unit_code, default_storage_location_id,
                 active, row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@product_id, 'P6 V8 payment correction product', NULL, 'kg', NULL,
                 true, 1, @now, @actor_id, NULL, NULL);

            INSERT INTO procurement.procurement_batches
                (id, procurement_date, procurement_product_id, procurement_status, lifecycle_status,
                 processing_route_id, processing_route_version_id,
                 completed_at, completed_by_account_id, closed_at, closed_by_account_id,
                 row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@batch_id, @date, @product_id, 'OPEN', 'ACTIVE',
                 NULL, NULL, NULL, NULL, NULL, NULL,
                 1, @now, @actor_id, NULL, NULL);

            INSERT INTO finance.payables
                (id, payable_kind, procurement_batch_id, supplier_id, farmer_id,
                 outsourced_supply_detail_id, employee_daily_wage_id, created_at, row_version)
            VALUES
                (@payable_id, 'PROCUREMENT_SUPPLIER', @batch_id, @supplier_id, NULL,
                 NULL, NULL, @now, 1);

            INSERT INTO finance.payable_outstanding_positions
                (payable_id, original_obligation_thb, adjustment_total_thb,
                 settlement_total_thb, outstanding_thb, row_version, updated_at)
            VALUES
                (@payable_id, 10000, 0, 0, 10000, 1, @now);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("supplier_id", scenario.SupplierId),
            ("product_id", scenario.ProcurementProductId),
            ("batch_id", scenario.ProcurementBatchId),
            ("payable_id", scenario.PayableId),
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
            DELETE FROM finance.payments WHERE payable_id = @payable_id;
            DELETE FROM finance.payable_outstanding_positions WHERE payable_id = @payable_id;
            DELETE FROM finance.payables WHERE id = @payable_id;
            DELETE FROM procurement.procurement_batches WHERE id = @batch_id;
            DELETE FROM product.procurement_products WHERE id = @product_id;
            DELETE FROM party.suppliers WHERE id = @supplier_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds.ToArray()),
            ("payable_id", scenario.PayableId),
            ("batch_id", scenario.ProcurementBatchId),
            ("product_id", scenario.ProcurementProductId),
            ("supplier_id", scenario.SupplierId),
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
        Guid SupplierId,
        Guid ProcurementProductId,
        Guid ProcurementBatchId,
        Guid PayableId);
}
