using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Api.Localization;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.DataProtection;

namespace YowThi.Erp.Api.DataProtection;

public static class HardDeleteOptionEndpoints
{
    public const string SuppliersOperationId = "DataProtection_ListHardDeleteSuppliers";
    public const string CustomersOperationId = "DataProtection_ListHardDeleteCustomers";
    public const string OutsourcedVendorsOperationId = "DataProtection_ListHardDeleteOutsourcedVendors";
    public const string FarmersOperationId = "DataProtection_ListHardDeleteFarmers";

    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;

    public static IEndpointRouteBuilder MapHardDeleteOptionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapApiV1().MapGroup("/data-protection/hard-delete-options");

        Map(group, "/suppliers", SuppliersOperationId,
            (reader, query, ct) => reader.GetSuppliersAsync(query, ct));
        Map(group, "/customers", CustomersOperationId,
            (reader, query, ct) => reader.GetCustomersAsync(query, ct));
        Map(group, "/outsourced-vendors", OutsourcedVendorsOperationId,
            (reader, query, ct) => reader.GetOutsourcedVendorsAsync(query, ct));
        Map(group, "/farmers", FarmersOperationId,
            (reader, query, ct) => reader.GetFarmersAsync(query, ct));

        return endpoints;
    }

    private static void Map(
        RouteGroupBuilder group,
        string path,
        string operationId,
        Func<IHardDeleteOptionsReader, HardDeleteOptionsQuery, CancellationToken, ValueTask<HardDeleteOptionPage<HardDeleteOption>>> read)
    {
        group.MapGet(path, async (
                HttpContext context,
                [FromServices] IApiLocaleResolver localeResolver,
                [FromServices] IHardDeleteOptionsReader reader,
                CancellationToken cancellationToken) =>
            {
                if (!TryCreateQuery(context, localeResolver, out var query, out var error)) return error!;
                var page = await read(reader, query!, cancellationToken);
                return TypedResults.Ok(new HardDeleteOptionsResponse(
                    page.Items.Select(item => new HardDeleteOptionResponse(
                        item.Id,
                        item.DisplayName,
                        item.Active,
                        item.RowVersion,
                        item.Deleted,
                        item.DeletedAt)).ToArray(),
                    EncodeCursor(page.NextOffset)));
            })
            .WithName(operationId)
            .RequireAuthorization(CapabilityPolicies.DataProtectionHardDelete)
            .Produces<HardDeleteOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static bool TryCreateQuery(
        HttpContext context,
        IApiLocaleResolver localeResolver,
        out HardDeleteOptionsQuery? query,
        out IResult? error)
    {
        var searches = context.Request.Query["search"];
        var limits = context.Request.Query["limit"];
        var cursors = context.Request.Query["cursor"];
        if (searches.Count > 1 || limits.Count > 1 || cursors.Count > 1)
        {
            query = null;
            error = ValidationProblem(context, "search, limit, and cursor may each be supplied at most once.");
            return false;
        }

        var limit = DefaultLimit;
        if (limits.Count == 1
            && (!int.TryParse(limits[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit)
                || limit is < 1 or > MaxLimit))
        {
            query = null;
            error = ValidationProblem(context, $"limit must be an integer between 1 and {MaxLimit}.");
            return false;
        }

        if (!TryDecodeCursor(cursors.Count == 1 ? cursors[0] : null, out var offset))
        {
            query = null;
            error = ValidationProblem(context, "cursor is invalid.");
            return false;
        }

        var search = searches.Count == 1 ? searches[0] : null;
        query = new HardDeleteOptionsQuery(
            localeResolver.Resolve(context.Request),
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
        if (offset is null) return null;
        return Convert.ToBase64String(
                Encoding.UTF8.GetBytes(offset.Value.ToString(CultureInfo.InvariantCulture)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static IResult ValidationProblem(HttpContext context, string detail) =>
        ApiProblemResults.Create(
            context,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.RequestValidationFailed,
            "Request validation failed.",
            detail);
}

public sealed record HardDeleteOptionResponse(
    Guid Id,
    string DisplayName,
    bool Active,
    long RowVersion,
    bool Deleted,
    DateTimeOffset? DeletedAt);

public sealed record HardDeleteOptionsResponse(
    IReadOnlyList<HardDeleteOptionResponse> Items,
    string? NextCursor);
