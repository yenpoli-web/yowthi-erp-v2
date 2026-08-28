using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Infrastructure.Persistence.System;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.System;

internal sealed class SystemAccountRecordConfiguration : IEntityTypeConfiguration<SystemAccountRecord>
{
    public void Configure(EntityTypeBuilder<SystemAccountRecord> builder)
    {
        builder.ToTable("accounts", "system", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_accounts_row_version", "row_version >= 1");
            tableBuilder.HasCheckConstraint(
                "ck_accounts_identity_pair",
                "(identity_issuer IS NULL AND identity_subject IS NULL) OR (identity_issuer IS NOT NULL AND identity_subject IS NOT NULL)");
            tableBuilder.HasCheckConstraint(
                "ck_accounts_identity_issuer_nonblank",
                "identity_issuer IS NULL OR btrim(identity_issuer) <> ''");
            tableBuilder.HasCheckConstraint(
                "ck_accounts_identity_subject_nonblank",
                "identity_subject IS NULL OR btrim(identity_subject) <> ''");
        });

        builder.HasKey(x => x.Id).HasName("pk_accounts");

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.DisplayName)
            .HasColumnName("display_name")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.Active)
            .HasColumnName("active")
            .IsRequired();

        builder.Property(x => x.IdentityIssuer)
            .HasColumnName("identity_issuer")
            .HasColumnType("text");

        builder.Property(x => x.IdentitySubject)
            .HasColumnName("identity_subject")
            .HasColumnType("text");

        builder.Property(x => x.RowVersion)
            .HasColumnName("row_version")
            .HasDefaultValue(1L)
            .IsConcurrencyToken();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(x => new { x.IdentityIssuer, x.IdentitySubject })
            .IsUnique()
            .HasFilter("identity_issuer IS NOT NULL")
            .HasDatabaseName("ux_accounts_identity_issuer_subject");
    }
}
