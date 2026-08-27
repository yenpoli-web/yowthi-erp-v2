using YowThi.Erp.Application.Common.Serialization;

namespace YowThi.Erp.Application.Common.Idempotency;

public sealed record CommandResultSnapshot
{
    private CommandResultSnapshot(JsonPayload payload)
    {
        Payload = payload;
    }

    public JsonPayload Payload { get; }

    public static CommandResultSnapshot From(JsonPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new CommandResultSnapshot(payload);
    }
}
