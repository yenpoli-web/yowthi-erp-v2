namespace YowThi.Erp.Api.Idempotency;

public sealed class RequiresIdempotencyKeyMetadata
{
    public static RequiresIdempotencyKeyMetadata Instance { get; } = new();

    private RequiresIdempotencyKeyMetadata()
    {
    }
}
