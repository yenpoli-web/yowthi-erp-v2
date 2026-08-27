using System.Text.Json;

namespace YowThi.Erp.Application.Common.Serialization;

public sealed class JsonPayload
{
    private readonly byte[] _utf8Json;

    private JsonPayload(byte[] utf8Json)
    {
        _utf8Json = utf8Json;
    }

    public ReadOnlyMemory<byte> Utf8Json => _utf8Json;

    public static JsonPayload FromUtf8Json(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IsEmpty)
        {
            throw new ArgumentException("JSON payload cannot be empty.", nameof(utf8Json));
        }

        var bytes = utf8Json.ToArray();
        using var _ = JsonDocument.Parse(bytes);
        return new JsonPayload(bytes);
    }
}
