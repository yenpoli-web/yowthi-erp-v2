using YowThi.Erp.Api.Errors;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Infrastructure;

namespace YowThi.Erp.Api.Infrastructure;

internal static class InfrastructureMasterEndpointSupport
{
    public static bool TryParseStatus(string? value, out InfrastructureMasterStatusFilter status)
    {
        status = value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "all" => InfrastructureMasterStatusFilter.All,
            "active" => InfrastructureMasterStatusFilter.Active,
            "inactive" => InfrastructureMasterStatusFilter.Inactive,
            "deleted" => InfrastructureMasterStatusFilter.Deleted,
            _ => (InfrastructureMasterStatusFilter)(-1),
        };
        return Enum.IsDefined(status);
    }

    public static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static IResult Failure(HttpContext httpContext, ApplicationError error, string title)
    {
        var statusCode = error.Kind switch
        {
            ApplicationErrorKind.Validation => StatusCodes.Status422UnprocessableEntity,
            ApplicationErrorKind.NotFound => StatusCodes.Status404NotFound,
            ApplicationErrorKind.Conflict => StatusCodes.Status409Conflict,
            ApplicationErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            _ => throw new ArgumentOutOfRangeException(nameof(error.Kind), error.Kind, "Unsupported application error kind."),
        };
        return ApiProblemResults.Create(httpContext, statusCode, error.Code, title);
    }
}
