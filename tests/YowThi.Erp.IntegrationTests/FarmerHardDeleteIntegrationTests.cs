using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.DataProtection;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class FarmerHardDeleteIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Hard_delete_physically_removes_farmer_retains_audit_and_replays_after_target_is_gone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedFarmerAsync(DependencyKind.None, cancellationToken);
        var commandId = Guid.CreateVersion7();
        var command = new HardDeleteFarmerCommand(scenario.FarmerId, 1);
        var execution = Execution(commandId, scenario.ActorAccountId, Hash(1), command);

        try
        {
            var result = await ExecuteAsync(execution, cancellationToken);
            Assert.True(result.IsSuccess);
            Assert.Equal(scenario.FarmerId, result.Value.FarmerId);
            Assert.Equal(
                0L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM party.farmers WHERE id = @farmer_id;",
                    cancellationToken,
                    ("farmer_id", scenario.FarmerId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM audit.audit_event_subjects s
                    JOIN audit.audit_events e ON e.id = s.audit_event_id
                    WHERE e.command_id = @command_id
                      AND e.command_type = 'HardDeleteFarmer'
                      AND e.event_kind = 'HARD_DELETE'
                      AND s.subject_kind = 'party.farmer'
                      AND s.change_kind = 'HARD_DELETE'
                      AND s.subject_key ->> 'id' = @farmer_id_text
                      AND s.before_row_version = 1
                      AND s.after_row_version IS NULL;
                    """,
                    cancellationToken,
                    ("command_id", commandId),
                    ("farmer_id_text", scenario.FarmerId.ToString())));

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
            Assert.Equal(FarmerHardDeleteErrorCodes.IdempotencyKeyReused, changedHash.Error.Code);
        }
        finally
        {
            await CleanupAsync(scenario, [commandId], cancellationToken);
        }
    }

    [Fact]
    public async Task Hard_delete_blocks_existing_procurement_entry_without_cascade_or_command_residue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedFarmerAsync(DependencyKind.ProcurementEntry, cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            var result = await ExecuteAsync(
                Execution(
                    commandId,
                    scenario.ActorAccountId,
                    Hash(3),
                    new HardDeleteFarmerCommand(scenario.FarmerId, 1)),
                cancellationToken);

            AssertDependencyBlocked(result);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM party.farmers WHERE id = @farmer_id;",
                    cancellationToken,
                    ("farmer_id", scenario.FarmerId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM procurement.procurement_entries
                    WHERE id = @entry_id
                      AND farmer_id = @farmer_id
                      AND source_type = 'FARMER'
                      AND row_version = 1;
                    """,
                    cancellationToken,
                    ("entry_id", scenario.ProcurementEntryId!.Value),
                    ("farmer_id", scenario.FarmerId)));
            await AssertNoCommandResidueAsync(commandId, cancellationToken);
        }
        finally
        {
            await CleanupAsync(scenario, [commandId], cancellationToken);
        }
    }

    [Fact]
    public async Task Hard_delete_blocks_existing_finance_payable_without_cascade_or_command_residue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = await SeedFarmerAsync(DependencyKind.Payable, cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            var result = await ExecuteAsync(
                Execution(
                    commandId,
                    scenario.ActorAccountId,
                    Hash(4),
                    new HardDeleteFarmerCommand(scenario.FarmerId, 1)),
                cancellationToken);

            AssertDependencyBlocked(result);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM party.farmers WHERE id = @farmer_id;",
                    cancellationToken,
                    ("farmer_id", scenario.FarmerId)));
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    """
                    SELECT count(*)
                    FROM finance.payables
                    WHERE id = @payable_id
                      AND farmer_id = @farmer_id
                      AND payable_kind = 'PROCUREMENT_FARMER'
                      AND row_version = 1;
                    """,
                    cancellationToken,
                    ("payable_id", scenario.PayableId!.Value),
                    ("farmer_id", scenario.FarmerId)));
            await AssertNoCommandResidueAsync(commandId, cancellationToken);
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
        var scenario = await SeedFarmerAsync(DependencyKind.None, cancellationToken);
        var commandId = Guid.CreateVersion7();

        try
        {
            var result = await ExecuteAsync(
                Execution(
                    commandId,
                    scenario.ActorAccountId,
                    Hash(5),
                    new HardDeleteFarmerCommand(scenario.FarmerId, 2)),
                cancellationToken);

            Assert.True(result.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, result.Error.Kind);
            Assert.Equal(FarmerHardDeleteErrorCodes.StaleRowVersion, result.Error.Code);
            Assert.Equal(
                1L,
                await ScalarAsync<long>(
                    "SELECT count(*) FROM party.farmers WHERE id = @farmer_id;",
                    cancellationToken,
                    ("farmer_id", scenario.FarmerId)));
            await AssertNoCommandResidueAsync(commandId, cancellationToken);
        }
        finally
        {
            await CleanupAsync(scenario, [commandId], cancellationToken);
        }
    }

    private static void AssertDependencyBlocked(ApplicationResult<HardDeleteFarmerResult> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(ApplicationErrorKind.Conflict, result.Error.Kind);
        Assert.Equal(FarmerHardDeleteErrorCodes.DependencyBlocked, result.Error.Code);
    }

    private static async Task AssertNoCommandResidueAsync(Guid commandId, CancellationToken cancellationToken)
    {
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
        Assert.Equal(
            0L,
            await ScalarAsync<long>(
                "SELECT count(*) FROM system.outbox_messages WHERE command_id = @command_id;",
                cancellationToken,
                ("command_id", commandId)));
    }

    private static HardDeleteFarmerExecution Execution(
        Guid commandId,
        Guid actorAccountId,
        CommandRequestHash requestHash,
        HardDeleteFarmerCommand command) =>
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

    private static async ValueTask<ApplicationResult<HardDeleteFarmerResult>> ExecuteAsync(
        HardDeleteFarmerExecution execution,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IHardDeleteFarmerExecutor>()
            .ExecuteAsync(execution, cancellationToken);
    }

    private static async Task<Scenario> SeedFarmerAsync(
        DependencyKind dependency,
        CancellationToken cancellationToken)
    {
        var scenario = new Scenario(
            ActorAccountId: Guid.CreateVersion7(),
            FarmerId: Guid.CreateVersion7(),
            ProcurementProductId: dependency == DependencyKind.None ? null : Guid.CreateVersion7(),
            ProcurementBatchId: dependency == DependencyKind.None ? null : Guid.CreateVersion7(),
            ProcurementEntryId: dependency == DependencyKind.ProcurementEntry ? Guid.CreateVersion7() : null,
            PayableId: dependency == DependencyKind.Payable ? Guid.CreateVersion7() : null);
        var now = DateTimeOffset.UtcNow;
        var suffix = scenario.FarmerId.ToString("N");

        await ExecuteNonQueryAsync(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@actor_id, 'P6 V8 C18 farmer hard delete actor', true, NULL, NULL, @now);

            INSERT INTO party.farmers
                (id, name_zh_tw, name_th_th, bank_name, bank_account, phone, address,
                 active, row_version, created_at, created_by_account_id)
            VALUES
                (@farmer_id, @farmer_name, NULL, NULL, NULL, NULL, NULL,
                 true, 1, @now, @actor_id);
            """,
            cancellationToken,
            ("actor_id", scenario.ActorAccountId),
            ("farmer_id", scenario.FarmerId),
            ("farmer_name", $"P6 V8 C18 farmer {suffix}"),
            ("now", now));

        if (dependency != DependencyKind.None)
        {
            await ExecuteNonQueryAsync(
                """
                INSERT INTO product.procurement_products
                    (id, name_zh_tw, name_th_th, unit_code, default_storage_location_id,
                     active, row_version, created_at, created_by_account_id)
                VALUES
                    (@product_id, @product_name, NULL, 'kg', NULL,
                     true, 1, @now, @actor_id);

                INSERT INTO procurement.procurement_batches
                    (id, procurement_date, procurement_product_id, procurement_status, lifecycle_status,
                     processing_route_id, processing_route_version_id,
                     completed_at, completed_by_account_id, closed_at, closed_by_account_id,
                     row_version, created_at, created_by_account_id)
                VALUES
                    (@batch_id, DATE '2026-09-05', @product_id, 'OPEN', 'ACTIVE',
                     NULL, NULL, NULL, NULL, NULL, NULL,
                     1, @now, @actor_id);
                """,
                cancellationToken,
                ("product_id", scenario.ProcurementProductId!.Value),
                ("product_name", $"P6 V8 C18 product {suffix}"),
                ("batch_id", scenario.ProcurementBatchId!.Value),
                ("actor_id", scenario.ActorAccountId),
                ("now", now));
        }

        if (scenario.ProcurementEntryId is { } entryId)
        {
            await ExecuteNonQueryAsync(
                """
                INSERT INTO procurement.procurement_entries
                    (id, procurement_batch_id, source_type, supplier_id, farmer_id,
                     net_quantity, unit_code_snapshot, unit_price, amount_thb, company_pickup,
                     recorded_at, recorded_by_account_id, row_version)
                VALUES
                    (@entry_id, @batch_id, 'FARMER', NULL, @farmer_id,
                     1, 'kg', 1, 1, false,
                     @now, @actor_id, 1);
                """,
                cancellationToken,
                ("entry_id", entryId),
                ("batch_id", scenario.ProcurementBatchId!.Value),
                ("farmer_id", scenario.FarmerId),
                ("actor_id", scenario.ActorAccountId),
                ("now", now));
        }

        if (scenario.PayableId is { } payableId)
        {
            await ExecuteNonQueryAsync(
                """
                INSERT INTO finance.payables
                    (id, payable_kind, procurement_batch_id, supplier_id, farmer_id,
                     outsourced_supply_detail_id, employee_daily_wage_id, created_at, row_version)
                VALUES
                    (@payable_id, 'PROCUREMENT_FARMER', @batch_id, NULL, @farmer_id,
                     NULL, NULL, @now, 1);
                """,
                cancellationToken,
                ("payable_id", payableId),
                ("batch_id", scenario.ProcurementBatchId!.Value),
                ("farmer_id", scenario.FarmerId),
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
            DELETE FROM finance.payable_outstanding_positions WHERE payable_id IN (SELECT id FROM finance.payables WHERE farmer_id = @farmer_id);
            DELETE FROM finance.payable_adjustments WHERE payable_id IN (SELECT id FROM finance.payables WHERE farmer_id = @farmer_id);
            DELETE FROM finance.payments WHERE payable_id IN (SELECT id FROM finance.payables WHERE farmer_id = @farmer_id);
            DELETE FROM finance.payables WHERE farmer_id = @farmer_id;
            DELETE FROM procurement.procurement_entries WHERE farmer_id = @farmer_id;
            DELETE FROM procurement.procurement_batches WHERE id = @batch_id;
            DELETE FROM product.procurement_products WHERE id = @product_id;
            DELETE FROM party.farmers WHERE id = @farmer_id;
            DELETE FROM system.accounts WHERE id = @actor_id;
            """,
            cancellationToken,
            ("command_ids", commandIds.ToArray()),
            ("farmer_id", scenario.FarmerId),
            ("batch_id", scenario.ProcurementBatchId ?? Guid.Empty),
            ("product_id", scenario.ProcurementProductId ?? Guid.Empty),
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

    private enum DependencyKind
    {
        None,
        ProcurementEntry,
        Payable,
    }

    private sealed record Scenario(
        Guid ActorAccountId,
        Guid FarmerId,
        Guid? ProcurementProductId,
        Guid? ProcurementBatchId,
        Guid? ProcurementEntryId,
        Guid? PayableId);
}
