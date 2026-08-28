namespace YowThi.Erp.Infrastructure.Persistence.Audit;

internal sealed class CorrectionLinkRecord
{
    public Guid CorrectionAuditEventId { get; private set; }
    public Guid CorrectedAuditEventId { get; private set; }
    public AuditCorrectionMode CorrectionMode { get; private set; }
}
