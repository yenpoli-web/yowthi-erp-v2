using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Infrastructure.Persistence.System;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.System;

internal sealed class OutboxMessageRecordConfiguration : IEntityTypeConfiguration<OutboxMessageRecord>
{
    public void Configure(EntityTypeBuilder<OutboxMessageRecord> builder)
    {
        builder.ToTable("outbox_messages", "system", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_outbox_messages_message_version", "message_version > 0");
            tableBuilder.HasCheckConstraint("ck_outbox_messages_delivery_attempt_count", "delivery_attempt_count >= 0");
        });

        builder.HasKey(x => x.Id).HasName("pk_outbox_messages");

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.MessageType)
            .HasColumnName("message_type")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.MessageVersion)
            .HasColumnName("message_version")
            .IsRequired();

        builder.Property(x => x.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.CommandId)
            .HasColumnName("command_id");

        builder.Property(x => x.OccurredAt)
            .HasColumnName("occurred_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.AvailableAt)
            .HasColumnName("available_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.PublishedAt)
            .HasColumnName("published_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.DeliveryAttemptCount)
            .HasColumnName("delivery_attempt_count")
            .IsRequired();

        builder.Property(x => x.NextAttemptAt)
            .HasColumnName("next_attempt_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.LockedUntil)
            .HasColumnName("locked_until")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.LockToken)
            .HasColumnName("lock_token");

        builder.Property(x => x.LastErrorSummary)
            .HasColumnName("last_error_summary")
            .HasColumnType("text");
    }
}
