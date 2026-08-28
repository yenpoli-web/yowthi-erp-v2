using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Application.Common.Audit;
using YowThi.Erp.Infrastructure.Persistence.Audit;
using YowThi.Erp.Infrastructure.Persistence.System;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Audit;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEventRecord>
{
    public void Configure(EntityTypeBuilder<AuditEventRecord> builder)
    {
        builder.ToTable("audit_events", "audit", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_audit_events_event_kind", "event_kind IN ('BUSINESS_COMMAND', 'CORRECTION', 'DATA_LIFECYCLE', 'HARD_DELETE')");
        });

        builder.HasKey(x => x.Id).HasName("pk_audit_events");
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.CommandId).HasColumnName("command_id");
        builder.Property(x => x.CommandType).HasColumnName("command_type").HasColumnType("text");
        builder.Property(x => x.EventKind).HasColumnName("event_kind").HasConversion(AuditPersistenceConversions.EventKind).HasColumnType("text").IsRequired();
        builder.Property(x => x.ActorAccountId).HasColumnName("actor_account_id").IsRequired();
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.ReasonText).HasColumnName("reason_text").HasColumnType("text");

        builder.HasOne<SystemAccountRecord>().WithMany().HasForeignKey(x => x.ActorAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_audit_events_actor_account");

        builder.HasIndex(x => x.CommandId).HasDatabaseName("ix_audit_events_command_id");
        builder.HasIndex(x => new { x.ActorAccountId, x.OccurredAt }).HasDatabaseName("ix_audit_events_actor_occurred_at");
        builder.HasIndex(x => new { x.EventKind, x.OccurredAt }).HasDatabaseName("ix_audit_events_kind_occurred_at");
    }
}
