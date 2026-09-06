using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Api.Localization;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.Inventory;

namespace YowThi.Erp.Api.Inventory;

public static class InventoryOperationOptionEndpoints
{
    public const string TransferSourcesOperationId = "Inventory_ListTransferSources";
    public const string AdjustmentIdentitiesOperationId = "Inventory_ListAdjustmentIdentities";
    public const string TransferDestinationsOperationId = "Inventory_ListTransferDestinations";
    public const string AdjustmentLocationsOperationId = "Inventory_ListAdjustmentLocations";
    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;

    public static IEndpointRouteBuilder MapInventoryOperationOptionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var inventory = endpoints.MapApiV1().MapGroup("/inventory/operation-options");
        inventory.MapGet("/transfer-sources", ListTransferSourcesAsync)
            .WithName(TransferSourcesOperationId).RequireAuthorization(CapabilityPolicies.InventoryAdjust)
            .Produces<InventoryTransferSourceOptionsResponse>(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403);
        inventory.MapGet("/adjustment-identities", ListAdjustmentIdentitiesAsync)
            .WithName(AdjustmentIdentitiesOperationId).RequireAuthorization(CapabilityPolicies.InventoryAdjust)
            .Produces<InventoryIdentityOptionsResponse>(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403);
        inventory.MapGet("/transfer-destinations", ListTransferDestinationsAsync)
            .WithName(TransferDestinationsOperationId).RequireAuthorization(CapabilityPolicies.InventoryAdjust)
            .Produces<InventoryStorageLocationOptionsResponse>(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403);
        inventory.MapGet("/adjustment-locations", ListAdjustmentLocationsAsync)
            .WithName(AdjustmentLocationsOperationId).RequireAuthorization(CapabilityPolicies.InventoryAdjust)
            .Produces<InventoryStorageLocationOptionsResponse>(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403);
        return endpoints;
    }

    private static async Task<IResult> ListTransferSourcesAsync(
        HttpContext context, [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IInventoryOperationOptionsReader reader, CancellationToken cancellationToken)
    {
        if (!TryQuery(context, localeResolver, out var query, out var error)) return error!;
        var page = await reader.GetTransferSourcesAsync(query!, cancellationToken);
        return TypedResults.Ok(new InventoryTransferSourceOptionsResponse(
            page.Items.Select(item => new InventoryTransferSourceOptionResponse(
                item.InventoryPositionId, ToResponse(item.InventoryIdentity), item.SourceStorageLocationId,
                item.SourceStorageLocationDisplayName, item.SourceStorageLocationActive, item.BalanceQuantity)).ToArray(),
            EncodeCursor(page.NextOffset)));
    }

    private static async Task<IResult> ListAdjustmentIdentitiesAsync(
        HttpContext context, [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IInventoryOperationOptionsReader reader, CancellationToken cancellationToken)
    {
        if (!TryQuery(context, localeResolver, out var query, out var error)) return error!;
        var page = await reader.GetAdjustmentIdentitiesAsync(query!, cancellationToken);
        return TypedResults.Ok(new InventoryIdentityOptionsResponse(page.Items.Select(ToResponse).ToArray(), EncodeCursor(page.NextOffset)));
    }

    private static async Task<IResult> ListTransferDestinationsAsync(
        HttpContext context, [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IInventoryOperationOptionsReader reader, CancellationToken cancellationToken)
    {
        if (!TryQuery(context, localeResolver, out var query, out var error)) return error!;
        return TypedResults.Ok(ToLocationResponse(await reader.GetTransferDestinationsAsync(query!, cancellationToken)));
    }

    private static async Task<IResult> ListAdjustmentLocationsAsync(
        HttpContext context, [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IInventoryOperationOptionsReader reader, CancellationToken cancellationToken)
    {
        if (!TryQuery(context, localeResolver, out var query, out var error)) return error!;
        return TypedResults.Ok(ToLocationResponse(await reader.GetAdjustmentLocationsAsync(query!, cancellationToken)));
    }

    private static InventoryStorageLocationOptionsResponse ToLocationResponse(
        InventoryOperationOptionPage<InventoryStorageLocationOption> page) =>
        new(page.Items.Select(item => new InventoryStorageLocationOptionResponse(item.Id, item.DisplayName, item.Active)).ToArray(), EncodeCursor(page.NextOffset));

    private static InventoryOperationIdentityOptionResponse ToResponse(InventoryOperationIdentityOption item) =>
        new(item.Origin.ToString(), item.ProcurementBatchId, item.OutsourcedSupplyBatchId, item.BatchDate,
            item.InventoryObjectKind.ToString(), item.ProcurementProductId, item.ProcessMaterialId, item.SalesProductId,
            item.ObjectDisplayName, item.RawSourceKind?.ToString(), item.SupplierId, item.RawSourceDisplayName);

    private static bool TryQuery(HttpContext context, IApiLocaleResolver localeResolver,
        out InventoryOperationOptionsQuery? query, out IResult? error)
    {
        var searches = context.Request.Query["search"];
        var limits = context.Request.Query["limit"];
        var cursors = context.Request.Query["cursor"];
        if (searches.Count > 1 || limits.Count > 1 || cursors.Count > 1)
        {
            query = null; error = ValidationProblem(context, "search, limit, and cursor may each be supplied at most once."); return false;
        }
        var limit = DefaultLimit;
        if (limits.Count == 1 && (!int.TryParse(limits[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > MaxLimit))
        {
            query = null; error = ValidationProblem(context, $"limit must be an integer between 1 and {MaxLimit}."); return false;
        }
        if (!TryDecodeCursor(cursors.Count == 1 ? cursors[0] : null, out var offset))
        {
            query = null; error = ValidationProblem(context, "cursor is invalid."); return false;
        }
        var search = searches.Count == 1 ? searches[0] : null;
        query = new InventoryOperationOptionsQuery(localeResolver.Resolve(context.Request), string.IsNullOrWhiteSpace(search) ? null : search.Trim(), offset, limit);
        error = null; return true;
    }

    private static bool TryDecodeCursor(string? cursor, out int offset)
    {
        offset = 0;
        if (string.IsNullOrWhiteSpace(cursor)) return true;
        try
        {
            var normalized = cursor.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            return int.TryParse(Encoding.UTF8.GetString(Convert.FromBase64String(normalized)), NumberStyles.None, CultureInfo.InvariantCulture, out offset) && offset >= 0;
        }
        catch (FormatException) { return false; }
    }

    private static string? EncodeCursor(int? offset) => offset is null ? null :
        Convert.ToBase64String(Encoding.UTF8.GetBytes(offset.Value.ToString(CultureInfo.InvariantCulture))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static IResult ValidationProblem(HttpContext context, string detail) =>
        ApiProblemResults.Create(context, 400, ApiErrorCodes.RequestValidationFailed, "Request validation failed.", detail);
}

public sealed record InventoryOperationIdentityOptionResponse(
    string Origin, Guid? ProcurementBatchId, Guid? OutsourcedSupplyBatchId, DateOnly BatchDate,
    string InventoryObjectKind, Guid? ProcurementProductId, Guid? ProcessMaterialId, Guid? SalesProductId,
    string ObjectDisplayName, string? RawSourceKind, Guid? SupplierId, string? RawSourceDisplayName);
public sealed record InventoryTransferSourceOptionResponse(
    Guid InventoryPositionId, InventoryOperationIdentityOptionResponse InventoryIdentity, Guid SourceStorageLocationId,
    string SourceStorageLocationDisplayName, bool SourceStorageLocationActive, decimal BalanceQuantity);
public sealed record InventoryStorageLocationOptionResponse(Guid Id, string DisplayName, bool Active);
public sealed record InventoryTransferSourceOptionsResponse(IReadOnlyList<InventoryTransferSourceOptionResponse> Items, string? NextCursor);
public sealed record InventoryIdentityOptionsResponse(IReadOnlyList<InventoryOperationIdentityOptionResponse> Items, string? NextCursor);
public sealed record InventoryStorageLocationOptionsResponse(IReadOnlyList<InventoryStorageLocationOptionResponse> Items, string? NextCursor);
