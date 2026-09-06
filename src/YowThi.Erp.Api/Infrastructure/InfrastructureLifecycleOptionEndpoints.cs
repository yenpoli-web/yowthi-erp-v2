using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Api.Localization;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.Infrastructure;

namespace YowThi.Erp.Api.Infrastructure;

public static class InfrastructureLifecycleOptionEndpoints
{
    public const string ContainersOperationId = "Infrastructure_ListContainerLifecycleOptions";
    public const string WarehousesOperationId = "Infrastructure_ListWarehouseLifecycleOptions";

    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;

    public static IEndpointRouteBuilder MapInfrastructureLifecycleOptionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var infrastructure = endpoints.MapApiV1().MapGroup("/infrastructure/lifecycle-options");

        infrastructure.MapGet("/containers", ListContainersAsync)
            .WithName(ContainersOperationId)
            .RequireAuthorization(CapabilityPolicies.InfrastructureContainerLifecycle)
            .Produces<ContainerLifecycleOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        infrastructure.MapGet("/warehouses", ListWarehousesAsync)
            .WithName(WarehousesOperationId)
            .RequireAuthorization(CapabilityPolicies.InfrastructureWarehouseLifecycle)
            .Produces<WarehouseLifecycleOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    private static async Task<IResult> ListContainersAsync(
        HttpContext context,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IInfrastructureLifecycleOptionsReader reader,
        CancellationToken cancellationToken)
    {
        if (!TryCreateQuery(context, localeResolver, out var query, out var error)) return error!;
        var page = await reader.GetContainersAsync(query!, cancellationToken);
        return TypedResults.Ok(new ContainerLifecycleOptionsResponse(
            page.Items.Select(item => new ContainerLifecycleOptionResponse(
                item.Id,
                item.DisplayName,
                item.TareWeight,
                item.Active,
                item.RowVersion,
                item.Deleted,
                item.DeletedAt)).ToArray(),
            EncodeCursor(page.NextOffset)));
    }

    private static async Task<IResult> ListWarehousesAsync(
        HttpContext context,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IInfrastructureLifecycleOptionsReader reader,
        CancellationToken cancellationToken)
    {
        if (!TryCreateQuery(context, localeResolver, out var query, out var error)) return error!;
        var page = await reader.GetWarehousesAsync(query!, cancellationToken);
        return TypedResults.Ok(new WarehouseLifecycleOptionsResponse(
            page.Items.Select(item => new WarehouseLifecycleOptionResponse(
                item.Id,
                item.DisplayName,
                item.Code,
                item.Active,
                item.RowVersion,
                item.Deleted,
                item.DeletedAt)).ToArray(),
            EncodeCursor(page.NextOffset)));
    }

    private static bool TryCreateQuery(
        HttpContext context,
        IApiLocaleResolver localeResolver,
        out InfrastructureLifecycleOptionsQuery? query,
        out IResult? error)
    {
        var searches = context.Request.Query["search"];
        var limits = context.Request.Query["limit"];
        var cursors = context.Request.Query["cursor"];
        if (searches.Count > 1 || limits.Count > 1 || cursors.Count > 1)
        {
            query = null;
            error = ValidationProblem(context, "search, limit, and cursor may each be supplied at most once.");
            return false;
        }

        var limit = DefaultLimit;
        if (limits.Count == 1
            && (!int.TryParse(limits[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit)
                || limit is < 1 or > MaxLimit))
        {
            query = null;
            error = ValidationProblem(context, $"limit must be an integer between 1 and {MaxLimit}.");
            return false;
        }

        if (!TryDecodeCursor(cursors.Count == 1 ? cursors[0] : null, out var offset))
        {
            query = null;
            error = ValidationProblem(context, "cursor is invalid.");
            return false;
        }

        var search = searches.Count == 1 ? searches[0] : null;
        query = new InfrastructureLifecycleOptionsQuery(
            localeResolver.Resolve(context.Request),
            string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            offset,
            limit);
        error = null;
        return true;
    }

    private static bool TryDecodeCursor(string? cursor, out int offset)
    {
        offset = 0;
        if (string.IsNullOrWhiteSpace(cursor)) return true;
        try
        {
            var normalized = cursor.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            return int.TryParse(
                    Encoding.UTF8.GetString(Convert.FromBase64String(normalized)),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out offset)
                && offset >= 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string? EncodeCursor(int? offset)
    {
        if (offset is null) return null;
        return Convert.ToBase64String(
                Encoding.UTF8.GetBytes(offset.Value.ToString(CultureInfo.InvariantCulture)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static IResult ValidationProblem(HttpContext context, string detail) =>
        ApiProblemResults.Create(
            context,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.RequestValidationFailed,
            "Request validation failed.",
            detail);
}

public sealed record ContainerLifecycleOptionResponse(
    Guid Id,
    string DisplayName,
    decimal TareWeight,
    bool Active,
    long RowVersion,
    bool Deleted,
    DateTimeOffset? DeletedAt);

public sealed record WarehouseLifecycleOptionResponse(
    Guid Id,
    string DisplayName,
    string? Code,
    bool Active,
    long RowVersion,
    bool Deleted,
    DateTimeOffset? DeletedAt);

public sealed record ContainerLifecycleOptionsResponse(
    IReadOnlyList<ContainerLifecycleOptionResponse> Items,
    string? NextCursor);

public sealed record WarehouseLifecycleOptionsResponse(
    IReadOnlyList<WarehouseLifecycleOptionResponse> Items,
    string? NextCursor);
