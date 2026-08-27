using System.Security.Cryptography;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Serialization;

namespace YowThi.Erp.Infrastructure.Persistence.Idempotency;

internal sealed class Sha256CommandRequestHasher : ICommandRequestHasher
{
    public CommandRequestHash Compute(JsonPayload canonicalCommandPayload)
    {
        ArgumentNullException.ThrowIfNull(canonicalCommandPayload);

        var hash = SHA256.HashData(canonicalCommandPayload.Utf8Json.Span);
        return CommandRequestHash.FromSha256(hash);
    }
}
