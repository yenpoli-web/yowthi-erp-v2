using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Api.Localization;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.Sales;

namespace YowThi.Erp.Api.Sales;

public static class SalesConfirmationOptionEndpoints
{
    public const string SalesOperationId = "Sales_ListConfirmationOptions";
    public const string WorkspaceOperationId = "Sales_GetConfirmationWorkspace";
    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;

    public static IEndpointRouteBuilder MapSalesConfirmationOptionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var sales = endpoints.MapApiV1().MapGroup("/sales");

        sales.MapGet("/confirmation-options/sales", ListSalesAsync)
            .WithName(SalesOperationId)
            .RequireAuthorization(CapabilityPolicies.SalesConfirm)
            .Produces<SalesConfirmationOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        sales.MapGet("/{salesId:guid}/confirmation-workspace", GetWorkspaceAsync)
            .WithName(WorkspaceOperationId)
            .RequireAuthorization(CapabilityPolicies.SalesConfirm)
            .Produces<SalesConfirmationWorkspaceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ListSalesAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] ISalesConfirmationOptionsReader reader,
        CancellationToken cancellationToken)
    {
        if (!TryCreateQuery(httpContext, localeResolver, out var query, out var error))
        {
            return error!;
        }

        var page = await reader.GetSalesAsync(query!, cancellationToken);
        return TypedResults.Ok(new SalesConfirmationOptionsResponse(
            page.Items.Select(item => new SalesConfirmationSaleOptionResponse(
                item.Id,
                item.SalesDate,
                item.CustomerDisplayName,
                item.RowVersion)).ToArray(),
            EncodeCursor(page.NextOffset)));
    }

    private static async Task<IResult> GetWorkspaceAsync(
        Guid salesId,
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] ISalesConfirmationOptionsReader reader,
        CancellationToken cancellationToken)
    {
        var workspace = await reader.GetWorkspaceAsync(
            salesId,
            localeResolver.Resolve(httpContext.Request),
            cancellationToken);
        if (workspace is null)
        {
            return ApiProblemResults.Create(
                httpContext,
                StatusCodes.Status404NotFound,
                "sales.not-found",
                "Draft Sale was not found in the confirmation view.");
        }

        return TypedResults.Ok(new SalesConfirmationWorkspaceResponse(
            workspace.SalesId,
            workspace.SalesDate,
            workspace.CustomerId,
            workspace.CustomerDisplayName,
            workspace.RowVersion,
            workspace.Details.Select(detail => new SalesConfirmationDetailResponse(
                detail.Id,
                detail.LineNumber,
                detail.SalesProductId,
                detail.ProductDisplayName,
                detail.Quantity,
                detail.PricingBasis,
                detail.SalesWeight,
                detail.UnitPrice,
                detail.AmountThb)).ToArray()));
    }

    private static bool TryCreateQuery(
        HttpContext httpContext,
        IApiLocaleResolver localeResolver,
        out SalesConfirmationOptionsQuery? query,
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
        query = new SalesConfirmationOptionsQuery(
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
        if (string.IsNullOrWhiteSpace(cursor)) return true;
        try
        {
            var normalized = cursor.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            return int.TryParse(Encoding.UTF8.GetString(Convert.FromBase64String(normalized)), NumberStyles.None, CultureInfo.InvariantCulture, out offset)
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
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(offset.Value.ToString(CultureInfo.InvariantCulture)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static IResult ValidationProblem(HttpContext httpContext, string detail) =>
        ApiProblemResults.Create(
            httpContext,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.RequestValidationFailed,
            "Request validation failed.",
            detail);
}

public sealed record SalesConfirmationSaleOptionResponse(Guid Id, DateOnly SalesDate, string CustomerDisplayName, long RowVersion);
public sealed record SalesConfirmationOptionsResponse(IReadOnlyList<SalesConfirmationSaleOptionResponse> Items, string? NextCursor);
public sealed record SalesConfirmationDetailResponse(Guid Id, int LineNumber, Guid SalesProductId, string ProductDisplayName, decimal Quantity, string PricingBasis, decimal? SalesWeight, decimal UnitPrice, long AmountThb);
public sealed record SalesConfirmationWorkspaceResponse(Guid SalesId, DateOnly SalesDate, Guid CustomerId, string CustomerDisplayName, long RowVersion, IReadOnlyList<SalesConfirmationDetailResponse> Details);
