using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Infrastructure.Persistence.System;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.System;

internal sealed class CommandExecutionRecordConfiguration : IEntityTypeConfiguration<CommandExecutionRecord>
{
    public void Configure(EntityTypeBuilder<CommandExecutionRecord> builder)
    {
        builder.ToTable("command_executions", "system", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_command_executions_request_hash_length",
                "octet_length(request_hash) = 32");
            tableBuilder.HasCheckConstraint(
                "ck_command_executions_status",
                "status IN ('IN_PROGRESS', 'SUCCEEDED')");
            tableBuilder.HasCheckConstraint(
                "ck_command_executions_execution_state",
                "(status = 'IN_PROGRESS' AND executed_at IS NULL) OR (status = 'SUCCEEDED' AND executed_at IS NOT NULL)");
        });

        builder.HasKey(x => x.CommandId).HasName("pk_command_executions");

        builder.Property(x => x.CommandId)
            .HasColumnName("command_id")
            .ValueGeneratedNever();

        builder.Property(x => x.CommandType)
            .HasColumnName("command_type")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.RequestHash)
            .HasColumnName("request_hash")
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.ResultPayload)
            .HasColumnName("result_payload")
            .HasColumnType("jsonb");

        builder.Property(x => x.ActorAccountId)
            .HasColumnName("actor_account_id")
            .IsRequired();

        builder.Property(x => x.StartedAt)
            .HasColumnName("started_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.ExecutedAt)
            .HasColumnName("executed_at")
            .HasColumnType("timestamp with time zone");

        builder.HasOne<SystemAccountRecord>()
            .WithMany()
            .HasForeignKey(x => x.ActorAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_command_executions_actor_account");

        builder.HasIndex(x => x.ActorAccountId)
            .HasDatabaseName("ix_command_executions_actor_account_id");
    }
}
