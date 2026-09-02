using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using YowThi.Erp.Application.Common.Transactions;
using YowThi.Erp.Infrastructure.Persistence;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

namespace YowThi.Erp.IntegrationTests;

public sealed class PostgreSqlTransactionRollbackAcceptanceTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Late_database_failure_rolls_back_all_prior_cross_module_writes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var accountId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var batchId = Guid.CreateVersion7();
        var warehouseId = Guid.CreateVersion7();
        var storageLocationId = Guid.CreateVersion7();
        var positionId = Guid.CreateVersion7();
        var validOutboxId = Guid.CreateVersion7();
        var invalidOutboxId = Guid.CreateVersion7();

        try
        {
            var services = new ServiceCollection();
            services.AddErpPersistence(GetConnectionString());
            using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();

            var runner = scope.ServiceProvider.GetRequiredService<ICommandTransactionRunner>();
            var dbContext = scope.ServiceProvider.GetRequiredService<ErpDbContext>();

            var exception = await Assert.ThrowsAsync<PostgresException>(async () =>
                await runner.ExecuteAsync(
                    async operationCancellationToken =>
                    {
                        await dbContext.Database.ExecuteSqlInterpolatedAsync(
                            $"""
                            INSERT INTO system.accounts
                                (id, display_name, active, identity_issuer, identity_subject, created_at)
                            VALUES
                                ({accountId}, 'P5 DB4 account', true, NULL, NULL, {DateTimeOffset.UtcNow})
                            """,
                            operationCancellationToken);

                        await dbContext.Database.ExecuteSqlInterpolatedAsync(
                            $"""
                            INSERT INTO product.procurement_products
                                (id, name_zh_tw, name_th_th, unit_code, default_storage_location_id,
                                 active, created_at, created_by_account_id)
                            VALUES
                                ({productId}, 'P5 DB4 product', NULL, 'kg', NULL,
                                 true, {DateTimeOffset.UtcNow}, {accountId})
                            """,
                            operationCancellationToken);

                        await dbContext.Database.ExecuteSqlInterpolatedAsync(
                            $"""
                            INSERT INTO procurement.procurement_batches
                                (id, procurement_date, procurement_product_id, procurement_status,
                                 lifecycle_status, processing_route_id, processing_route_version_id,
                                 created_at, created_by_account_id)
                            VALUES
                                ({batchId}, {DateOnly.FromDateTime(DateTime.UtcNow)}, {productId}, 'OPEN',
                                 'ACTIVE', NULL, NULL,
                                 {DateTimeOffset.UtcNow}, {accountId})
                            """,
                            operationCancellationToken);

                        await dbContext.Database.ExecuteSqlInterpolatedAsync(
                            $"""
                            INSERT INTO infrastructure.warehouses
                                (id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
                            VALUES
                                ({warehouseId}, NULL, 'P5 DB4 warehouse', NULL, true, {DateTimeOffset.UtcNow}, {accountId})
                            """,
                            operationCancellationToken);

                        await dbContext.Database.ExecuteSqlInterpolatedAsync(
                            $"""
                            INSERT INTO infrastructure.storage_locations
                                (id, warehouse_id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
                            VALUES
                                ({storageLocationId}, {warehouseId}, NULL, 'P5 DB4 location', NULL, true,
                                 {DateTimeOffset.UtcNow}, {accountId})
                            """,
                            operationCancellationToken);

                        await dbContext.Database.ExecuteSqlInterpolatedAsync(
                            $"""
                            INSERT INTO inventory.inventory_positions
                                (id, origin, procurement_batch_id, outsourced_supply_batch_id,
                                 inventory_object_kind, procurement_product_id, process_material_id,
                                 sales_product_id, storage_location_id, raw_source_kind, supplier_id,
                                 balance_quantity)
                            VALUES
                                ({positionId}, 'IN_HOUSE', {batchId}, NULL,
                                 'PROCUREMENT_PRODUCT', {productId}, NULL,
                                 NULL, {storageLocationId}, NULL, NULL,
                                 1)
                            """,
                            operationCancellationToken);

                        await dbContext.Database.ExecuteSqlInterpolatedAsync(
                            $"""
                            INSERT INTO system.outbox_messages
                                (id, message_type, message_version, payload, command_id,
                                 occurred_at, available_at, published_at, delivery_attempt_count,
                                 next_attempt_at, locked_until, lock_token, last_error_summary)
                            VALUES
                                ({validOutboxId}, 'P5.DB4.Valid', 1, jsonb_build_object(), NULL,
                                 {DateTimeOffset.UtcNow}, {DateTimeOffset.UtcNow}, NULL, 0,
                                 NULL, NULL, NULL, NULL)
                            """,
                            operationCancellationToken);

                        await dbContext.Database.ExecuteSqlInterpolatedAsync(
                            $"""
                            INSERT INTO system.outbox_messages
                                (id, message_type, message_version, payload, command_id,
                                 occurred_at, available_at, published_at, delivery_attempt_count,
                                 next_attempt_at, locked_until, lock_token, last_error_summary)
                            VALUES
                                ({invalidOutboxId}, 'P5.DB4.Invalid', 0, jsonb_build_object(), NULL,
                                 {DateTimeOffset.UtcNow}, {DateTimeOffset.UtcNow}, NULL, 0,
                                 NULL, NULL, NULL, NULL)
                            """,
                            operationCancellationToken);

                        return CommandTransactionDecision<int>.Commit(1);
                    },
                    cancellationToken).AsTask());

            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
            Assert.Equal("ck_outbox_messages_message_version", exception.ConstraintName);

            await using var verificationConnection = await OpenConnectionAsync(cancellationToken);
            await using var verificationCommand = new NpgsqlCommand(
                """
                SELECT
                    (SELECT count(*) FROM system.accounts WHERE id = @account_id),
                    (SELECT count(*) FROM product.procurement_products WHERE id = @product_id),
                    (SELECT count(*) FROM procurement.procurement_batches WHERE id = @batch_id),
                    (SELECT count(*) FROM infrastructure.warehouses WHERE id = @warehouse_id),
                    (SELECT count(*) FROM infrastructure.storage_locations WHERE id = @storage_location_id),
                    (SELECT count(*) FROM inventory.inventory_positions WHERE id = @position_id),
                    (SELECT count(*) FROM system.outbox_messages WHERE id = @valid_outbox_id),
                    (SELECT count(*) FROM system.outbox_messages WHERE id = @invalid_outbox_id)
                """,
                verificationConnection);
            verificationCommand.Parameters.AddWithValue("account_id", accountId);
            verificationCommand.Parameters.AddWithValue("product_id", productId);
            verificationCommand.Parameters.AddWithValue("batch_id", batchId);
            verificationCommand.Parameters.AddWithValue("warehouse_id", warehouseId);
            verificationCommand.Parameters.AddWithValue("storage_location_id", storageLocationId);
            verificationCommand.Parameters.AddWithValue("position_id", positionId);
            verificationCommand.Parameters.AddWithValue("valid_outbox_id", validOutboxId);
            verificationCommand.Parameters.AddWithValue("invalid_outbox_id", invalidOutboxId);

            await using var reader = await verificationCommand.ExecuteReaderAsync(cancellationToken);
            Assert.True(await reader.ReadAsync(cancellationToken));
            for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
            {
                Assert.Equal(0L, reader.GetInt64(ordinal));
            }
        }
        finally
        {
            await using var cleanupConnection = await OpenConnectionAsync(cancellationToken);
            await using var cleanupCommand = new NpgsqlCommand(
                """
                DELETE FROM system.outbox_messages WHERE id = @valid_outbox_id OR id = @invalid_outbox_id;
                DELETE FROM inventory.inventory_positions WHERE id = @position_id;
                DELETE FROM infrastructure.storage_locations WHERE id = @storage_location_id;
                DELETE FROM infrastructure.warehouses WHERE id = @warehouse_id;
                DELETE FROM procurement.procurement_batches WHERE id = @batch_id;
                DELETE FROM product.procurement_products WHERE id = @product_id;
                DELETE FROM system.accounts WHERE id = @account_id;
                """,
                cleanupConnection);
            cleanupCommand.Parameters.AddWithValue("account_id", accountId);
            cleanupCommand.Parameters.AddWithValue("product_id", productId);
            cleanupCommand.Parameters.AddWithValue("batch_id", batchId);
            cleanupCommand.Parameters.AddWithValue("warehouse_id", warehouseId);
            cleanupCommand.Parameters.AddWithValue("storage_location_id", storageLocationId);
            cleanupCommand.Parameters.AddWithValue("position_id", positionId);
            cleanupCommand.Parameters.AddWithValue("valid_outbox_id", validOutboxId);
            cleanupCommand.Parameters.AddWithValue("invalid_outbox_id", invalidOutboxId);
            await cleanupCommand.ExecuteNonQueryAsync(cancellationToken);
        }
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
}
