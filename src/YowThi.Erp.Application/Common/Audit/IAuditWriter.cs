namespace YowThi.Erp.Application.Common.Audit;

public interface IAuditWriter
{
    ValueTask AppendAsync(
        AuditEventDraft auditEvent,
        CancellationToken cancellationToken);
}
