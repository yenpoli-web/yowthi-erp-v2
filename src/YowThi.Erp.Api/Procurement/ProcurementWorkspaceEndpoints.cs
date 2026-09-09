using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Api.Localization;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.Procurement;

namespace YowThi.Erp.Api.Procurement;

public static class ProcurementWorkspaceEndpoints
{
    public const string ListBatchesOperationId = "Procurement_ListBatches";
    public const string GetBatchWorkspaceOperationId = "Procurement_GetBatchWorkspace";

    public static IEndpointRouteBuilder MapProcurementWorkspaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var procurement = endpoints.MapApiV1().MapGroup("/procurement");

        procurement.MapGet("/batches", ListBatchesAsync)
            .WithName(ListBatchesOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcurementConfirm)
            .Produces<ProcurementBatchListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        procurement.MapGet("/batches/{procurementBatchId:guid}", GetBatchAsync)
            .WithName(GetBatchWorkspaceOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcurementConfirm)
            .Produces<ProcurementBatchWorkspaceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ListBatchesAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IProcurementWorkspaceReader reader,
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

        var page = await reader.GetBatchesAsync(
            new ProcurementBatchListQuery(
                localeResolver.Resolve(httpContext.Request),
                string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                offset,
                limit),
            cancellationToken);

        return TypedResults.Ok(new ProcurementBatchListResponse(
            page.Items.Select(ToResponse).ToArray(),
            page.NextOffset));
    }

    private static async Task<IResult> GetBatchAsync(
        Guid procurementBatchId,
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IProcurementWorkspaceReader reader,
        CancellationToken cancellationToken)
    {
        var workspace = await reader.GetBatchAsync(
            procurementBatchId,
            localeResolver.Resolve(httpContext.Request),
            cancellationToken);

        if (workspace is null)
        {
            return ApiProblemResults.Create(
                httpContext,
                StatusCodes.Status404NotFound,
                "resource.not-found",
                "Procurement Batch was not found.");
        }

        return TypedResults.Ok(new ProcurementBatchWorkspaceResponse(
            workspace.Id,
            workspace.ProcurementDate,
            workspace.ProcurementProductId,
            workspace.ProcurementProductDisplayName,
            workspace.UnitCode,
            workspace.ProcurementStatus,
            workspace.LifecycleStatus,
            workspace.RowVersion,
            workspace.CreatedAt,
            workspace.CompletedAt,
            workspace.ClosedAt,
            workspace.DeletedAt,
            workspace.Entries.Select(ToResponse).ToArray()));
    }

    private static ProcurementBatchListItemResponse ToResponse(ProcurementBatchListItem item) =>
        new(
            item.Id,
            item.ProcurementDate,
            item.ProcurementProductId,
            item.ProcurementProductDisplayName,
            item.UnitCode,
            item.ProcurementStatus,
            item.LifecycleStatus,
            item.RowVersion,
            item.CreatedAt,
            item.DeletedAt);

    private static ProcurementBatchEntryResponse ToResponse(ProcurementBatchEntryItem item) =>
        new(
            item.Id,
            item.SourceType,
            item.SourceId,
            item.SourceCode,
            item.SourceDisplayName,
            item.NetQuantity,
            item.UnitCodeSnapshot,
            item.UnitPrice,
            item.AmountThb,
            item.CompanyPickup,
            item.ReceiptStorageLocationId,
            item.ReceiptStorageLocationDisplayName,
            item.RowVersion,
            item.RecordedAt,
            item.DeletedAt);
}

public sealed record ProcurementBatchListItemResponse(
    Guid Id,
    DateOnly ProcurementDate,
    Guid ProcurementProductId,
    string ProcurementProductDisplayName,
    string UnitCode,
    string ProcurementStatus,
    string LifecycleStatus,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt);

public sealed record ProcurementBatchListResponse(
    IReadOnlyList<ProcurementBatchListItemResponse> Items,
    int? NextOffset);

public sealed record ProcurementBatchEntryResponse(
    Guid Id,
    string SourceType,
    Guid SourceId,
    string? SourceCode,
    string SourceDisplayName,
    decimal NetQuantity,
    string UnitCodeSnapshot,
    decimal UnitPrice,
    long AmountThb,
    bool CompanyPickup,
    Guid? ReceiptStorageLocationId,
    string? ReceiptStorageLocationDisplayName,
    long RowVersion,
    DateTimeOffset RecordedAt,
    DateTimeOffset? DeletedAt);

public sealed record ProcurementBatchWorkspaceResponse(
    Guid Id,
    DateOnly ProcurementDate,
    Guid ProcurementProductId,
    string ProcurementProductDisplayName,
    string UnitCode,
    string ProcurementStatus,
    string LifecycleStatus,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? DeletedAt,
    IReadOnlyList<ProcurementBatchEntryResponse> Entries);
