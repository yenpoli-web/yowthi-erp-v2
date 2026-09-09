using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Localization;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.Inventory;
using YowThi.Erp.Domain.Inventory;

namespace YowThi.Erp.Api.Inventory;

public static class InventoryPositionEndpoints
{
    public static IEndpointRouteBuilder MapInventoryPositionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapApiV1().MapGroup("/inventory")
            .MapGet("/positions", ListAsync)
            .WithName("Inventory_ListPositions")
            .RequireAuthorization(CapabilityPolicies.InventoryView)
            .Produces<InventoryPositionResponsePage>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IInventoryPositionReader reader,
        string? search,
        string? origin,
        string? objectKind,
        Guid? warehouseId,
        Guid? storageLocationId,
        bool includeZeroBalance = false,
        int offset = 0,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0 || limit is < 1 or > 200
            || warehouseId == Guid.Empty || storageLocationId == Guid.Empty
            || !TryParseOptionalEnum(origin, out InventoryOrigin? parsedOrigin)
            || !TryParseOptionalEnum(objectKind, out InventoryObjectKind? parsedObjectKind))
        {
            return TypedResults.BadRequest();
        }

        var page = await reader.GetAsync(
            new InventoryPositionQuery(
                search,
                parsedOrigin,
                parsedObjectKind,
                warehouseId,
                storageLocationId,
                includeZeroBalance,
                offset,
                limit,
                localeResolver.Resolve(httpContext.Request)),
            cancellationToken);

        return TypedResults.Ok(new InventoryPositionResponsePage(
            page.Items.Select(item => new InventoryPositionResponse(
                item.Id,
                item.Origin.ToString(),
                item.SourceBatchId,
                item.ObjectKind.ToString(),
                item.ObjectId,
                item.ObjectDisplayName,
                item.WarehouseId,
                item.WarehouseDisplayName,
                item.StorageLocationId,
                item.StorageLocationDisplayName,
                item.RawSourceKind?.ToString(),
                item.SupplierId,
                item.SupplierDisplayName,
                item.BalanceQuantity,
                item.RowVersion)).ToArray(),
            page.NextOffset));
    }

    private static bool TryParseOptionalEnum<TEnum>(string? value, out TEnum? parsed)
        where TEnum : struct, Enum
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!Enum.TryParse<TEnum>(value.Trim(), ignoreCase: true, out var candidate)
            || !Enum.IsDefined(candidate))
        {
            return false;
        }

        parsed = candidate;
        return true;
    }
}

public sealed record InventoryPositionResponse(
    Guid Id,
    string Origin,
    Guid SourceBatchId,
    string ObjectKind,
    Guid ObjectId,
    string ObjectDisplayName,
    Guid WarehouseId,
    string WarehouseDisplayName,
    Guid StorageLocationId,
    string StorageLocationDisplayName,
    string? RawSourceKind,
    Guid? SupplierId,
    string? SupplierDisplayName,
    decimal BalanceQuantity,
    long RowVersion);

public sealed record InventoryPositionResponsePage(
    IReadOnlyList<InventoryPositionResponse> Items,
    int? NextOffset);
