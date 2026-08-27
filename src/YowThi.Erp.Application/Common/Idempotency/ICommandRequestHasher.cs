using YowThi.Erp.Application.Common.Serialization;

namespace YowThi.Erp.Application.Common.Idempotency;

public interface ICommandRequestHasher
{
    CommandRequestHash Compute(JsonPayload canonicalCommandPayload);
}
