using YowThi.Erp.Application.Common.Serialization;

namespace YowThi.Erp.Application.Common.Audit;

public sealed record AuditSubjectDraft(
    int Sequence,
    string SubjectKind,
    JsonObjectPayload SubjectKey,
    AuditChangeKind ChangeKind,
    long? BeforeRowVersion,
    long? AfterRowVersion,
    JsonPayload? ChangeSummary);
