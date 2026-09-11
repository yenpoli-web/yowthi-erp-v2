using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Api.Localization;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.Outsourced;

namespace YowThi.Erp.Api.Outsourced;

public static class OutsourcedWorkspaceEndpoints
{
    public const string ListBatchesOperationId = "Outsourced_ListWorkspace";
    public const string GetBatchOperationId = "Outsourced_GetWorkspace";

    public static IEndpointRouteBuilder MapOutsourcedWorkspaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapApiV1().MapGroup("/outsourced/workspace");

        group.MapGet("/", ListBatchesAsync)
            .WithName(ListBatchesOperationId)
            .RequireAuthorization(CapabilityPolicies.OutsourcedConfirm)
            .Produces<OutsourcedWorkspaceListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{outsourcedSupplyBatchId:guid}", GetBatchAsync)
            .WithName(GetBatchOperationId)
            .RequireAuthorization(CapabilityPolicies.OutsourcedConfirm)
            .Produces<OutsourcedWorkspaceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ListBatchesAsync(
        HttpContext context,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IOutsourcedWorkspaceReader reader,
        string? search = null,
        int offset = 0,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0 || limit is < 1 or > 100)
        {
            return ApiProblemResults.Create(
                context,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.RequestValidationFailed,
                "Request validation failed.",
                "offset must be non-negative and limit must be between 1 and 100.");
        }

        var page = await reader.GetBatchesAsync(
            new OutsourcedWorkspaceListQuery(
                localeResolver.Resolve(context.Request),
                string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                offset,
                limit),
            cancellationToken);

        return TypedResults.Ok(new OutsourcedWorkspaceListResponse(
            page.Items.Select(ToResponse).ToArray(),
            page.NextOffset));
    }

    private static async Task<IResult> GetBatchAsync(
        Guid outsourcedSupplyBatchId,
        HttpContext context,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IOutsourcedWorkspaceReader reader,
        CancellationToken cancellationToken)
    {
        var workspace = await reader.GetBatchAsync(
            outsourcedSupplyBatchId,
            localeResolver.Resolve(context.Request),
            cancellationToken);
        if (workspace is null)
        {
            return ApiProblemResults.Create(
                context,
                StatusCodes.Status404NotFound,
                "resource.not-found",
                "Outsourced Supply Batch was not found.");
        }

        return TypedResults.Ok(new OutsourcedWorkspaceResponse(
            workspace.Id,
            workspace.SupplyDate,
            workspace.OutsourcedVendorId,
            workspace.OutsourcedVendorDisplayName,
            workspace.LifecycleStatus,
            workspace.RowVersion,
            workspace.CreatedAt,
            workspace.ClosedAt,
            workspace.DeletedAt,
            workspace.Details.Select(ToResponse).ToArray()));
    }

    private static OutsourcedWorkspaceListItemResponse ToResponse(OutsourcedWorkspaceListItem item) =>
        new(item.Id, item.SupplyDate, item.OutsourcedVendorId, item.OutsourcedVendorDisplayName,
            item.LifecycleStatus, item.RowVersion, item.CreatedAt, item.ClosedAt, item.DeletedAt);

    private static OutsourcedWorkspaceDetailResponse ToResponse(OutsourcedWorkspaceDetailItem item) =>
        new(item.Id, item.SalesProductId, item.SalesProductDisplayName, item.Quantity, item.PricingBasis,
            item.UnitPrice, item.AmountThb, item.ReceiptStorageLocationId, item.ReceiptStorageLocationDisplayName,
            item.RowVersion, item.RecordedAt, item.DeletedAt);
}

public sealed record OutsourcedWorkspaceListItemResponse(
    Guid Id,
    DateOnly SupplyDate,
    Guid OutsourcedVendorId,
    string OutsourcedVendorDisplayName,
    string LifecycleStatus,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? DeletedAt);

public sealed record OutsourcedWorkspaceListResponse(
    IReadOnlyList<OutsourcedWorkspaceListItemResponse> Items,
    int? NextOffset);

public sealed record OutsourcedWorkspaceDetailResponse(
    Guid Id,
    Guid SalesProductId,
    string SalesProductDisplayName,
    decimal Quantity,
    string PricingBasis,
    decimal UnitPrice,
    long AmountThb,
    Guid ReceiptStorageLocationId,
    string ReceiptStorageLocationDisplayName,
    long RowVersion,
    DateTimeOffset RecordedAt,
    DateTimeOffset? DeletedAt);

public sealed record OutsourcedWorkspaceResponse(
    Guid Id,
    DateOnly SupplyDate,
    Guid OutsourcedVendorId,
    string OutsourcedVendorDisplayName,
    string LifecycleStatus,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? DeletedAt,
    IReadOnlyList<OutsourcedWorkspaceDetailResponse> Details);
