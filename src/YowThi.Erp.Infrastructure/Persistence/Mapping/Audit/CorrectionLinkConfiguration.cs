using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Infrastructure.Persistence.Audit;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Audit;

internal sealed class CorrectionLinkConfiguration : IEntityTypeConfiguration<CorrectionLinkRecord>
{
    public void Configure(EntityTypeBuilder<CorrectionLinkRecord> builder)
    {
        builder.ToTable("correction_links", "audit", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_correction_links_mode", "correction_mode IN ('DIRECT_AMENDMENT', 'COMPENSATION')");
            tableBuilder.HasCheckConstraint("ck_correction_links_distinct_events", "correction_audit_event_id <> corrected_audit_event_id");
        });

        builder.HasKey(x => new { x.CorrectionAuditEventId, x.CorrectedAuditEventId }).HasName("pk_correction_links");
        builder.Property(x => x.CorrectionAuditEventId).HasColumnName("correction_audit_event_id").IsRequired();
        builder.Property(x => x.CorrectedAuditEventId).HasColumnName("corrected_audit_event_id").IsRequired();
        builder.Property(x => x.CorrectionMode).HasColumnName("correction_mode").HasConversion(AuditPersistenceConversions.CorrectionMode).HasColumnType("text").IsRequired();

        builder.HasOne<AuditEventRecord>().WithMany().HasForeignKey(x => x.CorrectionAuditEventId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_correction_links_correction_event");
        builder.HasOne<AuditEventRecord>().WithMany().HasForeignKey(x => x.CorrectedAuditEventId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_correction_links_corrected_event");
        builder.HasIndex(x => x.CorrectedAuditEventId).HasDatabaseName("ix_correction_links_corrected_event_id");
    }
}
