using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Api.Localization;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.Outsourced;

namespace YowThi.Erp.Api.Outsourced;

public static class OutsourcedSupplyDetailOptionEndpoints
{
    public const string VendorsOperationId = "Outsourced_ListSupplyDetailVendorOptions";
    public const string ProductsOperationId = "Outsourced_ListSupplyDetailProductOptions";
    public const string StorageLocationsOperationId = "Outsourced_ListSupplyDetailStorageLocationOptions";

    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;

    public static IEndpointRouteBuilder MapOutsourcedSupplyDetailOptionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var outsourced = endpoints.MapApiV1().MapGroup("/outsourced");

        outsourced.MapGet("/supply-detail-options/vendors", ListVendorsAsync)
            .WithName(VendorsOperationId)
            .RequireAuthorization(CapabilityPolicies.OutsourcedConfirm)
            .Produces<OutsourcedVendorOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        outsourced.MapGet("/supply-detail-options/products", ListProductsAsync)
            .WithName(ProductsOperationId)
            .RequireAuthorization(CapabilityPolicies.OutsourcedConfirm)
            .Produces<OutsourcedSalesProductOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        outsourced.MapGet("/supply-detail-options/storage-locations", ListStorageLocationsAsync)
            .WithName(StorageLocationsOperationId)
            .RequireAuthorization(CapabilityPolicies.OutsourcedConfirm)
            .Produces<OutsourcedReceiptStorageLocationOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ListVendorsAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IOutsourcedSupplyDetailOptionsReader reader,
        CancellationToken cancellationToken)
    {
        if (!TryCreateQuery(httpContext, localeResolver, out var query, out var error))
        {
            return error!;
        }

        var page = await reader.GetVendorsAsync(query!, cancellationToken);
        return TypedResults.Ok(new OutsourcedVendorOptionsResponse(
            page.Items.Select(item => new OutsourcedVendorOptionResponse(item.Id, item.DisplayName)).ToArray(),
            EncodeCursor(page.NextOffset)));
    }

    private static async Task<IResult> ListProductsAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IOutsourcedSupplyDetailOptionsReader reader,
        CancellationToken cancellationToken)
    {
        if (!TryCreateQuery(httpContext, localeResolver, out var query, out var error))
        {
            return error!;
        }

        var page = await reader.GetProductsAsync(query!, cancellationToken);
        return TypedResults.Ok(new OutsourcedSalesProductOptionsResponse(
            page.Items.Select(item => new OutsourcedSalesProductOptionResponse(
                item.Id,
                item.DisplayName,
                item.PricingBasis.ToString())).ToArray(),
            EncodeCursor(page.NextOffset)));
    }

    private static async Task<IResult> ListStorageLocationsAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IOutsourcedSupplyDetailOptionsReader reader,
        CancellationToken cancellationToken)
    {
        if (!TryParseRequiredGuid(httpContext, "salesProductId", out var salesProductId, out var idError))
        {
            return idError!;
        }

        if (!TryCreateQuery(httpContext, localeResolver, out var query, out var error))
        {
            return error!;
        }

        var result = await reader.GetReceiptStorageLocationsAsync(salesProductId, query!, cancellationToken);
        if (result is null)
        {
            return ApiProblemResults.Create(
                httpContext,
                StatusCodes.Status404NotFound,
                "resource.not-found",
                "Sales Product was not found in the current-use view.");
        }

        return TypedResults.Ok(new OutsourcedReceiptStorageLocationOptionsResponse(
            result.SalesProductId,
            result.DefaultStorageLocationId,
            result.Locations.Items.Select(item => new OutsourcedReceiptStorageLocationOptionResponse(
                item.Id,
                item.DisplayName,
                item.Code,
                item.WarehouseId,
                item.IsProductDefault)).ToArray(),
            EncodeCursor(result.Locations.NextOffset)));
    }

    private static bool TryCreateQuery(
        HttpContext httpContext,
        IApiLocaleResolver localeResolver,
        out OutsourcedSupplyDetailOptionsQuery? query,
        out IResult? error)
    {
        if (!TryReadSingleQueryValue(httpContext, "search", required: false, out var rawSearch, out error)
            || !TryReadSingleQueryValue(httpContext, "limit", required: false, out var rawLimit, out error)
            || !TryReadSingleQueryValue(httpContext, "cursor", required: false, out var rawCursor, out error))
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

        query = new OutsourcedSupplyDetailOptionsQuery(
            localeResolver.Resolve(httpContext.Request),
            string.IsNullOrWhiteSpace(rawSearch) ? null : rawSearch.Trim(),
            offset,
            limit);
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

public sealed record OutsourcedVendorOptionResponse(Guid Id, string DisplayName);
public sealed record OutsourcedVendorOptionsResponse(IReadOnlyList<OutsourcedVendorOptionResponse> Items, string? NextCursor);
public sealed record OutsourcedSalesProductOptionResponse(Guid Id, string DisplayName, string PricingBasis);
public sealed record OutsourcedSalesProductOptionsResponse(IReadOnlyList<OutsourcedSalesProductOptionResponse> Items, string? NextCursor);
public sealed record OutsourcedReceiptStorageLocationOptionResponse(
    Guid Id,
    string DisplayName,
    string? Code,
    Guid WarehouseId,
    bool IsProductDefault);
public sealed record OutsourcedReceiptStorageLocationOptionsResponse(
    Guid SalesProductId,
    Guid? DefaultStorageLocationId,
    IReadOnlyList<OutsourcedReceiptStorageLocationOptionResponse> Items,
    string? NextCursor);
