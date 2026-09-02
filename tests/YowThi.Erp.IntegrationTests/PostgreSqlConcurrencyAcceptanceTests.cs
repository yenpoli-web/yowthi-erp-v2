using Microsoft.EntityFrameworkCore;
using Npgsql;
using YowThi.Erp.Domain.Party;
using YowThi.Erp.Infrastructure.Persistence;
using YowThi.Erp.Infrastructure.Persistence.Concurrency;

namespace YowThi.Erp.IntegrationTests;

public sealed class PostgreSqlConcurrencyAcceptanceTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Ef_rejects_stale_explicit_row_version_writer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var accountId = Guid.CreateVersion7();
        var supplierId = Guid.CreateVersion7();

        await using (var setupConnection = await OpenConnectionAsync(cancellationToken))
        {
            await using var setupCommand = new NpgsqlCommand(
                """
                INSERT INTO system.accounts
                    (id, display_name, active, identity_issuer, identity_subject, created_at)
                VALUES
                    (@account_id, 'P5 DB3 account', true, NULL, NULL, @created_at);

                INSERT INTO party.suppliers
                    (id, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
                VALUES
                    (@supplier_id, 'P5 DB3 supplier', NULL, true, @created_at, @account_id);
                """,
                setupConnection);
            setupCommand.Parameters.AddWithValue("account_id", accountId);
            setupCommand.Parameters.AddWithValue("supplier_id", supplierId);
            setupCommand.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);
            await setupCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            var options = new DbContextOptionsBuilder<ErpDbContext>()
                .UseNpgsql(GetConnectionString(), PostgreSqlProviderOptions.Configure)
                .AddInterceptors(new RowVersionSaveChangesInterceptor())
                .Options;

            await using var firstContext = new ErpDbContext(options);
            await using var secondContext = new ErpDbContext(options);

            var firstSupplier = await firstContext.Set<Supplier>()
                .SingleAsync(supplier => supplier.Id == supplierId, cancellationToken);
            var secondSupplier = await secondContext.Set<Supplier>()
                .SingleAsync(supplier => supplier.Id == supplierId, cancellationToken);

            Assert.Equal(1, firstSupplier.RowVersion);
            Assert.Equal(1, secondSupplier.RowVersion);

            firstContext.Entry(firstSupplier).Property(supplier => supplier.NameZhTw).CurrentValue = "P5 DB3 writer one";
            secondContext.Entry(secondSupplier).Property(supplier => supplier.NameZhTw).CurrentValue = "P5 DB3 writer two";

            await firstContext.SaveChangesAsync(cancellationToken);
            Assert.Equal(2, firstSupplier.RowVersion);

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                async () => await secondContext.SaveChangesAsync(cancellationToken));
        }
        finally
        {
            await using var cleanupConnection = await OpenConnectionAsync(cancellationToken);
            await using var cleanupCommand = new NpgsqlCommand(
                """
                DELETE FROM party.suppliers WHERE id = @supplier_id;
                DELETE FROM system.accounts WHERE id = @account_id;
                """,
                cleanupConnection);
            cleanupCommand.Parameters.AddWithValue("supplier_id", supplierId);
            cleanupCommand.Parameters.AddWithValue("account_id", accountId);
            await cleanupCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Inventory_position_race_allows_only_one_logical_identity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var accountId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var batchId = Guid.CreateVersion7();
        var warehouseId = Guid.CreateVersion7();
        var storageLocationId = Guid.CreateVersion7();
        var firstPositionId = Guid.CreateVersion7();
        var secondPositionId = Guid.CreateVersion7();

        await SetupInventoryIdentityAsync(
            accountId,
            productId,
            batchId,
            warehouseId,
            storageLocationId,
            cancellationToken);

        try
        {
            await using var firstConnection = await OpenConnectionAsync(cancellationToken);
            await using var secondConnection = await OpenConnectionAsync(cancellationToken);
            await using var firstTransaction = await firstConnection.BeginTransactionAsync(cancellationToken);
            await using var secondTransaction = await secondConnection.BeginTransactionAsync(cancellationToken);

            await InsertInventoryPositionAsync(
                firstConnection,
                firstTransaction,
                firstPositionId,
                batchId,
                productId,
                storageLocationId,
                cancellationToken);

            var secondInsertTask = InsertInventoryPositionAsync(
                secondConnection,
                secondTransaction,
                secondPositionId,
                batchId,
                productId,
                storageLocationId,
                cancellationToken);

            await Task.Delay(50, cancellationToken);
            await firstTransaction.CommitAsync(cancellationToken);

            var exception = await Assert.ThrowsAsync<PostgresException>(async () => await secondInsertTask);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
            Assert.Equal("ux_inventory_positions_full_identity", exception.ConstraintName);
            await secondTransaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            await CleanupInventoryIdentityAsync(
                accountId,
                productId,
                batchId,
                warehouseId,
                storageLocationId,
                cancellationToken);
        }
    }

    [Fact]
    public async Task Finance_outstanding_compare_and_set_allows_only_one_stale_version_writer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var payableId = Guid.CreateVersion7();

        await using (var setupConnection = await OpenConnectionAsync(cancellationToken))
        {
            await using var setupCommand = new NpgsqlCommand(
                """
                INSERT INTO finance.payables
                    (id, payable_kind, procurement_batch_id, supplier_id, farmer_id,
                     outsourced_supply_detail_id, employee_daily_wage_id, created_at)
                VALUES
                    (@payable_id, 'COMPANY_PICKUP_TRANSPORT', NULL, NULL, NULL,
                     NULL, NULL, @created_at);

                INSERT INTO finance.payable_outstanding_positions
                    (payable_id, original_obligation_thb, adjustment_total_thb,
                     settlement_total_thb, outstanding_thb, updated_at)
                VALUES
                    (@payable_id, 100, 0, 0, 100, @created_at);
                """,
                setupConnection);
            setupCommand.Parameters.AddWithValue("payable_id", payableId);
            setupCommand.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);
            await setupCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            await using var firstConnection = await OpenConnectionAsync(cancellationToken);
            await using var secondConnection = await OpenConnectionAsync(cancellationToken);
            await using var firstTransaction = await firstConnection.BeginTransactionAsync(cancellationToken);
            await using var secondTransaction = await secondConnection.BeginTransactionAsync(cancellationToken);

            var firstCount = await ExecuteOutstandingCasAsync(
                firstConnection,
                firstTransaction,
                payableId,
                cancellationToken);
            Assert.Equal(1, firstCount);

            var secondUpdateTask = ExecuteOutstandingCasAsync(
                secondConnection,
                secondTransaction,
                payableId,
                cancellationToken);

            await Task.Delay(50, cancellationToken);
            await firstTransaction.CommitAsync(cancellationToken);

            var secondCount = await secondUpdateTask;
            Assert.Equal(0, secondCount);
            await secondTransaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            await using var cleanupConnection = await OpenConnectionAsync(cancellationToken);
            await using var cleanupCommand = new NpgsqlCommand(
                """
                DELETE FROM finance.payable_outstanding_positions WHERE payable_id = @payable_id;
                DELETE FROM finance.payables WHERE id = @payable_id;
                """,
                cleanupConnection);
            cleanupCommand.Parameters.AddWithValue("payable_id", payableId);
            await cleanupCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Simultaneous_same_CommandId_acquisition_has_one_winner()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var accountId = Guid.CreateVersion7();
        var commandId = Guid.CreateVersion7();

        await using (var setupConnection = await OpenConnectionAsync(cancellationToken))
        {
            await using var setupCommand = new NpgsqlCommand(
                """
                INSERT INTO system.accounts
                    (id, display_name, active, identity_issuer, identity_subject, created_at)
                VALUES
                    (@account_id, 'P5 DB3 command account', true, NULL, NULL, @created_at)
                """,
                setupConnection);
            setupCommand.Parameters.AddWithValue("account_id", accountId);
            setupCommand.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);
            await setupCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            await using var firstConnection = await OpenConnectionAsync(cancellationToken);
            await using var secondConnection = await OpenConnectionAsync(cancellationToken);
            await using var firstTransaction = await firstConnection.BeginTransactionAsync(cancellationToken);
            await using var secondTransaction = await secondConnection.BeginTransactionAsync(cancellationToken);

            await InsertCommandExecutionAsync(
                firstConnection,
                firstTransaction,
                commandId,
                accountId,
                cancellationToken);

            var secondInsertTask = InsertCommandExecutionAsync(
                secondConnection,
                secondTransaction,
                commandId,
                accountId,
                cancellationToken);

            await Task.Delay(50, cancellationToken);
            await firstTransaction.CommitAsync(cancellationToken);

            var exception = await Assert.ThrowsAsync<PostgresException>(async () => await secondInsertTask);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
            Assert.Equal("pk_command_executions", exception.ConstraintName);
            await secondTransaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            await using var cleanupConnection = await OpenConnectionAsync(cancellationToken);
            await using var cleanupCommand = new NpgsqlCommand(
                """
                DELETE FROM system.command_executions WHERE command_id = @command_id;
                DELETE FROM system.accounts WHERE id = @account_id;
                """,
                cleanupConnection);
            cleanupCommand.Parameters.AddWithValue("command_id", commandId);
            cleanupCommand.Parameters.AddWithValue("account_id", accountId);
            await cleanupCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Outbox_workers_skip_locked_rows_and_claim_distinct_leases()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstMessageId = Guid.CreateVersion7();
        var secondMessageId = Guid.CreateVersion7();

        await using (var setupConnection = await OpenConnectionAsync(cancellationToken))
        {
            await using var setupCommand = new NpgsqlCommand(
                """
                INSERT INTO system.outbox_messages
                    (id, message_type, message_version, payload, command_id,
                     occurred_at, available_at, published_at, delivery_attempt_count,
                     next_attempt_at, locked_until, lock_token, last_error_summary)
                VALUES
                    (@first_id, 'P5.DB3', 1, '{}'::jsonb, NULL,
                     @occurred_at, @available_at, NULL, 0,
                     NULL, NULL, NULL, NULL),
                    (@second_id, 'P5.DB3', 1, '{}'::jsonb, NULL,
                     @occurred_at, @available_at, NULL, 0,
                     NULL, NULL, NULL, NULL)
                """,
                setupConnection);
            setupCommand.Parameters.AddWithValue("first_id", firstMessageId);
            setupCommand.Parameters.AddWithValue("second_id", secondMessageId);
            setupCommand.Parameters.AddWithValue("occurred_at", DateTimeOffset.UtcNow);
            setupCommand.Parameters.AddWithValue("available_at", DateTimeOffset.UtcNow.AddMinutes(-1));
            await setupCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            await using var firstConnection = await OpenConnectionAsync(cancellationToken);
            await using var secondConnection = await OpenConnectionAsync(cancellationToken);
            await using var firstTransaction = await firstConnection.BeginTransactionAsync(cancellationToken);
            await using var secondTransaction = await secondConnection.BeginTransactionAsync(cancellationToken);

            var firstClaim = await ClaimOutboxMessageAsync(
                firstConnection,
                firstTransaction,
                firstMessageId,
                secondMessageId,
                cancellationToken);
            var secondClaim = await ClaimOutboxMessageAsync(
                secondConnection,
                secondTransaction,
                firstMessageId,
                secondMessageId,
                cancellationToken);

            Assert.NotEqual(firstClaim, secondClaim);
            Assert.Contains(firstClaim, new[] { firstMessageId, secondMessageId });
            Assert.Contains(secondClaim, new[] { firstMessageId, secondMessageId });

            await firstTransaction.RollbackAsync(cancellationToken);
            await secondTransaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            await using var cleanupConnection = await OpenConnectionAsync(cancellationToken);
            await using var cleanupCommand = new NpgsqlCommand(
                "DELETE FROM system.outbox_messages WHERE id = @first_id OR id = @second_id",
                cleanupConnection);
            cleanupCommand.Parameters.AddWithValue("first_id", firstMessageId);
            cleanupCommand.Parameters.AddWithValue("second_id", secondMessageId);
            await cleanupCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task SetupInventoryIdentityAsync(
        Guid accountId,
        Guid productId,
        Guid batchId,
        Guid warehouseId,
        Guid storageLocationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@account_id, 'P5 DB3 inventory account', true, NULL, NULL, @created_at);

            INSERT INTO product.procurement_products
                (id, name_zh_tw, name_th_th, unit_code, default_storage_location_id,
                 active, created_at, created_by_account_id)
            VALUES
                (@product_id, 'P5 DB3 inventory product', NULL, 'kg', NULL,
                 true, @created_at, @account_id);

            INSERT INTO procurement.procurement_batches
                (id, procurement_date, procurement_product_id, procurement_status,
                 lifecycle_status, processing_route_id, processing_route_version_id,
                 created_at, created_by_account_id)
            VALUES
                (@batch_id, @procurement_date, @product_id, 'OPEN',
                 'ACTIVE', NULL, NULL,
                 @created_at, @account_id);

            INSERT INTO infrastructure.warehouses
                (id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@warehouse_id, NULL, 'P5 DB3 warehouse', NULL, true, @created_at, @account_id);

            INSERT INTO infrastructure.storage_locations
                (id, warehouse_id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@storage_location_id, @warehouse_id, NULL, 'P5 DB3 location', NULL, true, @created_at, @account_id);
            """,
            connection);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("product_id", productId);
        command.Parameters.AddWithValue("batch_id", batchId);
        command.Parameters.AddWithValue("warehouse_id", warehouseId);
        command.Parameters.AddWithValue("storage_location_id", storageLocationId);
        command.Parameters.AddWithValue("procurement_date", DateOnly.FromDateTime(DateTime.UtcNow));
        command.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CleanupInventoryIdentityAsync(
        Guid accountId,
        Guid productId,
        Guid batchId,
        Guid warehouseId,
        Guid storageLocationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            DELETE FROM inventory.inventory_positions WHERE procurement_batch_id = @batch_id;
            DELETE FROM infrastructure.storage_locations WHERE id = @storage_location_id;
            DELETE FROM infrastructure.warehouses WHERE id = @warehouse_id;
            DELETE FROM procurement.procurement_batches WHERE id = @batch_id;
            DELETE FROM product.procurement_products WHERE id = @product_id;
            DELETE FROM system.accounts WHERE id = @account_id;
            """,
            connection);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("product_id", productId);
        command.Parameters.AddWithValue("batch_id", batchId);
        command.Parameters.AddWithValue("warehouse_id", warehouseId);
        command.Parameters.AddWithValue("storage_location_id", storageLocationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertInventoryPositionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid positionId,
        Guid batchId,
        Guid productId,
        Guid storageLocationId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO inventory.inventory_positions
                (id, origin, procurement_batch_id, outsourced_supply_batch_id,
                 inventory_object_kind, procurement_product_id, process_material_id,
                 sales_product_id, storage_location_id, raw_source_kind, supplier_id,
                 balance_quantity)
            VALUES
                (@id, 'IN_HOUSE', @batch_id, NULL,
                 'PROCUREMENT_PRODUCT', @product_id, NULL,
                 NULL, @storage_location_id, NULL, NULL,
                 1)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", positionId);
        command.Parameters.AddWithValue("batch_id", batchId);
        command.Parameters.AddWithValue("product_id", productId);
        command.Parameters.AddWithValue("storage_location_id", storageLocationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> ExecuteOutstandingCasAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid payableId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE finance.payable_outstanding_positions
            SET settlement_total_thb = 10,
                outstanding_thb = 90,
                row_version = row_version + 1,
                updated_at = @updated_at
            WHERE payable_id = @payable_id
              AND row_version = 1
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("payable_id", payableId);
        command.Parameters.AddWithValue("updated_at", DateTimeOffset.UtcNow);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertCommandExecutionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid commandId,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO system.command_executions
                (command_id, command_type, request_hash, status, result_payload,
                 actor_account_id, started_at, executed_at)
            VALUES
                (@command_id, 'P5.DB3.Command', @request_hash, 'IN_PROGRESS', NULL,
                 @account_id, @started_at, NULL)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("command_id", commandId);
        command.Parameters.AddWithValue("request_hash", new byte[32]);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("started_at", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Guid> ClaimOutboxMessageAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid firstMessageId,
        Guid secondMessageId,
        CancellationToken cancellationToken)
    {
        Guid claimedId;
        await using (var selectCommand = new NpgsqlCommand(
                         """
                         SELECT id
                         FROM system.outbox_messages
                         WHERE (id = @first_id OR id = @second_id)
                           AND published_at IS NULL
                           AND available_at <= now()
                           AND (locked_until IS NULL OR locked_until < now())
                         ORDER BY id
                         FOR UPDATE SKIP LOCKED
                         LIMIT 1
                         """,
                         connection,
                         transaction))
        {
            selectCommand.Parameters.AddWithValue("first_id", firstMessageId);
            selectCommand.Parameters.AddWithValue("second_id", secondMessageId);
            var scalar = await selectCommand.ExecuteScalarAsync(cancellationToken);
            claimedId = Assert.IsType<Guid>(scalar);
        }

        await using var leaseCommand = new NpgsqlCommand(
            """
            UPDATE system.outbox_messages
            SET lock_token = @lock_token,
                locked_until = now() + interval '1 minute'
            WHERE id = @id
            """,
            connection,
            transaction);
        leaseCommand.Parameters.AddWithValue("lock_token", Guid.CreateVersion7());
        leaseCommand.Parameters.AddWithValue("id", claimedId);
        Assert.Equal(1, await leaseCommand.ExecuteNonQueryAsync(cancellationToken));

        return claimedId;
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
