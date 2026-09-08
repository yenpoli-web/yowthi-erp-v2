using Npgsql;

namespace YowThi.Erp.IntegrationTests;

public sealed class PostgreSqlSchemaAcceptanceTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";
    private static readonly string[] ExpectedMigrationIds =
    [
        "20260828033151_InitialV01",
        "20260908002500_P8SecurityFoundation",
        "20260908140602_P8PartyBusinessCodes",
    ];

    private static readonly string[] ExpectedSchemas =
    [
        "audit",
        "finance",
        "infrastructure",
        "inventory",
        "labor",
        "outsourced",
        "party",
        "processing",
        "processing_config",
        "procurement",
        "product",
        "sales",
        "sales_handling",
        "system",
    ];

    private static readonly string[] ExpectedRelations =
    [
        "audit.audit_event_subjects",
        "audit.audit_events",
        "audit.correction_links",
        "finance.company_pickup_transport_bases",
        "finance.company_pickup_transport_obligation_basis_items",
        "finance.payable_adjustments",
        "finance.payable_obligation_items",
        "finance.payable_outstanding_positions",
        "finance.payables",
        "finance.payments",
        "finance.receipts",
        "finance.receivable_obligation_items",
        "finance.receivable_outstanding_positions",
        "finance.receivables",
        "infrastructure.containers",
        "infrastructure.storage_locations",
        "infrastructure.warehouses",
        "inventory.inventory_movements",
        "inventory.inventory_operations",
        "inventory.inventory_positions",
        "labor.employee_daily_wages",
        "labor.processing_wage_component_sources",
        "labor.processing_wage_components",
        "labor.sales_packaging_wage_components",
        "outsourced.outsourced_supply_batches",
        "outsourced.outsourced_supply_details",
        "party.customers",
        "party.employees",
        "party.farmers",
        "party.outsourced_vendors",
        "party.suppliers",
        "processing.processing_execution_inputs",
        "processing.processing_execution_outputs",
        "processing.processing_executions",
        "processing_config.process_materials",
        "processing_config.processing_module_outputs",
        "processing_config.processing_modules",
        "processing_config.processing_route_versions",
        "processing_config.processing_routes",
        "processing_config.route_input_configs",
        "procurement.procurement_batches",
        "procurement.procurement_entries",
        "product.procurement_products",
        "product.sales_product_groups",
        "product.sales_products",
        "sales.sales",
        "sales.sales_allocation_revision_items",
        "sales.sales_allocation_revisions",
        "sales.sales_allocations",
        "sales.sales_details",
        "sales_handling.sales_packaging_items",
        "sales_handling.sales_packaging_work_records",
        "system.account_capability_grants",
        "system.accounts",
        "system.command_executions",
        "system.outbox_messages",
    ];

    [Fact]
    public async Task Database_identity_is_expected_postgresql_18_primary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT current_database(), current_user, current_setting('server_version_num')::integer, pg_is_in_recovery()",
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        Assert.True(await reader.ReadAsync(cancellationToken));
        Assert.Equal("yowthi_dev", reader.GetString(0));
        Assert.Equal("yowthi_dev", reader.GetString(1));
        Assert.Equal(18, reader.GetInt32(2) / 10_000);
        Assert.False(reader.GetBoolean(3));
        Assert.False(await reader.ReadAsync(cancellationToken));
    }

    [Fact]
    public async Task Database_contains_exactly_the_expected_erp_schemas_and_relations()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = await OpenConnectionAsync(cancellationToken);

        var schemas = new List<string>();
        await using (var schemaCommand = new NpgsqlCommand(
                         "SELECT nspname FROM pg_catalog.pg_namespace WHERE nspname = ANY (@schemas) ORDER BY nspname",
                         connection))
        {
            schemaCommand.Parameters.AddWithValue("schemas", ExpectedSchemas);
            await using var reader = await schemaCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                schemas.Add(reader.GetString(0));
            }
        }

        Assert.Equal(ExpectedSchemas, schemas);

        var relations = new List<string>();
        await using (var relationCommand = new NpgsqlCommand(
                         """
                         SELECT n.nspname || '.' || c.relname
                         FROM pg_catalog.pg_class AS c
                         INNER JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
                         WHERE n.nspname = ANY (@schemas)
                           AND c.relkind IN ('r', 'p')
                           AND NOT (n.nspname = 'system' AND c.relname = '__ef_migrations_history')
                         ORDER BY n.nspname, c.relname
                         """,
                         connection))
        {
            relationCommand.Parameters.AddWithValue("schemas", ExpectedSchemas);
            await using var reader = await relationCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                relations.Add(reader.GetString(0));
            }
        }

        Assert.Equal(ExpectedRelations, relations);
    }

    [Fact]
    public async Task Migration_history_contains_the_expected_forward_chain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT \"MigrationId\" FROM system.__ef_migrations_history ORDER BY \"MigrationId\"",
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var migrationIds = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            migrationIds.Add(reader.GetString(0));
        }

        Assert.Equal(ExpectedMigrationIds, migrationIds);
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
