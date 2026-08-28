namespace YowThi.Erp.Api.Routing;

public static class ApiRouteGroups
{
    public const string Root = "/api/v1";

    public static IReadOnlyList<string> Modules { get; } =
    [
        "party",
        "product",
        "processing-config",
        "procurement",
        "processing",
        "inventory",
        "outsourced",
        "sales",
        "sales-handling",
        "labor",
        "finance",
        "audit",
        "data-protection",
    ];

    public static RouteGroupBuilder MapApiV1(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.MapGroup(Root)
            .RequireAuthorization();
    }
}
