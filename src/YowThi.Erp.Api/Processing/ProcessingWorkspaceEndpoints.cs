using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Api.Localization;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.Processing;

namespace YowThi.Erp.Api.Processing;

public static class ProcessingWorkspaceEndpoints
{
    public const string ListExecutionsOperationId = "Processing_ListWorkspace";
    public const string GetExecutionOperationId = "Processing_GetWorkspace";

    public static IEndpointRouteBuilder MapProcessingWorkspaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapApiV1().MapGroup("/processing/workspace");

        group.MapGet("/", ListExecutionsAsync)
            .WithName(ListExecutionsOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcessingConfirm)
            .Produces<ProcessingWorkspaceListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{processingExecutionId:guid}", GetExecutionAsync)
            .WithName(GetExecutionOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcessingConfirm)
            .Produces<ProcessingWorkspaceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ListExecutionsAsync(
        HttpContext context,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IProcessingWorkspaceReader reader,
        string? search = null,
        int offset = 0,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0 || limit is < 1 or > 100)
        {
            return ApiProblemResults.Create(
                context,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.RequestValidationFailed,
                "Request validation failed.",
                "offset must be non-negative and limit must be between 1 and 100.");
        }

        var page = await reader.GetExecutionsAsync(
            new ProcessingWorkspaceListQuery(
                localeResolver.Resolve(context.Request),
                string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                offset,
                limit),
            cancellationToken);

        return TypedResults.Ok(new ProcessingWorkspaceListResponse(
            page.Items.Select(ToResponse).ToArray(),
            page.NextOffset));
    }

    private static async Task<IResult> GetExecutionAsync(
        Guid processingExecutionId,
        HttpContext context,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IProcessingWorkspaceReader reader,
        CancellationToken cancellationToken)
    {
        var workspace = await reader.GetExecutionAsync(
            processingExecutionId,
            localeResolver.Resolve(context.Request),
            cancellationToken);
        if (workspace is null)
        {
            return ApiProblemResults.Create(
                context,
                StatusCodes.Status404NotFound,
                "resource.not-found",
                "Processing Execution was not found.");
        }

        return TypedResults.Ok(ToResponse(workspace));
    }

    private static ProcessingWorkspaceListItemResponse ToResponse(ProcessingWorkspaceListItem item) =>
        new(
            item.Id,
            item.WorkDate,
            item.EmployeeId,
            item.EmployeeDisplayName,
            item.ProcurementBatchId,
            item.ProcurementDate,
            item.ProcurementProductId,
            item.ProcurementProductDisplayName,
            item.ProcessingModuleId,
            item.ProcessingModuleDisplayName,
            item.ExecutionMode,
            item.RowVersion,
            item.RecordedAt,
            item.DeletedAt);

    private static ProcessingWorkspaceResponse ToResponse(ProcessingWorkspace workspace) =>
        new(
            workspace.Id,
            workspace.WorkDate,
            workspace.EmployeeId,
            workspace.EmployeeDisplayName,
            workspace.ProcurementBatchId,
            workspace.ProcurementDate,
            workspace.ProcurementProductId,
            workspace.ProcurementProductDisplayName,
            workspace.ProcessingRouteVersionId,
            workspace.ProcessingModuleId,
            workspace.ProcessingModuleDisplayName,
            workspace.ExecutionMode,
            workspace.SourceKind,
            workspace.SupplierId,
            workspace.SupplierDisplayName,
            workspace.RowVersion,
            workspace.RecordedAt,
            workspace.DeletedAt,
            workspace.Input is null ? null : ToResponse(workspace.Input),
            workspace.Outputs.Select(ToResponse).ToArray());

    private static ProcessingWorkspaceInputResponse ToResponse(ProcessingWorkspaceInput input) =>
        new(
            input.ProcessingExecutionId,
            input.ConsumptionBasis,
            input.ConsumedQuantity,
            input.ObservedScaleReading,
            input.ActualContainerCount,
            input.TareWeightSnapshot,
            input.DerivedNetQuantity,
            input.InventoryObjectKind,
            input.InventoryObjectId,
            input.InventoryObjectDisplayName,
            input.StorageLocationId,
            input.StorageLocationDisplayName,
            input.RowVersion,
            input.DeletedAt);

    private static ProcessingWorkspaceOutputResponse ToResponse(ProcessingWorkspaceOutput output) =>
        new(
            output.Id,
            output.ProcessingModuleOutputId,
            output.OutputSequence,
            output.OutputKind,
            output.TargetId,
            output.TargetDisplayName,
            output.ConfiguredWageRate,
            output.ObservedScaleReading,
            output.ActualContainerCount,
            output.TareWeightSnapshot,
            output.DerivedNetQuantity,
            output.CompletedQuantity,
            output.PackagingWeightSnapshot,
            output.SourceConsumptionQuantity,
            output.StorageLocationId,
            output.StorageLocationDisplayName,
            output.RowVersion,
            output.DeletedAt);
}

