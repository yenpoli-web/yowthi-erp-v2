using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Api.Localization;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.Processing;

namespace YowThi.Erp.Api.Processing;

public static class ProcessingExecutionOptionEndpoints
{
    public const string EmployeesOperationId = "Processing_ListExecutionEmployeeOptions";
    public const string BatchesOperationId = "Processing_ListExecutionBatchOptions";
    public const string ModulesOperationId = "Processing_ListExecutionModuleOptions";
    public const string SuppliersOperationId = "Processing_ListExecutionSupplierOptions";
    public const string InputStorageLocationsOperationId = "Processing_ListExecutionInputStorageLocationOptions";
    public const string ModuleOutputsOperationId = "Processing_ListExecutionModuleOutputOptions";
    public const string StorageLocationsOperationId = "Processing_ListExecutionStorageLocationOptions";

    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;

    public static IEndpointRouteBuilder MapProcessingExecutionOptionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var processing = endpoints.MapApiV1().MapGroup("/processing");

        processing.MapGet("/execution-options/employees", ListEmployeesAsync)
            .WithName(EmployeesOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcessingConfirm)
            .Produces<ProcessingEmployeeOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        processing.MapGet("/execution-options/batches", ListBatchesAsync)
            .WithName(BatchesOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcessingConfirm)
            .Produces<ProcessingBatchOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        processing.MapGet("/execution-options/modules", ListModulesAsync)
            .WithName(ModulesOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcessingConfirm)
            .Produces<ProcessingBatchModuleOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        processing.MapGet("/execution-options/suppliers", ListSuppliersAsync)
            .WithName(SuppliersOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcessingConfirm)
            .Produces<ProcessingSupplierOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        processing.MapGet("/execution-options/input-storage-locations", ListInputStorageLocationsAsync)
            .WithName(InputStorageLocationsOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcessingConfirm)
            .Produces<ProcessingInputStorageLocationOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        processing.MapGet("/execution-options/module-outputs", ListModuleOutputsAsync)
            .WithName(ModuleOutputsOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcessingConfirm)
            .Produces<ProcessingModuleOutputOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        processing.MapGet("/execution-options/storage-locations", ListStorageLocationsAsync)
            .WithName(StorageLocationsOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcessingConfirm)
            .Produces<ProcessingStorageLocationOptionsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    private static async Task<IResult> ListEmployeesAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IProcessingExecutionOptionsReader reader,
        CancellationToken cancellationToken)
    {
        if (!TryCreateQuery(httpContext, localeResolver, out var query, out var error)) return error!;
        var page = await reader.GetEmployeesAsync(query!, cancellationToken);
        return TypedResults.Ok(new ProcessingEmployeeOptionsResponse(
            page.Items.Select(item => new ProcessingEmployeeOptionResponse(item.Id, item.DisplayName)).ToArray(),
            EncodeCursor(page.NextOffset)));
    }

    private static async Task<IResult> ListBatchesAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IProcessingExecutionOptionsReader reader,
        CancellationToken cancellationToken)
    {
        if (!TryCreateQuery(httpContext, localeResolver, out var query, out var error)) return error!;
        var page = await reader.GetBatchesAsync(query!, cancellationToken);
        return TypedResults.Ok(new ProcessingBatchOptionsResponse(
            page.Items.Select(item => new ProcessingBatchOptionResponse(
                item.Id,
                item.ProcurementDate,
                item.ProcurementProductId,
                item.ProcurementProductDisplayName,
                item.ProcessingRouteVersionId)).ToArray(),
            EncodeCursor(page.NextOffset)));
    }

    private static async Task<IResult> ListModulesAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IProcessingExecutionOptionsReader reader,
        CancellationToken cancellationToken)
    {
        if (!TryParseRequiredGuid(httpContext, "procurementBatchId", out var batchId, out var idError)) return idError!;
        if (!TryCreateQuery(httpContext, localeResolver, out var query, out var error)) return error!;

        var result = await reader.GetModulesAsync(batchId, query!, cancellationToken);
        if (result is null) return NotFound(httpContext, "Processing batch was not found in the executable current-use view.");

        return TypedResults.Ok(new ProcessingBatchModuleOptionsResponse(
            result.ProcurementBatchId,
            result.ProcessingRouteVersionId,
            result.Modules.Items.Select(item => new ProcessingModuleOptionResponse(
                item.Id,
                item.DisplayName,
                item.ExecutionMode.ToString(),
                item.InputProcessMaterialId,
                item.InputUsesContainer,
                item.InputContainerId,
                item.DefaultInputContainerCount)).ToArray(),
            EncodeCursor(result.Modules.NextOffset)));
    }

    private static async Task<IResult> ListSuppliersAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IProcessingExecutionOptionsReader reader,
        CancellationToken cancellationToken)
    {
        if (!TryCreateQuery(httpContext, localeResolver, out var query, out var error)) return error!;
        var page = await reader.GetSuppliersAsync(query!, cancellationToken);
        return TypedResults.Ok(new ProcessingSupplierOptionsResponse(
            page.Items.Select(item => new ProcessingSupplierOptionResponse(item.Id, item.DisplayName)).ToArray(),
            EncodeCursor(page.NextOffset)));
    }

    private static async Task<IResult> ListInputStorageLocationsAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IProcessingExecutionOptionsReader reader,
        CancellationToken cancellationToken)
    {
        if (!TryParseRequiredGuid(httpContext, "procurementBatchId", out var batchId, out var batchError)) return batchError!;
        if (!TryParseRequiredGuid(httpContext, "processingModuleId", out var moduleId, out var moduleError)) return moduleError!;
        if (!TryCreateQuery(httpContext, localeResolver, out var query, out var error)) return error!;

        var result = await reader.GetInputStorageLocationsAsync(batchId, moduleId, query!, cancellationToken);
        if (result is null) return NotFound(httpContext, "Processing batch/module pairing was not found in the executable current-use view.");

        return TypedResults.Ok(new ProcessingInputStorageLocationOptionsResponse(
            result.ProcurementBatchId,
            result.ProcessingModuleId,
            result.AutoSelectionLocationId,
            result.Locations.Items.Select(ToStorageLocationResponse).ToArray(),
            EncodeCursor(result.Locations.NextOffset)));
    }

    private static async Task<IResult> ListModuleOutputsAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IProcessingExecutionOptionsReader reader,
        CancellationToken cancellationToken)
    {
        if (!TryParseRequiredGuid(httpContext, "procurementBatchId", out var batchId, out var batchError)) return batchError!;
        if (!TryParseRequiredGuid(httpContext, "processingModuleId", out var moduleId, out var moduleError)) return moduleError!;

        var result = await reader.GetModuleOutputsAsync(
            batchId,
            moduleId,
            localeResolver.Resolve(httpContext.Request),
            cancellationToken);
        if (result is null) return NotFound(httpContext, "Processing batch/module pairing was not found in the executable current-use view.");

        return TypedResults.Ok(new ProcessingModuleOutputOptionsResponse(
            result.ProcurementBatchId,
            result.ProcessingModuleId,
            result.Outputs.Select(item => new ProcessingModuleOutputOptionResponse(
                item.Id,
                item.OutputSequence,
                item.OutputKind.ToString(),
                item.TargetId,
                item.TargetDisplayName,
                item.TargetAvailable,
                item.UsesContainer,
                item.ContainerId,
                item.DefaultContainerCount,
                item.PackagingWeight,
                item.DefaultStorageLocationId,
                item.DefaultStorageLocationAvailable,
                item.DefaultWageRate)).ToArray()));
    }

    private static async Task<IResult> ListStorageLocationsAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IProcessingExecutionOptionsReader reader,
        CancellationToken cancellationToken)
    {
        if (!TryCreateQuery(httpContext, localeResolver, out var query, out var error)) return error!;
        var page = await reader.GetStorageLocationsAsync(query!, cancellationToken);
        return TypedResults.Ok(new ProcessingStorageLocationOptionsResponse(
            page.Items.Select(ToStorageLocationResponse).ToArray(),
            EncodeCursor(page.NextOffset)));
    }

    private static ProcessingStorageLocationOptionResponse ToStorageLocationResponse(ProcessingStorageLocationOption item) =>
        new(item.Id, item.DisplayName, item.Code, item.WarehouseId);

    private static IResult NotFound(HttpContext httpContext, string detail) =>
        ApiProblemResults.Create(
            httpContext,
            StatusCodes.Status404NotFound,
            "resource.not-found",
            "Processing execution option resource was not found.",
            detail);

    private static bool TryCreateQuery(
        HttpContext httpContext,
        IApiLocaleResolver localeResolver,
        out ProcessingExecutionOptionsQuery? query,
        out IResult? error)
    {
        if (!TryReadSingleQueryValue(httpContext, "search", false, out var rawSearch, out error)
            || !TryReadSingleQueryValue(httpContext, "limit", false, out var rawLimit, out error)
            || !TryReadSingleQueryValue(httpContext, "cursor", false, out var rawCursor, out error))
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

        query = new ProcessingExecutionOptionsQuery(
            localeResolver.Resolve(httpContext.Request),
            string.IsNullOrWhiteSpace(rawSearch) ? null : rawSearch.Trim(),
            offset,
            limit);
        error = null;
        return true;
    }

    private static bool TryParseRequiredGuid(HttpContext httpContext, string name, out Guid value, out IResult? error)
    {
        if (!TryReadSingleQueryValue(httpContext, name, true, out var raw, out error))
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
        if (string.IsNullOrWhiteSpace(cursor)) return true;
        try
        {
            var normalized = cursor.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
            return int.TryParse(decoded, NumberStyles.None, CultureInfo.InvariantCulture, out offset) && offset >= 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string? EncodeCursor(int? offset)
    {
        if (offset is null) return null;
        var raw = Encoding.UTF8.GetBytes(offset.Value.ToString(CultureInfo.InvariantCulture));
        return Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static IResult ValidationProblem(HttpContext httpContext, string detail) =>
        ApiProblemResults.Create(
            httpContext,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.RequestValidationFailed,
            "Request validation failed.",
            detail);
}

public sealed record ProcessingEmployeeOptionResponse(Guid Id, string DisplayName);
public sealed record ProcessingEmployeeOptionsResponse(IReadOnlyList<ProcessingEmployeeOptionResponse> Items, string? NextCursor);
public sealed record ProcessingBatchOptionResponse(Guid Id, DateOnly ProcurementDate, Guid ProcurementProductId, string ProcurementProductDisplayName, Guid ProcessingRouteVersionId);
public sealed record ProcessingBatchOptionsResponse(IReadOnlyList<ProcessingBatchOptionResponse> Items, string? NextCursor);
public sealed record ProcessingModuleOptionResponse(Guid Id, string DisplayName, string ExecutionMode, Guid? InputProcessMaterialId, bool? InputUsesContainer, Guid? InputContainerId, int? DefaultInputContainerCount);
public sealed record ProcessingBatchModuleOptionsResponse(Guid ProcurementBatchId, Guid ProcessingRouteVersionId, IReadOnlyList<ProcessingModuleOptionResponse> Items, string? NextCursor);
public sealed record ProcessingSupplierOptionResponse(Guid Id, string DisplayName);
public sealed record ProcessingSupplierOptionsResponse(IReadOnlyList<ProcessingSupplierOptionResponse> Items, string? NextCursor);
public sealed record ProcessingStorageLocationOptionResponse(Guid Id, string DisplayName, string? Code, Guid WarehouseId);
public sealed record ProcessingStorageLocationOptionsResponse(IReadOnlyList<ProcessingStorageLocationOptionResponse> Items, string? NextCursor);
public sealed record ProcessingInputStorageLocationOptionsResponse(Guid ProcurementBatchId, Guid ProcessingModuleId, Guid? AutoSelectionLocationId, IReadOnlyList<ProcessingStorageLocationOptionResponse> Items, string? NextCursor);
public sealed record ProcessingModuleOutputOptionResponse(Guid Id, int OutputSequence, string OutputKind, Guid? TargetId, string? TargetDisplayName, bool TargetAvailable, bool UsesContainer, Guid? ContainerId, int? DefaultContainerCount, decimal? PackagingWeight, Guid? DefaultStorageLocationId, bool DefaultStorageLocationAvailable, decimal DefaultWageRate);
public sealed record ProcessingModuleOutputOptionsResponse(Guid ProcurementBatchId, Guid ProcessingModuleId, IReadOnlyList<ProcessingModuleOutputOptionResponse> Outputs);
