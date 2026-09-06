using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Api.Localization;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.Party;

namespace YowThi.Erp.Api.Party;

public static class PartyLifecycleOptionEndpoints
{
    public const string SuppliersOperationId = "Party_ListSupplierLifecycleOptions";
    public const string CustomersOperationId = "Party_ListCustomerLifecycleOptions";
    public const string OutsourcedVendorsOperationId = "Party_ListOutsourcedVendorLifecycleOptions";
    public const string FarmersOperationId = "Party_ListFarmerLifecycleOptions";
    public const string EmployeesOperationId = "Party_ListEmployeeLifecycleOptions";

    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;

    public static IEndpointRouteBuilder MapPartyLifecycleOptionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var party = endpoints.MapApiV1().MapGroup("/party/lifecycle-options");

        party.MapGet("/suppliers", ListSuppliersAsync)
            .WithName(SuppliersOperationId)
            .RequireAuthorization(CapabilityPolicies.SupplierLifecycle)
            .Produces<PartyLifecycleOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        party.MapGet("/customers", ListCustomersAsync)
            .WithName(CustomersOperationId)
            .RequireAuthorization(CapabilityPolicies.CustomerLifecycle)
            .Produces<PartyLifecycleOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        party.MapGet("/outsourced-vendors", ListOutsourcedVendorsAsync)
            .WithName(OutsourcedVendorsOperationId)
            .RequireAuthorization(CapabilityPolicies.OutsourcedVendorLifecycle)
            .Produces<PartyLifecycleOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        party.MapGet("/farmers", ListFarmersAsync)
            .WithName(FarmersOperationId)
            .RequireAuthorization(CapabilityPolicies.FarmerLifecycle)
            .Produces<PartyLifecycleOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        party.MapGet("/employees", ListEmployeesAsync)
            .WithName(EmployeesOperationId)
            .RequireAuthorization(CapabilityPolicies.EmployeeLifecycle)
            .Produces<PartyLifecycleOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    private static Task<IResult> ListSuppliersAsync(
        HttpContext context,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IPartyLifecycleOptionsReader reader,
        CancellationToken cancellationToken) =>
        ListAsync(context, localeResolver, reader.GetSuppliersAsync, cancellationToken);

    private static Task<IResult> ListCustomersAsync(
        HttpContext context,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IPartyLifecycleOptionsReader reader,
        CancellationToken cancellationToken) =>
        ListAsync(context, localeResolver, reader.GetCustomersAsync, cancellationToken);

    private static Task<IResult> ListOutsourcedVendorsAsync(
        HttpContext context,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IPartyLifecycleOptionsReader reader,
        CancellationToken cancellationToken) =>
        ListAsync(context, localeResolver, reader.GetOutsourcedVendorsAsync, cancellationToken);

    private static Task<IResult> ListFarmersAsync(
        HttpContext context,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IPartyLifecycleOptionsReader reader,
        CancellationToken cancellationToken) =>
        ListAsync(context, localeResolver, reader.GetFarmersAsync, cancellationToken);

    private static Task<IResult> ListEmployeesAsync(
        HttpContext context,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IPartyLifecycleOptionsReader reader,
        CancellationToken cancellationToken) =>
        ListAsync(context, localeResolver, reader.GetEmployeesAsync, cancellationToken);

    private static async Task<IResult> ListAsync(
        HttpContext context,
        IApiLocaleResolver localeResolver,
        Func<PartyLifecycleOptionsQuery, CancellationToken, ValueTask<PartyLifecycleOptionPage<PartyLifecycleOption>>> read,
        CancellationToken cancellationToken)
    {
        if (!TryCreateQuery(context, localeResolver, out var query, out var error))
        {
            return error!;
        }

        var page = await read(query!, cancellationToken);
        return TypedResults.Ok(new PartyLifecycleOptionsResponse(
            page.Items.Select(item => new PartyLifecycleOptionResponse(
                item.Id,
                item.DisplayName,
                item.Active,
                item.RowVersion,
                item.Deleted,
                item.DeletedAt)).ToArray(),
            EncodeCursor(page.NextOffset)));
    }

    private static bool TryCreateQuery(
        HttpContext context,
        IApiLocaleResolver localeResolver,
        out PartyLifecycleOptionsQuery? query,
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
        query = new PartyLifecycleOptionsQuery(
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

public sealed record PartyLifecycleOptionResponse(
    Guid Id,
    string DisplayName,
    bool Active,
    long RowVersion,
    bool Deleted,
    DateTimeOffset? DeletedAt);

public sealed record PartyLifecycleOptionsResponse(
    IReadOnlyList<PartyLifecycleOptionResponse> Items,
    string? NextCursor);
