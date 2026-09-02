using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Api.Idempotency;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Serialization;
using YowThi.Erp.Application.Processing;
using YowThi.Erp.Domain.Processing;

namespace YowThi.Erp.Api.Processing;

public static class ProcessingEndpoints
{
    public const string ConfirmExecutionOperationId = "Processing_ConfirmExecution";
    public const string ConfirmExecutionCommandType = "ConfirmProcessingExecution";

    private static readonly JsonSerializerOptions CanonicalCommandJsonOptions = CreateCanonicalCommandJsonOptions();

    public static IEndpointRouteBuilder MapProcessingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var processing = endpoints.MapApiV1().MapGroup("/processing");

        processing.MapPost("/executions", ConfirmExecutionAsync)
            .WithName(ConfirmExecutionOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcessingConfirm)
            .RequireIdempotencyKey()
            .Produces<ConfirmProcessingExecutionResult>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> ConfirmExecutionAsync(
        ConfirmProcessingExecutionRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IConfirmProcessingExecutionExecutor executor,
        CancellationToken cancellationToken)
    {
        var source = request.Source is null
            ? null
            : new ProcessingSourceSelection(request.Source.SourceKind, request.Source.SupplierId);

        var inputScale = request.InputScale is null
            ? null
            : new ProcessingScaleMeasurement(
                request.InputScale.ObservedScaleReading,
                request.InputScale.ActualContainerCount);

        var outputs = request.Outputs is null
            ? Array.Empty<ProcessingOutputMeasurement>()
            : request.Outputs
                .Select(output => new ProcessingOutputMeasurement(
                    output.ProcessingModuleOutputId,
                    output.ObservedScaleReading,
                    output.ActualContainerCount,
                    output.CompletedQuantity,
                    output.OutputStorageLocationId))
                .ToArray();

        var command = new ConfirmProcessingExecutionCommand(
            request.WorkDate,
            request.EmployeeId,
            request.ProcurementBatchId,
            request.ProcessingModuleId,
            source,
            inputScale,
            request.InputStorageLocationId,
            outputs);

        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalConfirmProcessingExecutionRequest(ConfirmExecutionCommandType, command),
                CanonicalCommandJsonOptions));

        var execution = new ConfirmProcessingExecutionExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.ExecuteAsync(execution, cancellationToken);
        if (result.IsSuccess)
        {
            return TypedResults.Created(
                $"/api/v1/processing/executions/{result.Value.ProcessingExecutionId}",
                result.Value);
        }

        var statusCode = result.Error.Kind switch
        {
            ApplicationErrorKind.Validation => StatusCodes.Status422UnprocessableEntity,
            ApplicationErrorKind.NotFound => StatusCodes.Status404NotFound,
            ApplicationErrorKind.Conflict => StatusCodes.Status409Conflict,
            ApplicationErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            _ => throw new ArgumentOutOfRangeException(nameof(result.Error.Kind), result.Error.Kind, "Unsupported application error kind."),
        };

        return ApiProblemResults.Create(
            httpContext,
            statusCode,
            result.Error.Code,
            "Processing Execution confirmation failed.");
    }

    private static JsonSerializerOptions CreateCanonicalCommandJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record CanonicalConfirmProcessingExecutionRequest(
        string CommandType,
        ConfirmProcessingExecutionCommand Command);
}

public sealed record ConfirmProcessingExecutionRequest(
    DateOnly WorkDate,
    Guid EmployeeId,
    Guid ProcurementBatchId,
    Guid ProcessingModuleId,
    ProcessingSourceSelectionRequest? Source,
    ProcessingScaleMeasurementRequest? InputScale,
    Guid? InputStorageLocationId,
    IReadOnlyList<ProcessingOutputMeasurementRequest>? Outputs);

public sealed record ProcessingSourceSelectionRequest(
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    ProcessingSourceKind SourceKind,
    Guid? SupplierId);

public sealed record ProcessingScaleMeasurementRequest(
    decimal ObservedScaleReading,
    int? ActualContainerCount);

public sealed record ProcessingOutputMeasurementRequest(
    Guid ProcessingModuleOutputId,
    decimal? ObservedScaleReading,
    int? ActualContainerCount,
    decimal? CompletedQuantity,
    Guid? OutputStorageLocationId);
