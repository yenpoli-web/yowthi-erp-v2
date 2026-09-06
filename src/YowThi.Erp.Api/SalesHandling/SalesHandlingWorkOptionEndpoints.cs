using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Api.Localization;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.SalesHandling;

namespace YowThi.Erp.Api.SalesHandling;

public static class SalesHandlingWorkOptionEndpoints
{
    public const string SalesOperationId = "SalesHandling_ListWorkSalesOptions";
    public const string EmployeesOperationId = "SalesHandling_ListWorkEmployeeOptions";
    public const string PackagingItemsOperationId = "SalesHandling_ListWorkPackagingItemOptions";

    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;

    public static IEndpointRouteBuilder MapSalesHandlingWorkOptionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var salesHandling = endpoints.MapApiV1().MapGroup("/sales-handling");

        salesHandling.MapGet("/work-options/sales", ListSalesAsync)
            .WithName(SalesOperationId)
            .RequireAuthorization(CapabilityPolicies.SalesHandlingWorkRecord)
            .Produces<SalesHandlingSaleOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        salesHandling.MapGet("/work-options/employees", ListEmployeesAsync)
            .WithName(EmployeesOperationId)
            .RequireAuthorization(CapabilityPolicies.SalesHandlingWorkRecord)
            .Produces<SalesHandlingEmployeeOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        salesHandling.MapGet("/work-options/packaging-items", ListPackagingItemsAsync)
            .WithName(PackagingItemsOperationId)
            .RequireAuthorization(CapabilityPolicies.SalesHandlingWorkRecord)
            .Produces<SalesHandlingPackagingItemOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    private static async Task<IResult> ListSalesAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] ISalesHandlingWorkOptionsReader reader,
        CancellationToken cancellationToken)
    {
        if (!TryCreateQuery(httpContext, localeResolver, out var query, out var error))
        {
            return error!;
        }

        var page = await reader.GetSalesAsync(query!, cancellationToken);
        return TypedResults.Ok(new SalesHandlingSaleOptionsResponse(
            page.Items.Select(item => new SalesHandlingSaleOptionResponse(
                item.Id,
                item.SalesDate,
                item.CustomerDisplayName,
                item.Status)).ToArray(),
            EncodeCursor(page.NextOffset)));
    }

    private static async Task<IResult> ListEmployeesAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] ISalesHandlingWorkOptionsReader reader,
        CancellationToken cancellationToken)
    {
        if (!TryCreateQuery(httpContext, localeResolver, out var query, out var error))
        {
            return error!;
        }

        var page = await reader.GetEmployeesAsync(query!, cancellationToken);
        return TypedResults.Ok(new SalesHandlingEmployeeOptionsResponse(
            page.Items.Select(item => new SalesHandlingEmployeeOptionResponse(item.Id, item.DisplayName)).ToArray(),
            EncodeCursor(page.NextOffset)));
    }

    private static async Task<IResult> ListPackagingItemsAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] ISalesHandlingWorkOptionsReader reader,
        CancellationToken cancellationToken)
    {
        if (!TryCreateQuery(httpContext, localeResolver, out var query, out var error))
        {
            return error!;
        }

        var page = await reader.GetPackagingItemsAsync(query!, cancellationToken);
        return TypedResults.Ok(new SalesHandlingPackagingItemOptionsResponse(
            page.Items.Select(item => new SalesHandlingPackagingItemOptionResponse(item.Id, item.DisplayName)).ToArray(),
            EncodeCursor(page.NextOffset)));
    }

    private static bool TryCreateQuery(
        HttpContext httpContext,
        IApiLocaleResolver localeResolver,
        out SalesHandlingWorkOptionsQuery? query,
        out IResult? error)
    {
        var rawSearch = httpContext.Request.Query["search"];
        var rawLimit = httpContext.Request.Query["limit"];
        var rawCursor = httpContext.Request.Query["cursor"];
        if (rawSearch.Count > 1 || rawLimit.Count > 1 || rawCursor.Count > 1)
        {
            query = null;
            error = ValidationProblem(httpContext, "search, limit, and cursor may each be supplied at most once.");
            return false;
        }

        var limit = DefaultLimit;
        if (rawLimit.Count == 1
            && (!int.TryParse(rawLimit[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit)
                || limit is < 1 or > MaxLimit))
        {
            query = null;
            error = ValidationProblem(httpContext, $"limit must be an integer between 1 and {MaxLimit}.");
            return false;
        }

        if (!TryDecodeCursor(rawCursor.Count == 1 ? rawCursor[0] : null, out var offset))
        {
            query = null;
            error = ValidationProblem(httpContext, "cursor is invalid.");
            return false;
        }

        var search = rawSearch.Count == 1 ? rawSearch[0] : null;
        query = new SalesHandlingWorkOptionsQuery(
            localeResolver.Resolve(httpContext.Request),
            string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            offset,
            limit);
        error = null;
        return true;
    }

    private static bool TryDecodeCursor(string? cursor, out int offset)
    {
        offset = 0;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return true;
        }

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
        if (offset is null)
        {
            return null;
        }

        return Convert.ToBase64String(
                Encoding.UTF8.GetBytes(offset.Value.ToString(CultureInfo.InvariantCulture)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static IResult ValidationProblem(HttpContext httpContext, string detail) =>
        ApiProblemResults.Create(
            httpContext,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.RequestValidationFailed,
            "Request validation failed.",
            detail);
}

public sealed record SalesHandlingSaleOptionResponse(
    Guid Id,
    DateOnly SalesDate,
    string CustomerDisplayName,
    string Status);

public sealed record SalesHandlingSaleOptionsResponse(
    IReadOnlyList<SalesHandlingSaleOptionResponse> Items,
    string? NextCursor);

public sealed record SalesHandlingEmployeeOptionResponse(
    Guid Id,
    string DisplayName);

public sealed record SalesHandlingEmployeeOptionsResponse(
    IReadOnlyList<SalesHandlingEmployeeOptionResponse> Items,
    string? NextCursor);

public sealed record SalesHandlingPackagingItemOptionResponse(
    Guid Id,
    string DisplayName);

public sealed record SalesHandlingPackagingItemOptionsResponse(
    IReadOnlyList<SalesHandlingPackagingItemOptionResponse> Items,
    string? NextCursor);
