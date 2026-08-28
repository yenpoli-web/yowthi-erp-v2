using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using YowThi.Erp.Infrastructure.Persistence;

namespace YowThi.Erp.ArchitectureTests;

public sealed class M1RelationalModelTests
{
    [Fact]
    public void M1_system_and_party_subset_remains_exactly_the_expected_eight_tables()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);

        var expected = new[]
        {
            "party.customers",
            "party.employees",
            "party.farmers",
            "party.outsourced_vendors",
            "party.suppliers",
            "system.accounts",
            "system.command_executions",
            "system.outbox_messages",
        };

        var actual = model.GetEntityTypes()
            .Where(entityType => entityType.GetSchema() is "party" or "system")
            .Select(entityType => $"{entityType.GetSchema()}.{entityType.GetTableName()}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Mutable_M1_rows_use_explicit_bigint_concurrency_tokens_only_where_approved()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);

        foreach (var table in new[] { "accounts", "suppliers", "farmers", "employees", "customers", "outsourced_vendors" })
        {
            var entityType = GetTable(model, table == "accounts" ? "system" : "party", table);
            var rowVersion = entityType.FindProperty("RowVersion");

            Assert.NotNull(rowVersion);
            Assert.True(rowVersion!.IsConcurrencyToken);
            Assert.Equal(typeof(long), rowVersion.ClrType);
            Assert.Equal(1L, rowVersion.GetDefaultValue());
        }

        Assert.Null(GetTable(model, "system", "command_executions").FindProperty("RowVersion"));
        Assert.Null(GetTable(model, "system", "outbox_messages").FindProperty("RowVersion"));
        Assert.DoesNotContain(
            model.GetEntityTypes().SelectMany(x => x.GetProperties()),
            property => string.Equals(property.Name, "xmin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Account_external_identity_mapping_is_optional_paired_nonblank_and_unique_when_present()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);
        var accounts = GetTable(model, "system", "accounts");
        var table = StoreObjectIdentifier.Table("accounts", "system");

        var issuer = accounts.FindProperty("IdentityIssuer");
        var subject = accounts.FindProperty("IdentitySubject");

        Assert.NotNull(issuer);
        Assert.NotNull(subject);
        Assert.True(issuer!.IsNullable);
        Assert.True(subject!.IsNullable);
        Assert.Equal("identity_issuer", issuer.GetColumnName(table));
        Assert.Equal("identity_subject", subject.GetColumnName(table));

        var checks = accounts.GetCheckConstraints().Select(check => check.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("ck_accounts_identity_pair", checks);
        Assert.Contains("ck_accounts_identity_issuer_nonblank", checks);
        Assert.Contains("ck_accounts_identity_subject_nonblank", checks);

        var identityIndex = Assert.Single(
            accounts.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Select(property => property.Name)
                    .SequenceEqual(new[] { "IdentityIssuer", "IdentitySubject" }, StringComparer.Ordinal));

        Assert.Equal("ux_accounts_identity_issuer_subject", identityIndex.GetDatabaseName());
        Assert.Equal("identity_issuer IS NOT NULL", identityIndex.GetFilter());
    }

    [Fact]
    public void Party_actor_references_are_real_restrict_foreign_keys_and_not_business_uniqueness()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);

        foreach (var table in new[] { "suppliers", "farmers", "employees", "customers", "outsourced_vendors" })
        {
            var entityType = GetTable(model, "party", table);
            var actorForeignKeys = entityType.GetForeignKeys()
                .Where(foreignKey => foreignKey.PrincipalEntityType.GetSchema() == "system" && foreignKey.PrincipalEntityType.GetTableName() == "accounts")
                .ToArray();

            Assert.Equal(2, actorForeignKeys.Length);
            Assert.All(actorForeignKeys, foreignKey => Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));
            Assert.DoesNotContain(entityType.GetIndexes(), index => index.IsUnique);
        }
    }

    [Fact]
    public void Party_name_and_lifecycle_checks_are_present()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);

        foreach (var table in new[] { "suppliers", "farmers", "employees", "customers", "outsourced_vendors" })
        {
            var entityType = GetTable(model, "party", table);
            var checks = entityType.GetCheckConstraints().Select(check => check.Name).ToHashSet(StringComparer.Ordinal);

            Assert.Contains($"ck_{table}_name_present", checks);
            Assert.Contains($"ck_{table}_row_version", checks);
            Assert.Contains($"ck_{table}_deleted_pair", checks);
        }
    }

    [Fact]
    public void CommandExecution_has_actor_fk_and_Outbox_command_id_is_correlation_only()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);

        var commandExecution = GetTable(model, "system", "command_executions");
        var commandForeignKey = Assert.Single(commandExecution.GetForeignKeys());
        Assert.Equal("accounts", commandForeignKey.PrincipalEntityType.GetTableName());
        Assert.Equal(DeleteBehavior.Restrict, commandForeignKey.DeleteBehavior);

        var commandChecks = commandExecution.GetCheckConstraints().Select(check => check.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("ck_command_executions_request_hash_length", commandChecks);
        Assert.Contains("ck_command_executions_status", commandChecks);
        Assert.Contains("ck_command_executions_execution_state", commandChecks);

        var outbox = GetTable(model, "system", "outbox_messages");
        Assert.Empty(outbox.GetForeignKeys());
        Assert.NotNull(outbox.FindProperty("CommandId"));
    }

    [Fact]
    public void No_M1_entity_uses_a_global_query_filter()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);

        Assert.All(model.GetEntityTypes(), entityType => Assert.Empty(entityType.GetDeclaredQueryFilters()));
    }

    private static ErpDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=yowthi_erp_v2;Username=postgres",
                PostgreSqlProviderOptions.Configure)
            .Options;

        return new ErpDbContext(options);
    }

    private static IModel GetDesignTimeModel(ErpDbContext context)
    {
        return context.GetService<IDesignTimeModel>().Model;
    }

    private static IEntityType GetTable(IModel model, string schema, string table)
    {
        return model.GetEntityTypes().Single(entityType =>
            entityType.GetSchema() == schema && entityType.GetTableName() == table);
    }
}
