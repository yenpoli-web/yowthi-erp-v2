using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Api.Localization;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Procurement;

namespace YowThi.Erp.Api.Procurement;

public static class ProcurementWorkspaceEndpoints
{
    public const string ListBatchesOperationId = "Procurement_ListBatches";
    public const string GetBatchWorkspaceOperationId = "Procurement_GetBatchWorkspace";
    public const string GetReceiptDestinationOperationId = "Procurement_GetReceiptDestination";

    public static IEndpointRouteBuilder MapProcurementWorkspaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var procurement = endpoints.MapApiV1().MapGroup("/procurement");

        procurement.MapGet("/receipt-destination", GetReceiptDestinationAsync)
            .WithName(GetReceiptDestinationOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcurementConfirm)
            .Produces<ProcurementReceiptDestinationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

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

    private static async Task<IResult> GetReceiptDestinationAsync(
        DateOnly procurementDate,
        Guid procurementProductId,
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IProcurementReceiptDestinationResolver resolver,
        CancellationToken cancellationToken)
    {
        var result = await resolver.ResolveAsync(
            procurementDate,
            procurementProductId,
            cancellationToken);

        if (result.IsFailure)
        {
            var statusCode = result.Error.Kind switch
            {
                ApplicationErrorKind.Validation => StatusCodes.Status422UnprocessableEntity,
                ApplicationErrorKind.NotFound => StatusCodes.Status404NotFound,
                ApplicationErrorKind.Conflict => StatusCodes.Status409Conflict,
                ApplicationErrorKind.Forbidden => StatusCodes.Status403Forbidden,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(result.Error.Kind),
                    result.Error.Kind,
                    "Unsupported application error kind."),
            };

            return ApiProblemResults.Create(
                httpContext,
                statusCode,
                result.Error.Code,
                "Procurement receipt destination resolution failed.");
        }

        var locale = localeResolver.Resolve(httpContext.Request);
        var destination = result.Value;
        var warehouseDisplayName = locale == "zh-TW"
            ? destination.WarehouseNameZhTw ?? destination.WarehouseNameThTh ?? "—"
            : destination.WarehouseNameThTh ?? destination.WarehouseNameZhTw ?? "—";

        return TypedResults.Ok(new ProcurementReceiptDestinationResponse(
            destination.StorageLocationId,
            destination.WarehouseId,
            warehouseDisplayName,
            destination.ResolutionSource));
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
            workspace.ReceiptStorageLocationId,
            workspace.WarehouseId,
            workspace.WarehouseDisplayName,
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
            item.RowVersion,
            item.RecordedAt,
            item.DeletedAt);
}

public sealed record ProcurementReceiptDestinationResponse(
    Guid StorageLocationId,
    Guid WarehouseId,
    string WarehouseDisplayName,
    string ResolutionSource);

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
    long RowVersion,
    DateTimeOffset RecordedAt,
    DateTimeOffset? DeletedAt);

public sealed record ProcurementBatchWorkspaceResponse(
    Guid Id,
    DateOnly ProcurementDate,
    Guid ProcurementProductId,
    string ProcurementProductDisplayName,
    string UnitCode,
    Guid? ReceiptStorageLocationId,
    Guid? WarehouseId,
    string? WarehouseDisplayName,
    string ProcurementStatus,
    string LifecycleStatus,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? DeletedAt,
    IReadOnlyList<ProcurementBatchEntryResponse> Entries);
