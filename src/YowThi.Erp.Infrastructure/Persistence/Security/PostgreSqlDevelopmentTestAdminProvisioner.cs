using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using YowThi.Erp.Application.Security;

namespace YowThi.Erp.Infrastructure.Persistence.Security;

internal sealed class PostgreSqlDevelopmentTestAdminProvisioner : IDevelopmentTestAdminProvisioner
{
    private readonly ErpDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public PostgreSqlDevelopmentTestAdminProvisioner(ErpDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async ValueTask<SecurityResolvedActor> EnsureAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        var npgsqlTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();
        var now = _timeProvider.GetUtcNow();

        var accountId = await FindAccountIdAsync(connection, npgsqlTransaction, cancellationToken);
        if (accountId == Guid.Empty)
        {
            accountId = Guid.CreateVersion7();
            await InsertAccountAsync(connection, npgsqlTransaction, accountId, now, cancellationToken);
        }
        else
        {
            await EnsureAccountActiveAsync(connection, npgsqlTransaction, accountId, cancellationToken);
        }

        await EnsureCapabilitiesAsync(connection, npgsqlTransaction, accountId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new SecurityResolvedActor(accountId, DevelopmentTestAdmin.DisplayName, SecurityCapabilities.All);
    }

    private static async Task<Guid> FindAccountIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT id
            FROM system.accounts
            WHERE identity_issuer = @issuer
              AND identity_subject = @subject
            FOR UPDATE;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("issuer", DevelopmentTestAdmin.IdentityIssuer);
        command.Parameters.AddWithValue("subject", DevelopmentTestAdmin.IdentitySubject);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid id ? id : Guid.Empty;
    }

    private static async Task InsertAccountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid accountId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, row_version, created_at)
            VALUES
                (@id, @display_name, true, @issuer, @subject, 1, @created_at);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", accountId);
        command.Parameters.AddWithValue("display_name", DevelopmentTestAdmin.DisplayName);
        command.Parameters.AddWithValue("issuer", DevelopmentTestAdmin.IdentityIssuer);
        command.Parameters.AddWithValue("subject", DevelopmentTestAdmin.IdentitySubject);
        command.Parameters.AddWithValue("created_at", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureAccountActiveAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE system.accounts
            SET display_name = @display_name,
                active = true,
                row_version = CASE
                    WHEN display_name IS DISTINCT FROM @display_name OR active = false THEN row_version + 1
                    ELSE row_version
                END
            WHERE id = @id;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", accountId);
        command.Parameters.AddWithValue("display_name", DevelopmentTestAdmin.DisplayName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureCapabilitiesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid accountId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var capabilities = SecurityCapabilities.All.ToArray();

        await using (var deactivateUnknown = new NpgsqlCommand(
            """
            UPDATE system.account_capability_grants
            SET active = false,
                row_version = row_version + 1
            WHERE account_id = @account_id
              AND active = true
              AND NOT (capability_name = ANY(@capabilities));
            """,
            connection,
            transaction))
        {
            deactivateUnknown.Parameters.AddWithValue("account_id", accountId);
            deactivateUnknown.Parameters.AddWithValue("capabilities", capabilities);
            await deactivateUnknown.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var capability in capabilities)
        {
            await using var grant = new NpgsqlCommand(
                """
                INSERT INTO system.account_capability_grants
                    (id, account_id, capability_name, active, row_version, created_at, created_by_account_id)
                VALUES
                    (@id, @account_id, @capability_name, true, 1, @created_at, @created_by_account_id)
                ON CONFLICT (account_id, capability_name) DO UPDATE
                SET active = true,
                    row_version = CASE
                        WHEN system.account_capability_grants.active = false
                            THEN system.account_capability_grants.row_version + 1
                        ELSE system.account_capability_grants.row_version
                    END;
                """,
                connection,
                transaction);
            grant.Parameters.AddWithValue("id", Guid.CreateVersion7());
            grant.Parameters.AddWithValue("account_id", accountId);
            grant.Parameters.AddWithValue("capability_name", capability);
            grant.Parameters.AddWithValue("created_at", now);
            grant.Parameters.AddWithValue("created_by_account_id", accountId);
            await grant.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
