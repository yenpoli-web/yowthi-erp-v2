using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Api.Localization;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.Sales;

namespace YowThi.Erp.Api.Sales;

public static class SalesWorkspaceEndpoints
{
    public const string ListSalesOperationId = "Sales_ListWorkspace";
    public const string GetSalesWorkspaceOperationId = "Sales_GetWorkspace";

    public static IEndpointRouteBuilder MapSalesWorkspaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var sales = endpoints.MapApiV1().MapGroup("/sales");

        sales.MapGet("/workspace", ListSalesAsync)
            .WithName(ListSalesOperationId)
            .RequireAuthorization(CapabilityPolicies.SalesTransactionLifecycle)
            .Produces<SalesWorkspaceListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        sales.MapGet("/workspace/{salesId:guid}", GetSaleAsync)
            .WithName(GetSalesWorkspaceOperationId)
            .RequireAuthorization(CapabilityPolicies.SalesTransactionLifecycle)
            .Produces<SalesWorkspaceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ListSalesAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] ISalesWorkspaceReader reader,
        string? search = null,
        int offset = 0,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0 || limit is < 1 or > 100)
        {
            return ApiProblemResults.Create(
                httpContext,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.RequestValidationFailed,
                "Request validation failed.",
                "offset must be non-negative and limit must be between 1 and 100.");
        }

        var page = await reader.GetSalesAsync(
            new SalesWorkspaceListQuery(
                localeResolver.Resolve(httpContext.Request),
                string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                offset,
                limit),
            cancellationToken);

        return TypedResults.Ok(new SalesWorkspaceListResponse(
            page.Items.Select(ToResponse).ToArray(),
            page.NextOffset));
    }

    private static async Task<IResult> GetSaleAsync(
        Guid salesId,
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] ISalesWorkspaceReader reader,
        CancellationToken cancellationToken)
    {
        var workspace = await reader.GetSaleAsync(
            salesId,
            localeResolver.Resolve(httpContext.Request),
            cancellationToken);

        if (workspace is null)
        {
            return ApiProblemResults.Create(
                httpContext,
                StatusCodes.Status404NotFound,
                "resource.not-found",
                "Sale was not found.");
        }

        return TypedResults.Ok(new SalesWorkspaceResponse(
            workspace.Id,
            workspace.SalesDate,
            workspace.CustomerId,
            workspace.CustomerDisplayName,
            workspace.Status,
            workspace.RowVersion,
            workspace.CreatedAt,
            workspace.ConfirmedAt,
            workspace.DeletedAt,
            workspace.Details.Select(ToResponse).ToArray()));
    }

    private static SalesWorkspaceListItemResponse ToResponse(SalesWorkspaceListItem item) =>
        new(
            item.Id,
            item.SalesDate,
            item.CustomerId,
            item.CustomerDisplayName,
            item.Status,
            item.RowVersion,
            item.CreatedAt,
            item.ConfirmedAt,
            item.DeletedAt);

    private static SalesWorkspaceDetailResponse ToResponse(SalesWorkspaceDetailItem item) =>
        new(
            item.Id,
            item.LineNumber,
            item.SalesProductId,
            item.ProductDisplayName,
            item.Quantity,
            item.PricingBasis,
            item.SalesWeight,
            item.UnitPrice,
            item.AmountThb,
            item.RowVersion,
            item.CreatedAt,
            item.DeletedAt);
}

public sealed record SalesWorkspaceListItemResponse(
    Guid Id,
    DateOnly SalesDate,
    Guid CustomerId,
    string CustomerDisplayName,
    string Status,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? DeletedAt);

public sealed record SalesWorkspaceListResponse(
    IReadOnlyList<SalesWorkspaceListItemResponse> Items,
    int? NextOffset);

public sealed record SalesWorkspaceDetailResponse(
    Guid Id,
    int LineNumber,
    Guid SalesProductId,
    string ProductDisplayName,
    decimal Quantity,
    string PricingBasis,
    decimal? SalesWeight,
    decimal UnitPrice,
    long AmountThb,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt);

public sealed record SalesWorkspaceResponse(
    Guid Id,
    DateOnly SalesDate,
    Guid CustomerId,
    string CustomerDisplayName,
    string Status,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? DeletedAt,
    IReadOnlyList<SalesWorkspaceDetailResponse> Details);
