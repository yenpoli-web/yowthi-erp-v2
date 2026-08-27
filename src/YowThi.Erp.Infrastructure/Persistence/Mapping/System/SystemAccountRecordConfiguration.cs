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

        builder.Property(x => x.RowVersion)
            .HasColumnName("row_version")
            .HasDefaultValue(1L)
            .IsConcurrencyToken();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
    }
}
