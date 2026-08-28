using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YowThi.Erp.Infrastructure.Persistence.Audit;

namespace YowThi.Erp.Infrastructure.Persistence.Mapping.Audit;

internal sealed class AuditEventSubjectConfiguration : IEntityTypeConfiguration<AuditEventSubjectRecord>
{
    public void Configure(EntityTypeBuilder<AuditEventSubjectRecord> builder)
    {
        builder.ToTable("audit_event_subjects", "audit", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_audit_event_subjects_sequence", "sequence > 0");
            tableBuilder.HasCheckConstraint("ck_audit_event_subjects_subject_key_object", "jsonb_typeof(subject_key) = 'object'");
            tableBuilder.HasCheckConstraint("ck_audit_event_subjects_change_kind", "change_kind IN ('CREATE', 'UPDATE', 'SOFT_DELETE', 'RESTORE', 'HARD_DELETE')");
        });

        builder.HasKey(x => new { x.AuditEventId, x.Sequence }).HasName("pk_audit_event_subjects");
        builder.Property(x => x.AuditEventId).HasColumnName("audit_event_id").IsRequired();
        builder.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(x => x.SubjectKind).HasColumnName("subject_kind").HasColumnType("text").IsRequired();
        builder.Property(x => x.SubjectKey).HasColumnName("subject_key").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ChangeKind).HasColumnName("change_kind").HasConversion(AuditPersistenceConversions.ChangeKind).HasColumnType("text").IsRequired();
        builder.Property(x => x.BeforeRowVersion).HasColumnName("before_row_version");
        builder.Property(x => x.AfterRowVersion).HasColumnName("after_row_version");
        builder.Property(x => x.ChangeSummary).HasColumnName("change_summary").HasColumnType("jsonb");

        builder.HasOne<AuditEventRecord>().WithMany().HasForeignKey(x => x.AuditEventId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_audit_event_subjects_event");
    }
}
