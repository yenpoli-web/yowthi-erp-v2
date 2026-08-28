using YowThi.Erp.Application.Common.Audit;

namespace YowThi.Erp.Infrastructure.Persistence.Audit;

internal sealed class AuditEventRecord
{
    public Guid Id { get; private set; }
    public Guid? CommandId { get; private set; }
    public string? CommandType { get; private set; }
    public AuditEventKind EventKind { get; private set; }
    public Guid ActorAccountId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string? ReasonText { get; private set; }
}
