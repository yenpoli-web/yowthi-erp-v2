namespace YowThi.Erp.Api.Errors;

public static class ApiProblemResults
{
    public static IResult Create(
        HttpContext httpContext,
        int statusCode,
        string code,
        string title,
        string? detail = null,
        IDictionary<string, object?>? additionalExtensions = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["code"] = code,
            ["traceId"] = httpContext.TraceIdentifier,
        };

        if (additionalExtensions is not null)
        {
            foreach (var pair in additionalExtensions)
            {
                extensions[pair.Key] = pair.Value;
            }
        }

        return Results.Problem(
            detail: detail,
            instance: httpContext.Request.Path.Value,
            statusCode: statusCode,
            title: title,
            type: $"urn:yowthi:error:{code}",
            extensions: extensions);
    }
}
