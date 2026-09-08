using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Infrastructure.Persistence.System;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.System;

internal sealed class SystemAccountCapabilityGrantRecordConfiguration : IEntityTypeConfiguration<SystemAccountCapabilityGrantRecord>
{
    public void Configure(EntityTypeBuilder<SystemAccountCapabilityGrantRecord> builder)
    {
        builder.ToTable("account_capability_grants", "system", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_account_capability_grants_row_version", "row_version >= 1");
            tableBuilder.HasCheckConstraint("ck_account_capability_grants_capability_nonblank", "btrim(capability_name) <> ''");
        });

        builder.HasKey(x => x.Id).HasName("pk_account_capability_grants");

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.AccountId)
            .HasColumnName("account_id")
            .IsRequired();

        builder.Property(x => x.CapabilityName)
            .HasColumnName("capability_name")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.Active)
            .HasColumnName("active")
            .IsRequired();

        builder.Property(x => x.RowVersion)
            .HasColumnName("row_version")
            .HasDefaultValue(1L)
            .IsConcurrencyToken();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.CreatedByAccountId)
            .HasColumnName("created_by_account_id")
            .IsRequired();

        builder.HasIndex(x => new { x.AccountId, x.CapabilityName })
            .IsUnique()
            .HasDatabaseName("ux_account_capability_grants_account_capability");

        builder.HasIndex(x => x.CreatedByAccountId)
            .HasDatabaseName("ix_account_capability_grants_created_by_account_id");

        builder.HasOne<SystemAccountRecord>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_account_capability_grants_accounts_account_id");

        builder.HasOne<SystemAccountRecord>()
            .WithMany()
            .HasForeignKey(x => x.CreatedByAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_account_capability_grants_accounts_created_by_account_id");
    }
}
