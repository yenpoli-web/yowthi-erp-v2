using YowThi.Erp.Api.Errors;
using YowThi.Erp.Application.Common.Identity;

namespace YowThi.Erp.Api.Idempotency;

public sealed class IdempotencyKeyEndpointFilter : IEndpointFilter
{
    public const string HeaderName = "Idempotency-Key";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var values = context.HttpContext.Request.Headers[HeaderName];
        if (values.Count == 0 || string.IsNullOrWhiteSpace(values[0]))
        {
            return ApiProblemResults.Create(
                context.HttpContext,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.IdempotencyKeyMissing,
                "Idempotency key is required.");
        }

        if (values.Count != 1 || !Guid.TryParse(values[0], out var value) || value == Guid.Empty)
        {
            return ApiProblemResults.Create(
                context.HttpContext,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.IdempotencyKeyInvalid,
                "Idempotency key is invalid.");
        }

        context.HttpContext.Features.Set(new IdempotencyKeyFeature(CommandId.From(value)));

        return await next(context);
    }
}
