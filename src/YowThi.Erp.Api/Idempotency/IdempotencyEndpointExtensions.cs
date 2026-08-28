using YowThi.Erp.Application.Common.Identity;

namespace YowThi.Erp.Api.Idempotency;

public static class IdempotencyEndpointExtensions
{
    public static RouteHandlerBuilder RequireIdempotencyKey(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.WithMetadata(RequiresIdempotencyKeyMetadata.Instance);
        builder.AddEndpointFilter<IdempotencyKeyEndpointFilter>();
        return builder;
    }

    public static CommandId GetRequiredCommandId(this HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return httpContext.Features.Get<IdempotencyKeyFeature>()?.CommandId
            ?? throw new InvalidOperationException("Idempotency-Key transport feature is not available for this endpoint.");
    }
}
