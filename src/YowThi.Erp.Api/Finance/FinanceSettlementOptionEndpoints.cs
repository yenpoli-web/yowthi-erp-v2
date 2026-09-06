using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Api.Localization;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.Finance;

namespace YowThi.Erp.Api.Finance;

public static class FinanceSettlementOptionEndpoints
{
    public const string PayablesOperationId = "Finance_ListSettlementPayables";
    public const string ReceivablesOperationId = "Finance_ListSettlementReceivables";

    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;

    public static IEndpointRouteBuilder MapFinanceSettlementOptionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var finance = endpoints.MapApiV1().MapGroup("/finance");

        finance.MapGet("/settlement-options/payables", ListPayablesAsync)
            .WithName(PayablesOperationId)
            .RequireAuthorization(CapabilityPolicies.FinancePay)
            .Produces<FinancePayableSettlementOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        finance.MapGet("/settlement-options/receivables", ListReceivablesAsync)
            .WithName(ReceivablesOperationId)
            .RequireAuthorization(CapabilityPolicies.FinancePay)
            .Produces<FinanceReceivableSettlementOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    private static async Task<IResult> ListPayablesAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IFinanceSettlementOptionsReader reader,
        CancellationToken cancellationToken)
    {
        if (!TryCreateQuery(httpContext, localeResolver, out var query, out var error))
        {
            return error!;
        }

        var page = await reader.GetPayablesAsync(query!, cancellationToken);
        return TypedResults.Ok(new FinancePayableSettlementOptionsResponse(
            page.Items.Select(item => new FinancePayableSettlementOptionResponse(
                item.PayableId,
                item.PayableKind,
                item.SourceDisplayName,
                item.OutstandingThb,
                item.OutstandingVersion,
                item.UpdatedAt)).ToArray(),
            EncodeCursor(page.NextOffset)));
    }

    private static async Task<IResult> ListReceivablesAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IFinanceSettlementOptionsReader reader,
        CancellationToken cancellationToken)
    {
        if (!TryCreateQuery(httpContext, localeResolver, out var query, out var error))
        {
            return error!;
        }

        var page = await reader.GetReceivablesAsync(query!, cancellationToken);
        return TypedResults.Ok(new FinanceReceivableSettlementOptionsResponse(
            page.Items.Select(item => new FinanceReceivableSettlementOptionResponse(
                item.ReceivableId,
                item.SalesId,
                item.SalesDate,
                item.CustomerDisplayName,
                item.OutstandingThb,
                item.OutstandingVersion,
                item.UpdatedAt)).ToArray(),
            EncodeCursor(page.NextOffset)));
    }

    private static bool TryCreateQuery(
        HttpContext httpContext,
        IApiLocaleResolver localeResolver,
        out FinanceSettlementOptionsQuery? query,
        out IResult? error)
    {
        var rawSearch = httpContext.Request.Query["search"];
        var rawLimit = httpContext.Request.Query["limit"];
        var rawCursor = httpContext.Request.Query["cursor"];
        if (rawSearch.Count > 1 || rawLimit.Count > 1 || rawCursor.Count > 1)
        {
            query = null;
            error = ValidationProblem(httpContext, "search, limit, and cursor may each be supplied at most once.");
            return false;
        }

        var limit = DefaultLimit;
        if (rawLimit.Count == 1
            && (!int.TryParse(rawLimit[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit)
                || limit is < 1 or > MaxLimit))
        {
            query = null;
            error = ValidationProblem(httpContext, $"limit must be an integer between 1 and {MaxLimit}.");
            return false;
        }

        if (!TryDecodeCursor(rawCursor.Count == 1 ? rawCursor[0] : null, out var offset))
        {
            query = null;
            error = ValidationProblem(httpContext, "cursor is invalid.");
            return false;
        }

        var search = rawSearch.Count == 1 ? rawSearch[0] : null;
        query = new FinanceSettlementOptionsQuery(
            localeResolver.Resolve(httpContext.Request),
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

    private static IResult ValidationProblem(HttpContext httpContext, string detail) =>
        ApiProblemResults.Create(
            httpContext,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.RequestValidationFailed,
            "Request validation failed.",
            detail);
}

public sealed record FinancePayableSettlementOptionResponse(
    Guid PayableId,
    string PayableKind,
    string SourceDisplayName,
    long OutstandingThb,
    long OutstandingVersion,
    DateTimeOffset UpdatedAt);

public sealed record FinancePayableSettlementOptionsResponse(
    IReadOnlyList<FinancePayableSettlementOptionResponse> Items,
    string? NextCursor);

public sealed record FinanceReceivableSettlementOptionResponse(
    Guid ReceivableId,
    Guid SalesId,
    DateOnly SalesDate,
    string CustomerDisplayName,
    long OutstandingThb,
    long OutstandingVersion,
    DateTimeOffset UpdatedAt);

public sealed record FinanceReceivableSettlementOptionsResponse(
    IReadOnlyList<FinanceReceivableSettlementOptionResponse> Items,
    string? NextCursor);
