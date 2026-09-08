using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Api.Localization;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.Procurement;
using YowThi.Erp.Domain.Procurement;

namespace YowThi.Erp.Api.Procurement;

public static class ProcurementEntryOptionEndpoints
{
    public const string ProductsOperationId = "Procurement_ListEntryProductOptions";
    public const string SourcesOperationId = "Procurement_ListEntrySourceOptions";
    public const string StorageLocationsOperationId = "Procurement_ListEntryStorageLocationOptions";

    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;

    public static IEndpointRouteBuilder MapProcurementEntryOptionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var procurement = endpoints.MapApiV1().MapGroup("/procurement");

        procurement.MapGet("/entry-options/products", ListProductsAsync)
            .WithName(ProductsOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcurementConfirm)
            .Produces<ProcurementProductOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        procurement.MapGet("/entry-options/sources", ListSourcesAsync)
            .WithName(SourcesOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcurementConfirm)
            .Produces<ProcurementSourceOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        procurement.MapGet("/entry-options/storage-locations", ListStorageLocationsAsync)
            .WithName(StorageLocationsOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcurementConfirm)
            .Produces<ProcurementReceiptStorageLocationOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ListProductsAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IProcurementEntryOptionsReader reader,
        CancellationToken cancellationToken)
    {
        if (!TryCreateQuery(httpContext, localeResolver, out var query, out var error))
        {
            return error!;
        }

        var page = await reader.GetProductsAsync(query!, cancellationToken);
        return TypedResults.Ok(new ProcurementProductOptionsResponse(
            page.Items
                .Select(item => new ProcurementProductOptionResponse(item.Id, item.DisplayName, item.UnitCode))
                .ToArray(),
            EncodeCursor(page.NextOffset)));
    }

    private static async Task<IResult> ListSourcesAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IProcurementEntryOptionsReader reader,
        CancellationToken cancellationToken)
    {
        if (!TryParseSourceType(httpContext, out var sourceType, out var sourceError))
        {
            return sourceError!;
        }

        if (!TryCreateQuery(httpContext, localeResolver, out var query, out var error))
        {
            return error!;
        }

        var page = await reader.GetSourcesAsync(sourceType, query!, cancellationToken);
        return TypedResults.Ok(new ProcurementSourceOptionsResponse(
            sourceType.ToString(),
            page.Items
                .Select(item => new ProcurementSourceOptionResponse(item.Id, item.Code, item.DisplayName))
                .ToArray(),
            EncodeCursor(page.NextOffset)));
    }

    private static async Task<IResult> ListStorageLocationsAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IProcurementEntryOptionsReader reader,
        CancellationToken cancellationToken)
    {
        if (!TryParseRequiredGuid(httpContext, "procurementProductId", out var procurementProductId, out var idError))
        {
            return idError!;
        }

        if (!TryCreateQuery(httpContext, localeResolver, out var query, out var error))
        {
            return error!;
        }

        var result = await reader.GetReceiptStorageLocationsAsync(procurementProductId, query!, cancellationToken);
        if (result is null)
        {
            return ApiProblemResults.Create(
                httpContext,
                StatusCodes.Status404NotFound,
                "resource.not-found",
                "Procurement Product was not found in the current-use view.");
        }

        return TypedResults.Ok(new ProcurementReceiptStorageLocationOptionsResponse(
            result.ProcurementProductId,
            result.DefaultStorageLocationId,
            result.Locations.Items
                .Select(item => new ProcurementReceiptStorageLocationOptionResponse(
                    item.Id,
                    item.DisplayName,
                    item.Code,
                    item.WarehouseId,
                    item.IsProductDefault))
                .ToArray(),
            EncodeCursor(result.Locations.NextOffset)));
    }

    private static bool TryCreateQuery(
        HttpContext httpContext,
        IApiLocaleResolver localeResolver,
        out ProcurementEntryOptionsQuery? query,
        out IResult? error)
    {
        if (!TryReadSingleQueryValue(httpContext, "search", required: false, out var rawSearch, out error))
        {
            query = null;
            return false;
        }

        if (!TryReadSingleQueryValue(httpContext, "limit", required: false, out var rawLimit, out error))
        {
            query = null;
            return false;
        }

        if (!TryReadSingleQueryValue(httpContext, "cursor", required: false, out var rawCursor, out error))
        {
            query = null;
            return false;
        }

        var limit = DefaultLimit;
        if (!string.IsNullOrWhiteSpace(rawLimit)
            && (!int.TryParse(rawLimit, NumberStyles.None, CultureInfo.InvariantCulture, out limit)
                || limit is < 1 or > MaxLimit))
        {
            query = null;
            error = ValidationProblem(httpContext, $"limit must be an integer between 1 and {MaxLimit}.");
            return false;
        }

        if (!TryDecodeCursor(rawCursor, out var offset))
        {
            query = null;
            error = ValidationProblem(httpContext, "cursor is invalid.");
            return false;
        }

        query = new ProcurementEntryOptionsQuery(
            localeResolver.Resolve(httpContext.Request),
            string.IsNullOrWhiteSpace(rawSearch) ? null : rawSearch.Trim(),
            offset,
            limit);
        error = null;
        return true;
    }

    private static bool TryParseSourceType(
        HttpContext httpContext,
        out ProcurementSourceType sourceType,
        out IResult? error)
    {
        if (!TryReadSingleQueryValue(httpContext, "sourceType", required: true, out var raw, out error))
        {
            sourceType = default;
            return false;
        }

        if (!Enum.TryParse(raw, ignoreCase: false, out sourceType)
            || sourceType is not (ProcurementSourceType.SUPPLIER or ProcurementSourceType.FARMER))
        {
            error = ValidationProblem(httpContext, "sourceType must be SUPPLIER or FARMER.");
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryParseRequiredGuid(
        HttpContext httpContext,
        string name,
        out Guid value,
        out IResult? error)
    {
        if (!TryReadSingleQueryValue(httpContext, name, required: true, out var raw, out error))
        {
            value = Guid.Empty;
            return false;
        }

        if (!Guid.TryParse(raw, out value) || value == Guid.Empty)
        {
            error = ValidationProblem(httpContext, $"{name} must be a non-empty UUID.");
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryReadSingleQueryValue(
        HttpContext httpContext,
        string name,
        bool required,
        out string value,
        out IResult? error)
    {
        var values = httpContext.Request.Query[name];
        if (values.Count > 1)
        {
            value = string.Empty;
            error = ValidationProblem(httpContext, $"{name} must be supplied at most once.");
            return false;
        }

        value = values.Count == 1 ? values[0] ?? string.Empty : string.Empty;
        if (required && string.IsNullOrWhiteSpace(value))
        {
            error = ValidationProblem(httpContext, $"{name} is required.");
            return false;
        }

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
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
            return int.TryParse(decoded, NumberStyles.None, CultureInfo.InvariantCulture, out offset)
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

        var raw = Encoding.UTF8.GetBytes(offset.Value.ToString(CultureInfo.InvariantCulture));
        return Convert.ToBase64String(raw)
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

public sealed record ProcurementProductOptionResponse(
    Guid Id,
    string DisplayName,
    string UnitCode);

public sealed record ProcurementProductOptionsResponse(
    IReadOnlyList<ProcurementProductOptionResponse> Items,
    string? NextCursor);

public sealed record ProcurementSourceOptionResponse(
    Guid Id,
    string? Code,
    string DisplayName);

public sealed record ProcurementSourceOptionsResponse(
    string SourceType,
    IReadOnlyList<ProcurementSourceOptionResponse> Items,
    string? NextCursor);

public sealed record ProcurementReceiptStorageLocationOptionResponse(
    Guid Id,
    string DisplayName,
    string? Code,
    Guid WarehouseId,
    bool IsProductDefault);

public sealed record ProcurementReceiptStorageLocationOptionsResponse(
    Guid ProcurementProductId,
    Guid? DefaultStorageLocationId,
    IReadOnlyList<ProcurementReceiptStorageLocationOptionResponse> Items,
    string? NextCursor);
