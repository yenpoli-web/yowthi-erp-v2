using System.Text.Json;

namespace YowThi.Erp.Infrastructure.Persistence.System;

internal sealed class CommandExecutionRecord
{
    public Guid CommandId { get; private set; }
    public string CommandType { get; private set; } = null!;
    public byte[] RequestHash { get; private set; } = Array.Empty<byte>();
    public string Status { get; private set; } = null!;
    public JsonElement? ResultPayload { get; private set; }
    public Guid ActorAccountId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? ExecutedAt { get; private set; }
}
