using YowThi.Erp.Api.Errors;

namespace YowThi.Erp.Api.Authorization;

public sealed class DeletionReauthenticationEndpointFilter : IEndpointFilter
{
    private readonly SecurityRuntimeOptions _runtimeOptions;
    private readonly TimeProvider _timeProvider;

    public DeletionReauthenticationEndpointFilter(
        SecurityRuntimeOptions runtimeOptions,
        TimeProvider timeProvider)
    {
        _runtimeOptions = runtimeOptions;
        _timeProvider = timeProvider;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (!DeletionReauthenticationClaims.TryGetFreshTimestamp(
                context.HttpContext.User,
                _timeProvider,
                _runtimeOptions.DeletionReauthenticationMaxAge,
                out _))
        {
            return ApiProblemResults.Create(
                context.HttpContext,
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.DeletionReauthenticationRequired,
                "Recent authentication confirmation is required before deletion.");
        }

        return await next(context);
    }
}

public sealed class RequiresDeletionReauthenticationMetadata
{
    private RequiresDeletionReauthenticationMetadata()
    {
    }

    public static RequiresDeletionReauthenticationMetadata Instance { get; } = new();
}

public static class DeletionReauthenticationEndpointExtensions
{
    public static RouteHandlerBuilder RequireRecentDeletionReauthentication(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.WithMetadata(RequiresDeletionReauthenticationMetadata.Instance);
        builder.AddEndpointFilter<DeletionReauthenticationEndpointFilter>();
        return builder;
    }
}
