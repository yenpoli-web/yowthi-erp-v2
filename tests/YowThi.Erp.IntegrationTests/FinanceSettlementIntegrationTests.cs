using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Finance;
using YowThi.Erp.Domain.Finance;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class FinanceSettlementIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Supplier_deduction_updates_outstanding_replays_and_blocks_negative_outstanding()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(cancellationToken);
        var adjustmentCommandId = Guid.CreateVersion7();
        var excessiveCommandId = Guid.CreateVersion7();
        var transportCommandId = Guid.CreateVersion7();

        try
        {
            var command = new AddPayableAdjustmentCommand(
                scenario.SupplierPayableId,
                1,
                PayableAdjustmentType.SUPPLIER_QUALITY_WEIGHT_DEDUCTION,
                -200,
                "quality deduction");
            var execution = AdjustmentExecution(
                adjustmentCommandId,
                scenario.ActorAccountId,
                Hash(1),
                command);

            var result = await ExecuteAdjustmentAsync(execution, cancellationToken);
            Assert.True(result.IsSuccess);
            Assert.Equal(-200, result.Value.AmountDeltaThb);
            Assert.Equal(800, result.Value.OutstandingThb);
            Assert.Equal(2, result.Value.OutstandingVersion);

            var replay = await ExecuteAdjustmentAsync(execution, cancellationToken);
            Assert.True(replay.IsSuccess);
            Assert.Equal(result.Value, replay.Value);

            var changedHash = await ExecuteAdjustmentAsync(
                AdjustmentExecution(adjustmentCommandId, scenario.ActorAccountId, Hash(2), command),
                cancellationToken);
            Assert.True(changedHash.IsFailure);
            Assert.Equal(FinanceApplicationErrorCodes.IdempotencyKeyReused, changedHash.Error.Code);

            var excessive = await ExecuteAdjustmentAsync(
                AdjustmentExecution(
                    excessiveCommandId,
                    scenario.ActorAccountId,
                    Hash(3),
                    new AddPayableAdjustmentCommand(
                        scenario.SupplierPayableId,
                        2,
                        PayableAdjustmentType.SUPPLIER_QUALITY_WEIGHT_DEDUCTION,
                        -801,
                        null)),
                cancellationToken);
            Assert.True(excessive.IsFailure);
            Assert.Equal(
                FinanceApplicationErrorCodes.AdjustmentCausesNegativeOutstanding,
                excessive.Error.Code);

            var transport = await ExecuteAdjustmentAsync(
                AdjustmentExecution(
                    transportCommandId,
                    scenario.ActorAccountId,
                    Hash(4),
                    new AddPayableAdjustmentCommand(
                        scenario.TransportPayableId,
                        1,
                        PayableAdjustmentType.SUPPLIER_QUALITY_WEIGHT_DEDUCTION,
                        -1,
                        null)),
                cancellationToken);
            Assert.True(transport.IsFailure);
            Assert.Equal(FinanceApplicationErrorCodes.AdjustmentNotAllowed, transport.Error.Code);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM finance.payable_adjustments WHERE payable_id = @payable_id;",
                    cancellationToken,
                    ("payable_id", scenario.SupplierPayableId)));
            Assert.Equal(
                -200L,
                await ScalarAsync<long>(
                    "SELECT adjustment_total_thb FROM finance.payable_outstanding_positions WHERE payable_id = @payable_id;",
                    cancellationToken,
                    ("payable_id", scenario.SupplierPayableId)));
            Assert.Equal(
                800L,
                await ScalarAsync<long>(
                    "SELECT outstanding_thb FROM finance.payable_outstanding_positions WHERE payable_id = @payable_id;",
                    cancellationToken,
                    ("payable_id", scenario.SupplierPayableId)));
            Assert.Equal(
                2L,
                await ScalarAsync<long>(
                    "SELECT row_version FROM finance.payable_outstanding_positions WHERE payable_id = @payable_id;",
                    cancellationToken,
                    ("payable_id", scenario.SupplierPayableId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM system.command_executions WHERE command_id = ANY(@command_ids);",
                    cancellationToken,
                    ("command_ids", new[] { excessiveCommandId, transportCommandId })));
        }
        finally
        {
            await CleanupScenarioAsync(
                scenario,
                new[] { adjustmentCommandId, excessiveCommandId, transportCommandId },
                cancellationToken);
        }
    }

    [Fact]
    public async Task Pay_payable_supports_partial_then_default_full_and_blocks_transport_and_overpayment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(cancellationToken);
        var partialCommandId = Guid.CreateVersion7();
        var fullCommandId = Guid.CreateVersion7();
        var overCommandId = Guid.CreateVersion7();
        var transportCommandId = Guid.CreateVersion7();

        try
        {
            var partialExecution = PaymentExecution(
                partialCommandId,
                scenario.ActorAccountId,
                Hash(10),
                new PayPayableCommand(scenario.SupplierPayableId, 300, 1));
            var partial = await ExecutePaymentAsync(partialExecution, cancellationToken);
            Assert.True(partial.IsSuccess);
            Assert.Equal(300, partial.Value.AmountThb);
            Assert.Equal(700, partial.Value.OutstandingThb);
            Assert.Equal(2, partial.Value.OutstandingVersion);

            var full = await ExecutePaymentAsync(
                PaymentExecution(
                    fullCommandId,
                    scenario.ActorAccountId,
                    Hash(11),
                    new PayPayableCommand(scenario.SupplierPayableId, null, 2)),
                cancellationToken);
            Assert.True(full.IsSuccess);
            Assert.Equal(700, full.Value.AmountThb);
            Assert.Equal(0, full.Value.OutstandingThb);
            Assert.Equal(3, full.Value.OutstandingVersion);

            var replay = await ExecutePaymentAsync(partialExecution, cancellationToken);
            Assert.True(replay.IsSuccess);
            Assert.Equal(partial.Value, replay.Value);

            var over = await ExecutePaymentAsync(
                PaymentExecution(
                    overCommandId,
                    scenario.ActorAccountId,
                    Hash(12),
                    new PayPayableCommand(scenario.SupplierPayableId, 1, 3)),
                cancellationToken);
            Assert.True(over.IsFailure);
            Assert.Equal(FinanceApplicationErrorCodes.PaymentExceedsOutstanding, over.Error.Code);

            var transport = await ExecutePaymentAsync(
                PaymentExecution(
                    transportCommandId,
                    scenario.ActorAccountId,
                    Hash(13),
                    new PayPayableCommand(scenario.TransportPayableId, 100, 1)),
                cancellationToken);
            Assert.True(transport.IsFailure);
            Assert.Equal(FinanceApplicationErrorCodes.TransportSettlementUnavailable, transport.Error.Code);

            Assert.Equal(
                2L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM finance.payments WHERE payable_id = @payable_id;",
                    cancellationToken,
                    ("payable_id", scenario.SupplierPayableId)));
            Assert.Equal(
                1000L,
                await ScalarAsync<long>(
                    "SELECT settlement_total_thb FROM finance.payable_outstanding_positions WHERE payable_id = @payable_id;",
                    cancellationToken,
                    ("payable_id", scenario.SupplierPayableId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT outstanding_thb FROM finance.payable_outstanding_positions WHERE payable_id = @payable_id;",
                    cancellationToken,
                    ("payable_id", scenario.SupplierPayableId)));
        }
        finally
        {
            await CleanupScenarioAsync(
                scenario,
                new[] { partialCommandId, fullCommandId, overCommandId, transportCommandId },
                cancellationToken);
        }
    }

    [Fact]
    public async Task Receive_receivable_supports_partial_then_default_full_and_blocks_over_collection_and_stale_version()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(cancellationToken);
        var partialCommandId = Guid.CreateVersion7();
        var fullCommandId = Guid.CreateVersion7();
        var overCommandId = Guid.CreateVersion7();
        var staleCommandId = Guid.CreateVersion7();

        try
        {
            var partialExecution = ReceiptExecution(
                partialCommandId,
                scenario.ActorAccountId,
                Hash(20),
                new ReceiveReceivableCommand(scenario.ReceivableId, 500, 1));
            var partial = await ExecuteReceiptAsync(partialExecution, cancellationToken);
            Assert.True(partial.IsSuccess);
            Assert.Equal(500, partial.Value.AmountThb);
            Assert.Equal(1000, partial.Value.OutstandingThb);
            Assert.Equal(2, partial.Value.OutstandingVersion);

            var stale = await ExecuteReceiptAsync(
                ReceiptExecution(
                    staleCommandId,
                    scenario.ActorAccountId,
                    Hash(21),
                    new ReceiveReceivableCommand(scenario.ReceivableId, 1, 1)),
                cancellationToken);
            Assert.True(stale.IsFailure);
            Assert.Equal(FinanceApplicationErrorCodes.OutstandingChanged, stale.Error.Code);

            var full = await ExecuteReceiptAsync(
                ReceiptExecution(
                    fullCommandId,
                    scenario.ActorAccountId,
                    Hash(22),
                    new ReceiveReceivableCommand(scenario.ReceivableId, null, 2)),
                cancellationToken);
            Assert.True(full.IsSuccess);
            Assert.Equal(1000, full.Value.AmountThb);
            Assert.Equal(0, full.Value.OutstandingThb);
            Assert.Equal(3, full.Value.OutstandingVersion);

            var replay = await ExecuteReceiptAsync(partialExecution, cancellationToken);
            Assert.True(replay.IsSuccess);
            Assert.Equal(partial.Value, replay.Value);

            var over = await ExecuteReceiptAsync(
                ReceiptExecution(
                    overCommandId,
                    scenario.ActorAccountId,
                    Hash(23),
                    new ReceiveReceivableCommand(scenario.ReceivableId, 1, 3)),
                cancellationToken);
            Assert.True(over.IsFailure);
            Assert.Equal(FinanceApplicationErrorCodes.ReceiptExceedsOutstanding, over.Error.Code);

            Assert.Equal(
                2L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM finance.receipts WHERE receivable_id = @receivable_id;",
                    cancellationToken,
                    ("receivable_id", scenario.ReceivableId)));
            Assert.Equal(
                1500L,
                await ScalarAsync<long>(
                    "SELECT settlement_total_thb FROM finance.receivable_outstanding_positions WHERE receivable_id = @receivable_id;",
                    cancellationToken,
                    ("receivable_id", scenario.ReceivableId)));
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT outstanding_thb FROM finance.receivable_outstanding_positions WHERE receivable_id = @receivable_id;",
                    cancellationToken,
                    ("receivable_id", scenario.ReceivableId)));
        }
        finally
        {
            await CleanupScenarioAsync(
                scenario,
                new[] { partialCommandId, fullCommandId, overCommandId, staleCommandId },
                cancellationToken);
        }
    }

    [Fact]
    public async Task Concurrent_payments_with_same_outstanding_version_allow_only_one_commit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedScenarioAsync(cancellationToken);
        var firstCommandId = Guid.CreateVersion7();
        var secondCommandId = Guid.CreateVersion7();

        try
        {
            var firstTask = ExecutePaymentAsync(
                PaymentExecution(
                    firstCommandId,
                    scenario.ActorAccountId,
                    Hash(30),
                    new PayPayableCommand(scenario.SupplierPayableId, 700, 1)),
                cancellationToken).AsTask();
            var secondTask = ExecutePaymentAsync(
                PaymentExecution(
                    secondCommandId,
                    scenario.ActorAccountId,
                    Hash(31),
                    new PayPayableCommand(scenario.SupplierPayableId, 700, 1)),
                cancellationToken).AsTask();

            var results = await Task.WhenAll(firstTask, secondTask);
            var winner = Assert.Single(results, x => x.IsSuccess);
            var loser = Assert.Single(results, x => x.IsFailure);

            Assert.Equal(700, winner.Value.AmountThb);
            Assert.Equal(300, winner.Value.OutstandingThb);
            Assert.Equal(FinanceApplicationErrorCodes.OutstandingChanged, loser.Error.Code);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM finance.payments WHERE payable_id = @payable_id;",
                    cancellationToken,
                    ("payable_id", scenario.SupplierPayableId)));
            Assert.Equal(
                300L,
                await ScalarAsync<long>(
                    "SELECT outstanding_thb FROM finance.payable_outstanding_positions WHERE payable_id = @payable_id;",
                    cancellationToken,
                    ("payable_id", scenario.SupplierPayableId)));
            Assert.Equal(
                2L,
                await ScalarAsync<long>(
                    "SELECT row_version FROM finance.payable_outstanding_positions WHERE payable_id = @payable_id;",
                    cancellationToken,
                    ("payable_id", scenario.SupplierPayableId)));
        }
        finally
        {
            await CleanupScenarioAsync(
                scenario,
                new[] { firstCommandId, secondCommandId },
                cancellationToken);
        }
    }

    private static AddPayableAdjustmentExecution AdjustmentExecution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        AddPayableAdjustmentCommand command) =>
        new(CommandId.From(commandId), requestHash, ActorAccountId.From(actorAccountId), command);

    private static PayPayableExecution PaymentExecution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        PayPayableCommand command) =>
        new(CommandId.From(commandId), requestHash, ActorAccountId.From(actorAccountId), command);

    private static ReceiveReceivableExecution ReceiptExecution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        ReceiveReceivableCommand command) =>
        new(CommandId.From(commandId), requestHash, ActorAccountId.From(actorAccountId), command);

    private static CommandRequestHash Hash(byte value)
    {
        var bytes = new byte[CommandRequestHash.Sha256Length];
        Array.Fill(bytes, value);
        return CommandRequestHash.FromSha256(bytes);
    }

    private static async ValueTask<ApplicationResult<AddPayableAdjustmentResult>> ExecuteAdjustmentAsync(
        AddPayableAdjustmentExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IAddPayableAdjustmentExecutor>()
            .ExecuteAsync(execution, cancellationToken);
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

    private static async Task<Scenario> SeedScenarioAsync(CancellationToken cancellationToken)
    {
        var scenario = new Scenario(
            ActorAccountId: Guid.CreateVersion7(),
            SupplierId: Guid.CreateVersion7(),
            ProcurementProductId: Guid.CreateVersion7(),
            ProcurementBatchId: Guid.CreateVersion7(),
            SupplierPayableId: Guid.CreateVersion7(),
            TransportPayableId: Guid.CreateVersion7(),
            CustomerId: Guid.CreateVersion7(),
            SalesId: Guid.CreateVersion7(),
            ReceivableId: Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;
        var date = new DateOnly(2026, 9, 3);

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 V6 finance actor', true, NULL, NULL, @now);

            INSERT INTO party.suppliers
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@supplier_id, 'P6 V6 supplier', NULL, NULL, NULL, NULL, NULL,
                 true, 1, @now, @actor_id, NULL, NULL);

            INSERT INTO product.procurement_products
                (id, name_zh_tw, name_th_th, unit_code, default_storage_location_id,
                 active, row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@product_id, 'P6 V6 product', NULL, 'kg', NULL,
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
                (@supplier_payable_id, 'PROCUREMENT_SUPPLIER', @batch_id, @supplier_id, NULL,
                 NULL, NULL, @now, 1),
                (@transport_payable_id, 'COMPANY_PICKUP_TRANSPORT', NULL, NULL, NULL,
                 NULL, NULL, @now, 1);

            INSERT INTO finance.payable_outstanding_positions
                (payable_id, original_obligation_thb, adjustment_total_thb,
                 settlement_total_thb, outstanding_thb, row_version, updated_at)
            VALUES
                (@supplier_payable_id, 1000, 0, 0, 1000, 1, @now),
                (@transport_payable_id, 400, 0, 0, 400, 1, @now);

            INSERT INTO party.customers
                (id, name_zh_tw, name_th_th, phone, active, row_version,
                 created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@customer_id, 'P6 V6 customer', NULL, NULL, true, 1,
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
                (@receivable_id, 1500, 0, 0, 1500, 1, @now);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("supplier_id", scenario.SupplierId),
            ("product_id", scenario.ProcurementProductId),
            ("batch_id", scenario.ProcurementBatchId),
            ("supplier_payable_id", scenario.SupplierPayableId),
            ("transport_payable_id", scenario.TransportPayableId),
            ("customer_id", scenario.CustomerId),
            ("sales_id", scenario.SalesId),
            ("receivable_id", scenario.ReceivableId),
            ("date", date),
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

            DELETE FROM finance.payments
            WHERE payable_id = @supplier_payable_id OR payable_id = @transport_payable_id;
            DELETE FROM finance.payable_adjustments
            WHERE payable_id = @supplier_payable_id OR payable_id = @transport_payable_id;
            DELETE FROM finance.receipts WHERE receivable_id = @receivable_id;

            DELETE FROM finance.receivable_outstanding_positions WHERE receivable_id = @receivable_id;
            DELETE FROM finance.receivables WHERE id = @receivable_id;

            DELETE FROM finance.payable_outstanding_positions
            WHERE payable_id = @supplier_payable_id OR payable_id = @transport_payable_id;
            DELETE FROM finance.payables
            WHERE id = @supplier_payable_id OR id = @transport_payable_id;

            DELETE FROM sales.sales WHERE id = @sales_id;
            DELETE FROM party.customers WHERE id = @customer_id;
            DELETE FROM procurement.procurement_batches WHERE id = @batch_id;
            DELETE FROM product.procurement_products WHERE id = @product_id;
            DELETE FROM party.suppliers WHERE id = @supplier_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds.ToArray()),
            ("supplier_payable_id", scenario.SupplierPayableId),
            ("transport_payable_id", scenario.TransportPayableId),
            ("receivable_id", scenario.ReceivableId),
            ("sales_id", scenario.SalesId),
            ("customer_id", scenario.CustomerId),
            ("batch_id", scenario.ProcurementBatchId),
            ("product_id", scenario.ProcurementProductId),
            ("supplier_id", scenario.SupplierId),
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
        Guid SupplierId,
        Guid ProcurementProductId,
        Guid ProcurementBatchId,
        Guid SupplierPayableId,
        Guid TransportPayableId,
        Guid CustomerId,
        Guid SalesId,
        Guid ReceivableId);
}
