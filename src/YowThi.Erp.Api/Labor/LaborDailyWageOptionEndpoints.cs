using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Api.Localization;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.Labor;

namespace YowThi.Erp.Api.Labor;

public static class LaborDailyWageOptionEndpoints
{
    public const string EmployeesOperationId = "Labor_ListDailyWageEmployeeOptions";
    public const string WorkspaceOperationId = "Labor_GetDailyWageWorkspace";
    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;

    public static IEndpointRouteBuilder MapLaborDailyWageOptionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var labor = endpoints.MapApiV1().MapGroup("/labor");

        labor.MapGet("/daily-wage-options/employees", ListEmployeesAsync)
            .WithName(EmployeesOperationId)
            .RequireAuthorization(CapabilityPolicies.LaborDailyWageConfirm)
            .Produces<LaborDailyWageEmployeeOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        labor.MapGet("/employees/{employeeId:guid}/daily-wage-workspace", GetWorkspaceAsync)
            .WithName(WorkspaceOperationId)
            .RequireAuthorization(CapabilityPolicies.LaborDailyWageConfirm)
            .Produces<LaborDailyWageWorkspaceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ListEmployeesAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] ILaborDailyWageOptionsReader reader,
        CancellationToken cancellationToken)
    {
        if (!TryCreateQuery(httpContext, localeResolver, out var query, out var error))
        {
            return error!;
        }

        var page = await reader.GetEmployeesAsync(query!, cancellationToken);
        return TypedResults.Ok(new LaborDailyWageEmployeeOptionsResponse(
            page.Items.Select(item => new LaborDailyWageEmployeeOptionResponse(item.Id, item.DisplayName)).ToArray(),
            EncodeCursor(page.NextOffset)));
    }

    private static async Task<IResult> GetWorkspaceAsync(
        Guid employeeId,
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] ILaborDailyWageOptionsReader reader,
        CancellationToken cancellationToken)
    {
        var values = httpContext.Request.Query["workDate"];
        if (values.Count != 1 || !DateOnly.TryParseExact(values[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var workDate))
        {
            return ValidationProblem(httpContext, "workDate must be supplied exactly once as yyyy-MM-dd.");
        }

        var workspace = await reader.GetWorkspaceAsync(
            employeeId,
            workDate,
            localeResolver.Resolve(httpContext.Request),
            cancellationToken);
        if (workspace is null)
        {
            return ApiProblemResults.Create(
                httpContext,
                StatusCodes.Status404NotFound,
                "labor.employee-not-found",
                "Active Employee was not found in the daily wage workspace.");
        }

        return TypedResults.Ok(new LaborDailyWageWorkspaceResponse(
            workspace.EmployeeId,
            workspace.WorkDate,
            workspace.EmployeeDisplayName,
            workspace.AlreadyConfirmed,
            workspace.ProcessingTargets.Select(item => new LaborProcessingWageTargetResponse(
                item.ProcessingModuleOutputId,
                item.DisplayName,
                item.ConfiguredWageRateSnapshot,
                item.AggregatedQuantity)).ToArray(),
            workspace.SalesPackagingWorkRecordCount,
            workspace.SalesPackagingWageTotalThb));
    }

    private static bool TryCreateQuery(
        HttpContext httpContext,
        IApiLocaleResolver localeResolver,
        out LaborDailyWageOptionsQuery? query,
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
        query = new LaborDailyWageOptionsQuery(
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
            return int.TryParse(Encoding.UTF8.GetString(Convert.FromBase64String(normalized)), NumberStyles.None, CultureInfo.InvariantCulture, out offset)
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
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(offset.Value.ToString(CultureInfo.InvariantCulture)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static IResult ValidationProblem(HttpContext httpContext, string detail) =>
        ApiProblemResults.Create(
            httpContext,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.RequestValidationFailed,
            "Request validation failed.",
            detail);
}

public sealed record LaborDailyWageEmployeeOptionResponse(Guid Id, string DisplayName);
public sealed record LaborDailyWageEmployeeOptionsResponse(IReadOnlyList<LaborDailyWageEmployeeOptionResponse> Items, string? NextCursor);
public sealed record LaborProcessingWageTargetResponse(Guid ProcessingModuleOutputId, string DisplayName, decimal ConfiguredWageRateSnapshot, decimal AggregatedQuantity);
public sealed record LaborDailyWageWorkspaceResponse(Guid EmployeeId, DateOnly WorkDate, string EmployeeDisplayName, bool AlreadyConfirmed, IReadOnlyList<LaborProcessingWageTargetResponse> ProcessingTargets, int SalesPackagingWorkRecordCount, long SalesPackagingWageTotalThb);
