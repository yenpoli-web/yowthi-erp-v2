using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using YowThi.Erp.Infrastructure.Persistence;

namespace YowThi.Erp.IntegrationTests;

public sealed class PostgreSqlCatalogObjectAcceptanceTests
{
    private const string ConnectionStringEnvironmentVariable = "YOWTHI_ERP_CONNECTION_STRING";
    private const string LocalDevelopmentConnectionString =
        "Host=127.0.0.1;Port=55432;Database=yowthi_dev;Username=yowthi_dev";

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

    [Fact]
    public async Task Database_contains_the_expected_named_constraints()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = GetConnectionString();
        var expected = GetExpectedCatalogObjects(connectionString).Constraints;

        await using var connection = await OpenConnectionAsync(connectionString, cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT n.nspname || '.' || t.relname || '.' || c.conname
            FROM pg_catalog.pg_constraint AS c
            INNER JOIN pg_catalog.pg_class AS t ON t.oid = c.conrelid
            INNER JOIN pg_catalog.pg_namespace AS n ON n.oid = t.relnamespace
            WHERE n.nspname = ANY (@schemas)
              AND c.contype IN ('p', 'u', 'f', 'c')
              AND NOT (n.nspname = 'system' AND t.relname = '__ef_migrations_history')
            ORDER BY 1
            """,
            connection);
        command.Parameters.AddWithValue("schemas", ExpectedSchemas);

        var actual = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            actual.Add(reader.GetString(0));
        }

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Database_contains_the_expected_named_explicit_indexes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = GetConnectionString();
        var expected = GetExpectedCatalogObjects(connectionString).Indexes;

        await using var connection = await OpenConnectionAsync(connectionString, cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT n.nspname || '.' || t.relname || '.' || i.relname
            FROM pg_catalog.pg_index AS x
            INNER JOIN pg_catalog.pg_class AS t ON t.oid = x.indrelid
            INNER JOIN pg_catalog.pg_class AS i ON i.oid = x.indexrelid
            INNER JOIN pg_catalog.pg_namespace AS n ON n.oid = t.relnamespace
            WHERE n.nspname = ANY (@schemas)
              AND (i.relname LIKE 'ix\_%' ESCAPE '\' OR i.relname LIKE 'ux\_%' ESCAPE '\')
            ORDER BY 1
            """,
            connection);
        command.Parameters.AddWithValue("schemas", ExpectedSchemas);

        var actual = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            actual.Add(reader.GetString(0));
        }

        Assert.Equal(expected, actual);
    }

    private static (string[] Constraints, string[] Indexes) GetExpectedCatalogObjects(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql(connectionString, PostgreSqlProviderOptions.Configure)
            .Options;

        using var context = new ErpDbContext(options);
        var relationalModel = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var expectedSchemaSet = ExpectedSchemas.ToHashSet(StringComparer.Ordinal);
        var tables = relationalModel.Tables
            .Where(table => table.Schema is not null && expectedSchemaSet.Contains(table.Schema))
            .ToArray();

        var constraints = tables
            .SelectMany(table =>
                table.UniqueConstraints.Select(constraint => $"{table.Schema}.{table.Name}.{constraint.Name}")
                    .Concat(table.ForeignKeyConstraints.Select(constraint => $"{table.Schema}.{table.Name}.{constraint.Name}"))
                    .Concat(table.CheckConstraints.Select(constraint => $"{table.Schema}.{table.Name}.{constraint.Name}")))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var indexes = tables
            .SelectMany(table => table.Indexes.Select(index => $"{table.Schema}.{table.Name}.{index.Name}"))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return (constraints, indexes);
    }

    private static string GetConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        return string.IsNullOrWhiteSpace(connectionString)
            ? LocalDevelopmentConnectionString
            : connectionString;
    }

    private static async Task<NpgsqlConnection> OpenConnectionAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
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
