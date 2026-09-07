using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Party;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class CustomerMasterIntegrationTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString = "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Create_and_update_customer_persist_master_data_with_audit_and_idempotent_replay()
    {
        var ct = TestContext.Current.CancellationToken;
        var actorId = Guid.CreateVersion7();
        var createCommandId = Guid.CreateVersion7();
        var updateCommandId = Guid.CreateVersion7();
        Guid customerId = Guid.Empty;
        await SeedActorAsync(actorId, ct);

        try
        {
            var createExecution = new CreateCustomerExecution(
                CommandId.From(createCommandId), Hash(11), ActorAccountId.From(actorId),
                new CreateCustomerCommand("客戶整合測試", "ลูกค้าทดสอบ", "0812345678", true));
            var created = await ExecuteCreateAsync(createExecution, ct);
            Assert.True(created.IsSuccess);
            customerId = created.Value.CustomerId;
            Assert.NotEqual(Guid.Empty, customerId);
            Assert.Equal(1, created.Value.RowVersion);

            var row = await ReadCustomerAsync(customerId, ct);
            Assert.Equal("客戶整合測試", row.NameZhTw);
            Assert.Equal("ลูกค้าทดสอบ", row.NameThTh);
            Assert.Equal("0812345678", row.Phone);
            Assert.True(row.Active);
            Assert.Equal(1, row.RowVersion);
            Assert.Equal(actorId, row.CreatedByAccountId);
            Assert.Null(row.DeletedAt);

            Assert.Equal(1L, await AuditCountAsync(createCommandId, "CreateCustomer", "CREATE", null, 1, ct));
            var replay = await ExecuteCreateAsync(createExecution, ct);
            Assert.True(replay.IsSuccess);
            Assert.Equal(created.Value, replay.Value);
            Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM party.customers WHERE id = @id;", ct, ("id", customerId)));
            Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM audit.audit_events WHERE command_id = @id;", ct, ("id", createCommandId)));

            var updateExecution = new UpdateCustomerExecution(
                CommandId.From(updateCommandId), Hash(12), ActorAccountId.From(actorId),
                new UpdateCustomerCommand(customerId, 1, "客戶更新", null, "0899999999", false));
            var updated = await ExecuteUpdateAsync(updateExecution, ct);
            Assert.True(updated.IsSuccess);
            Assert.Equal(new CustomerMasterWriteResult(customerId, 2), updated.Value);

            row = await ReadCustomerAsync(customerId, ct);
            Assert.Equal("客戶更新", row.NameZhTw);
            Assert.Null(row.NameThTh);
            Assert.Equal("0899999999", row.Phone);
            Assert.False(row.Active);
            Assert.Equal(2, row.RowVersion);
            Assert.Equal(1L, await AuditCountAsync(updateCommandId, "UpdateCustomer", "UPDATE", 1, 2, ct));

            var page = await ReadMasterAsync("客戶更新", CustomerMasterStatusFilter.Inactive, ct);
            var projected = Assert.Single(page.Items, item => item.Id == customerId);
            Assert.Equal("0899999999", projected.Phone);
            Assert.Equal(2, projected.RowVersion);
        }
        finally
        {
            await CleanupAsync(actorId, customerId, [createCommandId, updateCommandId], ct);
        }
    }

    [Fact]
    public async Task Update_stale_or_deleted_customer_rolls_back_command_acquisition()
    {
        var ct = TestContext.Current.CancellationToken;
        var actorId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        var staleId = Guid.CreateVersion7();
        var deletedId = Guid.CreateVersion7();
        await SeedActorAsync(actorId, ct);
        await ExecuteNonQueryAsync(
            """
            INSERT INTO party.customers
                (id, name_zh_tw, name_th_th, phone, active, row_version, created_at, created_by_account_id)
            VALUES (@id, '客戶', NULL, NULL, true, 1, now(), @actor);
            """, ct, ("id", customerId), ("actor", actorId));

        try
        {
            var stale = await ExecuteUpdateAsync(new UpdateCustomerExecution(
                CommandId.From(staleId), Hash(13), ActorAccountId.From(actorId),
                new UpdateCustomerCommand(customerId, 2, "客戶", null, null, true)), ct);
            Assert.True(stale.IsFailure);
            Assert.Equal(ApplicationErrorKind.Conflict, stale.Error.Kind);
            Assert.Equal(CustomerMasterErrorCodes.StaleRowVersion, stale.Error.Code);

            await ExecuteNonQueryAsync(
                "UPDATE party.customers SET deleted_at = now(), deleted_by_account_id = @actor, row_version = 2 WHERE id = @id;",
                ct, ("actor", actorId), ("id", customerId));
            var deleted = await ExecuteUpdateAsync(new UpdateCustomerExecution(
                CommandId.From(deletedId), Hash(14), ActorAccountId.From(actorId),
                new UpdateCustomerCommand(customerId, 2, "客戶", null, null, true)), ct);
            Assert.True(deleted.IsFailure);
            Assert.Equal(CustomerMasterErrorCodes.CustomerDeleted, deleted.Error.Code);

            Assert.Equal(0L, await ScalarAsync<long>(
                "SELECT count(*) FROM system.command_executions WHERE command_id = ANY(@ids);",
                ct, ("ids", new[] { staleId, deletedId })));
        }
        finally
        {
            await CleanupAsync(actorId, customerId, [staleId, deletedId], ct);
        }
    }

    private static async ValueTask<ApplicationResult<CustomerMasterWriteResult>> ExecuteCreateAsync(CreateCustomerExecution execution, CancellationToken ct)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICustomerMasterExecutor>().CreateAsync(execution, ct);
    }

    private static async ValueTask<ApplicationResult<CustomerMasterWriteResult>> ExecuteUpdateAsync(UpdateCustomerExecution execution, CancellationToken ct)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICustomerMasterExecutor>().UpdateAsync(execution, ct);
    }

    private static async ValueTask<CustomerMasterPage> ReadMasterAsync(string search, CustomerMasterStatusFilter status, CancellationToken ct)
    {
        var services = new ServiceCollection();
        services.AddErpPersistence(GetConnectionString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICustomerMasterReader>().GetAsync(new(search, status, 0, 100), ct);
    }

    private static CommandRequestHash Hash(byte value)
    {
        var bytes = new byte[CommandRequestHash.Sha256Length];
        Array.Fill(bytes, value);
        return CommandRequestHash.FromSha256(bytes);
    }

    private static Task SeedActorAsync(Guid actorId, CancellationToken ct) => ExecuteNonQueryAsync(
        "INSERT INTO system.accounts (id, display_name, active, identity_issuer, identity_subject, created_at) VALUES (@id, 'P8 customer master actor', true, NULL, NULL, now());",
        ct, ("id", actorId));

    private static async Task<CustomerDbRow> ReadCustomerAsync(Guid id, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT name_zh_tw, name_th_th, phone, active, row_version, created_by_account_id, deleted_at FROM party.customers WHERE id = @id;",
            connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        Assert.True(await reader.ReadAsync(ct));
        string? GetText(int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        return new(GetText(0), GetText(1), GetText(2), reader.GetBoolean(3), reader.GetInt64(4), reader.GetGuid(5), reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6));
    }

    private static Task<long> AuditCountAsync(Guid commandId, string commandType, string changeKind, long? before, long after, CancellationToken ct) => ScalarAsync<long>(
        """
        SELECT count(*)
        FROM audit.audit_event_subjects s
        JOIN audit.audit_events e ON e.id = s.audit_event_id
        WHERE e.command_id = @command_id AND e.command_type = @command_type
          AND e.event_kind = 'BUSINESS_COMMAND' AND s.subject_kind = 'party.customer'
          AND s.change_kind = @change_kind
          AND s.before_row_version IS NOT DISTINCT FROM @before_version
          AND s.after_row_version = @after_version;
        """, ct, ("command_id", commandId), ("command_type", commandType), ("change_kind", changeKind), ("before_version", before ?? (object)DBNull.Value), ("after_version", after));

    private static async Task CleanupAsync(Guid actorId, Guid customerId, IReadOnlyCollection<Guid> commandIds, CancellationToken ct)
    {
        if (commandIds.Count > 0)
        {
            await ExecuteNonQueryAsync(
                """
                DELETE FROM audit.audit_event_subjects WHERE audit_event_id IN (SELECT id FROM audit.audit_events WHERE command_id = ANY(@ids));
                DELETE FROM audit.audit_events WHERE command_id = ANY(@ids);
                DELETE FROM system.command_executions WHERE command_id = ANY(@ids);
                """, ct, ("ids", commandIds.ToArray()));
        }
        if (customerId != Guid.Empty) await ExecuteNonQueryAsync("DELETE FROM party.customers WHERE id = @id;", ct, ("id", customerId));
        await ExecuteNonQueryAsync("DELETE FROM system.accounts WHERE id = @id;", ct, ("id", actorId));
    }

    private static async Task<T> ScalarAsync<T>(string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        var value = await command.ExecuteScalarAsync(ct);
        return (T)(value ?? throw new InvalidOperationException("Expected scalar result."));
    }

    private static async Task ExecuteNonQueryAsync(string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string GetConnectionString() => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)) ? LocalDevelopmentConnectionString : Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)!;
    private static async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var builder = new NpgsqlConnectionStringBuilder(GetConnectionString()) { Pooling = false, Timeout = 5, CommandTimeout = 15 };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    private sealed record CustomerDbRow(string? NameZhTw, string? NameThTh, string? Phone, bool Active, long RowVersion, Guid CreatedByAccountId, DateTimeOffset? DeletedAt);
}
