using System.Text.Json;

namespace YowThi.Erp.Infrastructure.Persistence.System;

internal sealed class OutboxMessageRecord
{
    public Guid Id { get; private set; }
    public string MessageType { get; private set; } = null!;
    public int MessageVersion { get; private set; }
    public JsonElement Payload { get; private set; }
    public Guid? CommandId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset AvailableAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public int DeliveryAttemptCount { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }
    public Guid? LockToken { get; private set; }
    public string? LastErrorSummary { get; private set; }
}
