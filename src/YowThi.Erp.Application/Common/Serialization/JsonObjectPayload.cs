using System.Text.Json;

namespace YowThi.Erp.Application.Common.Serialization;

public sealed class JsonObjectPayload
{
    private readonly byte[] _utf8Json;

    private JsonObjectPayload(byte[] utf8Json)
    {
        _utf8Json = utf8Json;
    }

    public ReadOnlyMemory<byte> Utf8Json => _utf8Json;

    public static JsonObjectPayload FromUtf8Json(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IsEmpty)
        {
            throw new ArgumentException("JSON object payload cannot be empty.", nameof(utf8Json));
        }

        var bytes = utf8Json.ToArray();
        using var document = JsonDocument.Parse(bytes);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("JSON payload must have an object root.", nameof(utf8Json));
        }

        return new JsonObjectPayload(bytes);
    }
}
