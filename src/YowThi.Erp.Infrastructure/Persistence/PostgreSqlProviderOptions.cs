using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace YowThi.Erp.Infrastructure.Persistence;

public static class PostgreSqlProviderOptions
{
    public const int MajorVersion = 18;
    public const int MinorVersion = 0;
    public const string MigrationsHistoryTableName = "__ef_migrations_history";
    public const string MigrationsHistorySchema = "system";

    public static void Configure(NpgsqlDbContextOptionsBuilder options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.SetPostgresVersion(MajorVersion, MinorVersion);
        options.MigrationsHistoryTable(MigrationsHistoryTableName, MigrationsHistorySchema);
    }
}
