using System.Text.Json;
using YowThi.Erp.Application.Common.Audit;

namespace YowThi.Erp.Infrastructure.Persistence.Audit;

internal sealed class AuditEventSubjectRecord
{
    public Guid AuditEventId { get; private set; }
    public int Sequence { get; private set; }
    public string SubjectKind { get; private set; } = null!;
    public JsonElement SubjectKey { get; private set; }
    public AuditChangeKind ChangeKind { get; private set; }
    public long? BeforeRowVersion { get; private set; }
    public long? AfterRowVersion { get; private set; }
    public JsonElement? ChangeSummary { get; private set; }
}
