using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;

namespace YowThi.Erp.Application.Common.Audit;

public sealed record AuditEventDraft(
    Guid Id,
    CommandId? CommandId,
    CommandType? CommandType,
    AuditEventKind EventKind,
    ActorAccountId ActorAccountId,
    DateTimeOffset OccurredAt,
    string? ReasonText,
    IReadOnlyList<AuditSubjectDraft> Subjects);
