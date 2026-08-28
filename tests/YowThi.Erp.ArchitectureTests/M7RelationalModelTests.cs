using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using YowThi.Erp.Application.Common.Audit;
using YowThi.Erp.Infrastructure.Persistence;

namespace YowThi.Erp.ArchitectureTests;

public sealed class M7RelationalModelTests
{
    [Fact]
    public void M7_maps_exactly_audit_relations()
    {
        using var context = CreateContext();
        var model = GetDesignTimeModel(context);

        var expected = new[]
        {
            "audit.audit_event_subjects",
            "audit.audit_events",
            "audit.correction_links",
        };

        var actual = model.GetEntityTypes()
            .Where(entityType => entityType.GetSchema() == "audit")
            .Select(entityType => $"{entityType.GetSchema()}.{entityType.GetTableName()}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Audit_event_is_append_oriented_with_command_correlation_only()
    {
        using var context = CreateContext();
        var auditEvent = GetTable(GetDesignTimeModel(context), "audit", "audit_events");

        Assert.Contains("ck_audit_events_event_kind", CheckNames(auditEvent));
        Assert.Equal(typeof(AuditEventKind), auditEvent.FindProperty("EventKind")!.ClrType);
        Assert.Equal("text", auditEvent.FindProperty("EventKind")!.GetColumnType());
        Assert.Null(auditEvent.FindProperty("RowVersion"));

        var actorForeignKey = Assert.Single(auditEvent.GetForeignKeys());
        Assert.Equal("system", actorForeignKey.PrincipalEntityType.GetSchema());
        Assert.Equal("accounts", actorForeignKey.PrincipalEntityType.GetTableName());
        Assert.Equal(new[] { "ActorAccountId" }, PropertyNames(actorForeignKey.Properties));
        Assert.Equal(DeleteBehavior.Restrict, actorForeignKey.DeleteBehavior);

        Assert.NotNull(auditEvent.FindProperty("CommandId"));
        Assert.DoesNotContain(
            auditEvent.GetForeignKeys(),
            foreignKey => foreignKey.Properties.Any(property => property.Name == "CommandId"));

        var commandIndex = Assert.Single(
            auditEvent.GetIndexes(),
            index => PropertyNames(index.Properties).SequenceEqual(new[] { "CommandId" }));
        Assert.False(commandIndex.IsUnique);

        Assert.Contains(
            auditEvent.GetIndexes(),
            index => PropertyNames(index.Properties).SequenceEqual(new[] { "ActorAccountId", "OccurredAt" }));
        Assert.Contains(
            auditEvent.GetIndexes(),
            index => PropertyNames(index.Properties).SequenceEqual(new[] { "EventKind", "OccurredAt" }));
    }

    [Fact]
    public void Audit_subject_is_historical_json_locator_not_domain_foreign_key()
    {
        using var context = CreateContext();
        var subject = GetTable(GetDesignTimeModel(context), "audit", "audit_event_subjects");

        Assert.Equal(new[] { "AuditEventId", "Sequence" }, PropertyNames(subject.FindPrimaryKey()!.Properties));

        var checks = CheckNames(subject);
        Assert.Contains("ck_audit_event_subjects_sequence", checks);
        Assert.Contains("ck_audit_event_subjects_subject_key_object", checks);
        Assert.Contains("ck_audit_event_subjects_change_kind", checks);

        Assert.Equal("jsonb", subject.FindProperty("SubjectKey")!.GetColumnType());
        Assert.Equal("jsonb", subject.FindProperty("ChangeSummary")!.GetColumnType());
        Assert.Equal(typeof(AuditChangeKind), subject.FindProperty("ChangeKind")!.ClrType);
        Assert.Equal("text", subject.FindProperty("ChangeKind")!.GetColumnType());
        Assert.Null(subject.FindProperty("RowVersion"));

        var foreignKey = Assert.Single(subject.GetForeignKeys());
        Assert.Equal("audit", foreignKey.PrincipalEntityType.GetSchema());
        Assert.Equal("audit_events", foreignKey.PrincipalEntityType.GetTableName());
        Assert.Equal(new[] { "AuditEventId" }, PropertyNames(foreignKey.Properties));
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void Correction_links_connect_audit_events_without_generic_domain_relationships()
    {
        using var context = CreateContext();
        var link = GetTable(GetDesignTimeModel(context), "audit", "correction_links");

        Assert.Equal(
            new[] { "CorrectionAuditEventId", "CorrectedAuditEventId" },
            PropertyNames(link.FindPrimaryKey()!.Properties));

        var checks = CheckNames(link);
        Assert.Contains("ck_correction_links_mode", checks);
        Assert.Contains("ck_correction_links_distinct_events", checks);
        Assert.Equal("text", link.FindProperty("CorrectionMode")!.GetColumnType());
        Assert.Null(link.FindProperty("RowVersion"));

        var foreignKeys = link.GetForeignKeys().ToArray();
        Assert.Equal(2, foreignKeys.Length);
        Assert.All(foreignKeys, foreignKey =>
        {
            Assert.Equal("audit", foreignKey.PrincipalEntityType.GetSchema());
            Assert.Equal("audit_events", foreignKey.PrincipalEntityType.GetTableName());
            Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        });

        Assert.Contains(
            link.GetIndexes(),
            index => PropertyNames(index.Properties).SequenceEqual(new[] { "CorrectedAuditEventId" }));
    }

    private static HashSet<string> CheckNames(IEntityType entityType)
    {
        return entityType.GetCheckConstraints().Select(check => check.Name!).ToHashSet(StringComparer.Ordinal);
    }

    private static string[] PropertyNames(IEnumerable<IReadOnlyProperty> properties)
    {
        return properties.Select(property => property.Name).ToArray();
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
