using Npgsql;

namespace YowThi.Erp.IntegrationTests;

public sealed class PostgreSqlStructuralRejectionTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

    [Fact]
    public async Task Account_rejects_incomplete_external_identity_pair()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@id, 'P5 DB2 identity pair', true, 'https://issuer.example.test', NULL, @created_at)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);

        var exception = await Assert.ThrowsAsync<PostgresException>(
            async () => await command.ExecuteNonQueryAsync(cancellationToken));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_accounts_identity_pair", exception.ConstraintName);
        await transaction.RollbackAsync(cancellationToken);
    }

    [Fact]
    public async Task Sales_product_rejects_unit_based_pricing_with_sales_weight()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var accountId = Guid.CreateVersion7();
        var groupId = Guid.CreateVersion7();
        await InsertAccountAsync(connection, transaction, accountId, cancellationToken);

        await using (var groupCommand = new NpgsqlCommand(
                         """
                         INSERT INTO product.sales_product_groups
                             (id, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
                         VALUES
                             (@id, 'P5 DB2 group', NULL, true, @created_at, @account_id)
                         """,
                         connection,
                         transaction))
        {
            groupCommand.Parameters.AddWithValue("id", groupId);
            groupCommand.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);
            groupCommand.Parameters.AddWithValue("account_id", accountId);
            await groupCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var invalidCommand = new NpgsqlCommand(
            """
            INSERT INTO product.sales_products
                (id, sales_product_group_id, name_zh_tw, name_th_th, pricing_basis,
                 packaging_weight, sales_weight, default_storage_location_id, active,
                 created_at, created_by_account_id)
            VALUES
                (@id, @group_id, 'P5 DB2 product', NULL, 'UNIT_BASED',
                 NULL, 1, NULL, true, @created_at, @account_id)
            """,
            connection,
            transaction);
        invalidCommand.Parameters.AddWithValue("id", Guid.CreateVersion7());
        invalidCommand.Parameters.AddWithValue("group_id", groupId);
        invalidCommand.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);
        invalidCommand.Parameters.AddWithValue("account_id", accountId);

        var exception = await Assert.ThrowsAsync<PostgresException>(
            async () => await invalidCommand.ExecuteNonQueryAsync(cancellationToken));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_sales_products_pricing_shape", exception.ConstraintName);
        await transaction.RollbackAsync(cancellationToken);
    }

    [Fact]
    public async Task Procurement_entry_rejects_supplier_type_without_supplier_fk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var accountId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var batchId = Guid.CreateVersion7();
        await InsertAccountAsync(connection, transaction, accountId, cancellationToken);
        await InsertProcurementProductAsync(connection, transaction, productId, accountId, cancellationToken);
        await InsertProcurementBatchAsync(connection, transaction, batchId, productId, accountId, cancellationToken);

        await using var invalidCommand = new NpgsqlCommand(
            """
            INSERT INTO procurement.procurement_entries
                (id, procurement_batch_id, source_type, supplier_id, farmer_id,
                 net_quantity, unit_code_snapshot, unit_price, amount_thb,
                 company_pickup, recorded_at, recorded_by_account_id)
            VALUES
                (@id, @batch_id, 'SUPPLIER', NULL, NULL,
                 1, 'kg', 10, 10,
                 false, @recorded_at, @account_id)
            """,
            connection,
            transaction);
        invalidCommand.Parameters.AddWithValue("id", Guid.CreateVersion7());
        invalidCommand.Parameters.AddWithValue("batch_id", batchId);
        invalidCommand.Parameters.AddWithValue("recorded_at", DateTimeOffset.UtcNow);
        invalidCommand.Parameters.AddWithValue("account_id", accountId);

        var exception = await Assert.ThrowsAsync<PostgresException>(
            async () => await invalidCommand.ExecuteNonQueryAsync(cancellationToken));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_procurement_entries_typed_source", exception.ConstraintName);
        await transaction.RollbackAsync(cancellationToken);
    }

    [Fact]
    public async Task Inventory_position_rejects_duplicate_identity_when_nullable_dimensions_are_null()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var accountId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var batchId = Guid.CreateVersion7();
        var warehouseId = Guid.CreateVersion7();
        var storageLocationId = Guid.CreateVersion7();

        await InsertAccountAsync(connection, transaction, accountId, cancellationToken);
        await InsertProcurementProductAsync(connection, transaction, productId, accountId, cancellationToken);
        await InsertProcurementBatchAsync(connection, transaction, batchId, productId, accountId, cancellationToken);
        await InsertStorageLocationAsync(
            connection,
            transaction,
            warehouseId,
            storageLocationId,
            accountId,
            cancellationToken);

        await InsertInventoryPositionAsync(
            connection,
            transaction,
            Guid.CreateVersion7(),
            batchId,
            productId,
            storageLocationId,
            cancellationToken);

        var exception = await Assert.ThrowsAsync<PostgresException>(
            async () => await InsertInventoryPositionAsync(
                connection,
                transaction,
                Guid.CreateVersion7(),
                batchId,
                productId,
                storageLocationId,
                cancellationToken));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal("ux_inventory_positions_full_identity", exception.ConstraintName);
        await transaction.RollbackAsync(cancellationToken);
    }

    private static async Task InsertAccountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO system.accounts
                (id, display_name, active, identity_issuer, identity_subject, created_at)
            VALUES
                (@id, 'P5 DB2 account', true, NULL, NULL, @created_at)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", accountId);
        command.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertProcurementProductAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid productId,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO product.procurement_products
                (id, name_zh_tw, name_th_th, unit_code, default_storage_location_id,
                 active, created_at, created_by_account_id)
            VALUES
                (@id, 'P5 DB2 procurement product', NULL, 'kg', NULL,
                 true, @created_at, @account_id)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", productId);
        command.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("account_id", accountId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertProcurementBatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid batchId,
        Guid productId,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO procurement.procurement_batches
                (id, procurement_date, procurement_product_id, procurement_status,
                 lifecycle_status, processing_route_id, processing_route_version_id,
                 created_at, created_by_account_id)
            VALUES
                (@id, @procurement_date, @product_id, 'OPEN',
                 'ACTIVE', NULL, NULL,
                 @created_at, @account_id)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", batchId);
        command.Parameters.AddWithValue("procurement_date", DateOnly.FromDateTime(DateTime.UtcNow));
        command.Parameters.AddWithValue("product_id", productId);
        command.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("account_id", accountId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertStorageLocationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid warehouseId,
        Guid storageLocationId,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        await using (var warehouseCommand = new NpgsqlCommand(
                         """
                         INSERT INTO infrastructure.warehouses
                             (id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
                         VALUES
                             (@id, NULL, 'P5 DB2 warehouse', NULL, true, @created_at, @account_id)
                         """,
                         connection,
                         transaction))
        {
            warehouseCommand.Parameters.AddWithValue("id", warehouseId);
            warehouseCommand.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);
            warehouseCommand.Parameters.AddWithValue("account_id", accountId);
            await warehouseCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var locationCommand = new NpgsqlCommand(
            """
            INSERT INTO infrastructure.storage_locations
                (id, warehouse_id, code, name_zh_tw, name_th_th, active, created_at, created_by_account_id)
            VALUES
                (@id, @warehouse_id, NULL, 'P5 DB2 location', NULL, true, @created_at, @account_id)
            """,
            connection,
            transaction);
        locationCommand.Parameters.AddWithValue("id", storageLocationId);
        locationCommand.Parameters.AddWithValue("warehouse_id", warehouseId);
        locationCommand.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);
        locationCommand.Parameters.AddWithValue("account_id", accountId);
        await locationCommand.ExecuteNonQueryAsync(cancellationToken);
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

    private static async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = LocalDevelopmentConnectionString;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
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