public sealed record ProcessingWorkspaceListItemResponse(
    Guid Id,
    DateOnly WorkDate,
    Guid EmployeeId,
    string EmployeeDisplayName,
    Guid ProcurementBatchId,
    DateOnly ProcurementDate,
    Guid ProcurementProductId,
    string ProcurementProductDisplayName,
    Guid ProcessingModuleId,
    string ProcessingModuleDisplayName,
    string ExecutionMode,
    long RowVersion,
    DateTimeOffset RecordedAt,
    DateTimeOffset? DeletedAt);

public sealed record ProcessingWorkspaceListResponse(
    IReadOnlyList<ProcessingWorkspaceListItemResponse> Items,
    int? NextOffset);

public sealed record ProcessingWorkspaceInputResponse(
    Guid ProcessingExecutionId,
    string ConsumptionBasis,
    decimal ConsumedQuantity,
    decimal? ObservedScaleReading,
    int? ActualContainerCount,
    decimal? TareWeightSnapshot,
    decimal? DerivedNetQuantity,
    string? InventoryObjectKind,
    Guid? InventoryObjectId,
    string? InventoryObjectDisplayName,
    Guid? StorageLocationId,
    string? StorageLocationDisplayName,
    long RowVersion,
    DateTimeOffset? DeletedAt);

public sealed record ProcessingWorkspaceOutputResponse(
    Guid Id,
    Guid ProcessingModuleOutputId,
    int OutputSequence,
    string OutputKind,
    Guid? TargetId,
    string? TargetDisplayName,
    decimal ConfiguredWageRate,
    decimal? ObservedScaleReading,
    int? ActualContainerCount,
    decimal? TareWeightSnapshot,
    decimal? DerivedNetQuantity,
    decimal? CompletedQuantity,
    decimal? PackagingWeightSnapshot,
    decimal? SourceConsumptionQuantity,
    Guid? StorageLocationId,
    string? StorageLocationDisplayName,
    long RowVersion,
    DateTimeOffset? DeletedAt);

public sealed record ProcessingWorkspaceResponse(
    Guid Id,
    DateOnly WorkDate,
    Guid EmployeeId,
    string EmployeeDisplayName,
    Guid ProcurementBatchId,
    DateOnly ProcurementDate,
    Guid ProcurementProductId,
    string ProcurementProductDisplayName,
    Guid ProcessingRouteVersionId,
    Guid ProcessingModuleId,
    string ProcessingModuleDisplayName,
    string ExecutionMode,
    string? SourceKind,
    Guid? SupplierId,
    string? SupplierDisplayName,
    long RowVersion,
    DateTimeOffset RecordedAt,
    DateTimeOffset? DeletedAt,
    ProcessingWorkspaceInputResponse? Input,
    IReadOnlyList<ProcessingWorkspaceOutputResponse> Outputs);
