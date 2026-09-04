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

public sealed class PayableAdjustmentCorrectionIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Correct_adjustment_to_smaller_deduction_rebuilds_outstanding_preserves_recorded_metadata_and_links_audit_with_replay()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedPayableAsync(cancellationToken);
        var addCommandId = Guid.CreateVersion7();
        var correctionCommandId = Guid.CreateVersion7();

        try
        {
            var added = await ExecuteAddAsync(
                AddExecution(
                    addCommandId,
                    scenario.ActorAccountId,
                    Hash(1),
                    new AddPayableAdjustmentCommand(
                        scenario.PayableId,
                        1,
                        PayableAdjustmentType.SUPPLIER_QUALITY_WEIGHT_DEDUCTION,
                        -2000,
                        "original reason")),
                cancellationToken);
            Assert.True(added.IsSuccess);
            Assert.Equal(8000, added.Value.OutstandingThb);

            var correctionExecution = CorrectExecution(
                correctionCommandId,
                scenario.ActorAccountId,
                Hash(2),
                new CorrectPayableAdjustmentCommand(
                    scenario.PayableId,
                    added.Value.PayableAdjustmentId,
                    -1000,
                    "corrected reason",
                    2,
                    "entry correction"));
            var correction = await ExecuteCorrectionAsync(correctionExecution, cancellationToken);

            Assert.True(correction.IsSuccess);
            Assert.Equal(added.Value.PayableAdjustmentId, correction.Value.PayableAdjustmentId);
            Assert.Equal(-2000, correction.Value.PreviousAmountDeltaThb);
            Assert.Equal(-1000, correction.Value.CorrectedAmountDeltaThb);
            Assert.Equal("original reason", correction.Value.PreviousReasonText);
            Assert.Equal("corrected reason", correction.Value.CorrectedReasonText);
            Assert.Equal(9000, correction.Value.OutstandingThb);
            Assert.Equal(3, correction.Value.OutstandingVersion);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM finance.payable_adjustments
                    WHERE id = @adjustment_id
                      AND payable_id = @payable_id
                      AND adjustment_type = 'SUPPLIER_QUALITY_WEIGHT_DEDUCTION'
                      AND amount_delta_thb = -1000
                      AND reason_text = 'corrected reason'
                      AND recorded_at = @recorded_at
                      AND recorded_by_account_id = @actor_id;
                    """,
                    cancellationToken,
                    ("adjustment_id", added.Value.PayableAdjustmentId),
                    ("payable_id", scenario.PayableId),
                    ("recorded_at", added.Value.RecordedAt),
                    ("actor_id", scenario.ActorAccountId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM finance.payable_outstanding_positions
                    WHERE payable_id = @payable_id
                      AND adjustment_total_thb = -1000
                      AND settlement_total_thb = 0
                      AND outstanding_thb = 9000
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
                      AND e.command_type = 'CorrectPayableAdjustment'
                      AND e.event_kind = 'CORRECTION'
                      AND e.reason_text = 'entry correction'
                      AND s.subject_kind = 'finance.payable-adjustment'
                      AND s.change_kind = 'UPDATE'
                      AND s.subject_key ->> 'id' = @adjustment_id_text
                      AND (s.change_summary -> 'amountDeltaThb' ->> 'before')::bigint = -2000
                      AND (s.change_summary -> 'amountDeltaThb' ->> 'after')::bigint = -1000
                      AND s.change_summary -> 'reasonText' ->> 'before' = 'original reason'
                      AND s.change_summary -> 'reasonText' ->> 'after' = 'corrected reason';
                    """,
                    cancellationToken,
                    ("command_id", correctionCommandId),
                    ("adjustment_id_text", added.Value.PayableAdjustmentId.ToString())));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.correction_links l
                    JOIN audit.audit_events correction ON correction.id = l.correction_audit_event_id
                    JOIN audit.audit_events corrected ON corrected.id = l.corrected_audit_event_id
                    WHERE correction.command_id = @correction_command_id
                      AND corrected.command_id = @add_command_id
                      AND l.correction_mode = 'DIRECT_AMENDMENT';
                    """,
                    cancellationToken,
                    ("correction_command_id", correctionCommandId),
                    ("add_command_id", addCommandId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM system.outbox_messages
                    WHERE command_id = @command_id
                      AND message_type = 'finance.payable-adjustment-corrected';
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
            await CleanupAsync(scenario, [addCommandId, correctionCommandId], cancellationToken);
        }
    }

    [Fact]
    public async Task Correct_adjustment_to_larger_deduction_updates_adjustment_projection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedPayableAsync(cancellationToken);
        var addCommandId = Guid.CreateVersion7();
        var correctionCommandId = Guid.CreateVersion7();

        try
        {
            var added = await ExecuteAddAsync(
                AddExecution(
                    addCommandId,
                    scenario.ActorAccountId,
                    Hash(10),
                    new AddPayableAdjustmentCommand(
                        scenario.PayableId,
                        1,
                        PayableAdjustmentType.SUPPLIER_QUALITY_WEIGHT_DEDUCTION,
                        -1000,
                        null)),
                cancellationToken);
            Assert.True(added.IsSuccess);
            Assert.Equal(9000, added.Value.OutstandingThb);

            var correction = await ExecuteCorrectionAsync(
                CorrectExecution(
                    correctionCommandId,
                    scenario.ActorAccountId,
                    Hash(11),
                    new CorrectPayableAdjustmentCommand(
                        scenario.PayableId,
                        added.Value.PayableAdjustmentId,
                        -4000,
                        null,
                        2,
                        null)),
                cancellationToken);

            Assert.True(correction.IsSuccess);
            Assert.Equal(-1000, correction.Value.PreviousAmountDeltaThb);
            Assert.Equal(-4000, correction.Value.CorrectedAmountDeltaThb);
            Assert.Equal(6000, correction.Value.OutstandingThb);
            Assert.Equal(3, correction.Value.OutstandingVersion);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM finance.payable_outstanding_positions
                    WHERE payable_id = @payable_id
                      AND adjustment_total_thb = -4000
                      AND outstanding_thb = 6000
                      AND row_version = 3;
                    """,
                    cancellationToken,
                    ("payable_id", scenario.PayableId)));
        }
        finally
        {
            await CleanupAsync(scenario, [addCommandId, correctionCommandId], cancellationToken);
        }
    }

    [Fact]
    public async Task Correct_adjustment_reason_only_preserves_monetary_projection_and_advances_concurrency_version()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedPayableAsync(cancellationToken);
        var addCommandId = Guid.CreateVersion7();
        var correctionCommandId = Guid.CreateVersion7();

        try
        {
            var added = await ExecuteAddAsync(
                AddExecution(
                    addCommandId,
                    scenario.ActorAccountId,
                    Hash(20),
                    new AddPayableAdjustmentCommand(
                        scenario.PayableId,
                        1,
                        PayableAdjustmentType.SUPPLIER_QUALITY_WEIGHT_DEDUCTION,
                        -2000,
                        "wrong label")),
                cancellationToken);
            Assert.True(added.IsSuccess);

            var correction = await ExecuteCorrectionAsync(
                CorrectExecution(
                    correctionCommandId,
                    scenario.ActorAccountId,
                    Hash(21),
                    new CorrectPayableAdjustmentCommand(
                        scenario.PayableId,
                        added.Value.PayableAdjustmentId,
                        -2000,
                        "correct label",
                        2,
                        "text correction")),
                cancellationToken);

            Assert.True(correction.IsSuccess);
            Assert.Equal(-2000, correction.Value.PreviousAmountDeltaThb);
            Assert.Equal(-2000, correction.Value.CorrectedAmountDeltaThb);
            Assert.Equal(8000, correction.Value.OutstandingThb);
            Assert.Equal(3, correction.Value.OutstandingVersion);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM finance.payable_adjustments
                    WHERE id = @adjustment_id
                      AND amount_delta_thb = -2000
                      AND reason_text = 'correct label';
                    """,
                    cancellationToken,
                    ("adjustment_id", added.Value.PayableAdjustmentId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM finance.payable_outstanding_positions
                    WHERE payable_id = @payable_id
                      AND adjustment_total_thb = -2000
                      AND outstanding_thb = 8000
                      AND row_version = 3;
                    """,
                    cancellationToken,
                    ("payable_id", scenario.PayableId)));
        }
        finally
        {
            await CleanupAsync(scenario, [addCommandId, correctionCommandId], cancellationToken);
        }
    }

    [Fact]
    public async Task Adjustment_correction_negative_outstanding_stale_version_and_no_change_roll_back_without_mutation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedPayableAsync(cancellationToken);
        var addCommandId = Guid.CreateVersion7();
        var overCommandId = Guid.CreateVersion7();
        var staleCommandId = Guid.CreateVersion7();
        var noChangeCommandId = Guid.CreateVersion7();

        try
        {
            var added = await ExecuteAddAsync(
                AddExecution(
                    addCommandId,
                    scenario.ActorAccountId,
                    Hash(30),
                    new AddPayableAdjustmentCommand(
                        scenario.PayableId,
                        1,
                        PayableAdjustmentType.SUPPLIER_QUALITY_WEIGHT_DEDUCTION,
                        -8000,
                        "quality deduction")),
                cancellationToken);
            Assert.True(added.IsSuccess);
            Assert.Equal(2000, added.Value.OutstandingThb);

            var over = await ExecuteCorrectionAsync(
                CorrectExecution(
                    overCommandId,
                    scenario.ActorAccountId,
                    Hash(31),
                    new CorrectPayableAdjustmentCommand(
                        scenario.PayableId,
                        added.Value.PayableAdjustmentId,
                        -11000,
                        "quality deduction",
                        2,
                        null)),
                cancellationToken);
            Assert.True(over.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, over.Error.Kind);
            Assert.Equal(FinanceApplicationErrorCodes.AdjustmentCorrectionCausesNegativeOutstanding, over.Error.Code);

            var stale = await ExecuteCorrectionAsync(
                CorrectExecution(
                    staleCommandId,
                    scenario.ActorAccountId,
                    Hash(32),
                    new CorrectPayableAdjustmentCommand(
                        scenario.PayableId,
                        added.Value.PayableAdjustmentId,
                        -5000,
                        "quality deduction",
                        1,
                        null)),
                cancellationToken);
            Assert.True(stale.IsFailure);
            Assert.Equal(FinanceApplicationErrorCodes.OutstandingChanged, stale.Error.Code);

            var noChange = await ExecuteCorrectionAsync(
                CorrectExecution(
                    noChangeCommandId,
                    scenario.ActorAccountId,
                    Hash(33),
                    new CorrectPayableAdjustmentCommand(
                        scenario.PayableId,
                        added.Value.PayableAdjustmentId,
                        -8000,
                        "quality deduction",
                        2,
                        null)),
                cancellationToken);
            Assert.True(noChange.IsFailure);
            Assert.Equal(ApplicationErrorKind.Validation, noChange.Error.Kind);
            Assert.Equal(FinanceApplicationErrorCodes.AdjustmentCorrectionNoChange, noChange.Error.Code);

            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM finance.payable_adjustments
                    WHERE id = @adjustment_id
                      AND amount_delta_thb = -8000
                      AND reason_text = 'quality deduction';
                    """,
                    cancellationToken,
                    ("adjustment_id", added.Value.PayableAdjustmentId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM finance.payable_outstanding_positions
                    WHERE payable_id = @payable_id
                      AND adjustment_total_thb = -8000
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
                [addCommandId, overCommandId, staleCommandId, noChangeCommandId],
                cancellationToken);
        }
    }

    private static AddPayableAdjustmentExecution AddExecution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        AddPayableAdjustmentCommand command) =>
        new(CommandId.From(commandId), requestHash, ActorAccountId.From(actorAccountId), command);

    private static CorrectPayableAdjustmentExecution CorrectExecution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        CorrectPayableAdjustmentCommand command) =>
        new(CommandId.From(commandId), requestHash, ActorAccountId.From(actorAccountId), command);

    private static CommandRequestHash Hash(byte value)
    {
        var bytes = new byte[CommandRequestHash.Sha256Length];
        Array.Fill(bytes, value);
        return CommandRequestHash.FromSha256(bytes);
    }

    private static async ValueTask<ApplicationResult<AddPayableAdjustmentResult>> ExecuteAddAsync(
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

    private static async ValueTask<ApplicationResult<CorrectPayableAdjustmentResult>> ExecuteCorrectionAsync(
        CorrectPayableAdjustmentExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<ICorrectPayableAdjustmentExecutor>()
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
                (@actor_id, 'P6 V8 adjustment correction actor', true, NULL, NULL, @now);

            INSERT INTO party.suppliers
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@supplier_id, 'P6 V8 adjustment correction supplier', NULL, NULL, NULL, NULL, NULL,
                 true, 1, @now, @actor_id, NULL, NULL);

            INSERT INTO product.procurement_products
                (id, name_zh_tw, name_th_th, unit_code, default_storage_location_id,
                 active, row_version, created_at, created_by_account_id, deleted_at, deleted_by_account_id)
            VALUES
                (@product_id, 'P6 V8 adjustment correction product', NULL, 'kg', NULL,
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
            DELETE FROM finance.payable_adjustments WHERE payable_id = @payable_id;
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
