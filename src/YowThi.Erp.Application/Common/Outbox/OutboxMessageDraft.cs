using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Serialization;

namespace YowThi.Erp.Application.Common.Outbox;

public sealed record OutboxMessageDraft(
    Guid Id,
    string MessageType,
    int MessageVersion,
    JsonPayload Payload,
    CommandId? CommandId,
    DateTimeOffset OccurredAt,
    DateTimeOffset AvailableAt);
