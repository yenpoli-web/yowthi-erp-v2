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
using YowThi.Erp.Application.Procurement;
using YowThi.Erp.Domain.Procurement;

namespace YowThi.Erp.Api.Procurement;

public static class ProcurementEndpoints
{
    public const string ConfirmEntryOperationId = "Procurement_ConfirmEntry";
    public const string ConfirmEntryCommandType = "ConfirmProcurementEntry";
    public const string CloseBatchOperationId = "Procurement_CloseBatch";
    public const string CloseBatchCommandType = "CloseProcurementBatch";
    public const string ReopenBatchOperationId = "Procurement_ReopenBatch";
    public const string ReopenBatchCommandType = "ReopenProcurementBatch";

    private static readonly JsonSerializerOptions CanonicalCommandJsonOptions = CreateCanonicalCommandJsonOptions();

    public static IEndpointRouteBuilder MapProcurementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var procurement = endpoints.MapApiV1().MapGroup("/procurement");

        procurement.MapPost("/entries", ConfirmEntryAsync)
            .WithName(ConfirmEntryOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcurementConfirm)
            .RequireIdempotencyKey()
            .Produces<ConfirmProcurementEntryResult>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        procurement.MapPost("/batches/{procurementBatchId:guid}/close", CloseBatchAsync)
            .WithName(CloseBatchOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcurementConfirm)
            .RequireIdempotencyKey()
            .Produces<CloseProcurementBatchResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        procurement.MapPost("/batches/{procurementBatchId:guid}/reopen", ReopenBatchAsync)
            .WithName(ReopenBatchOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcurementBatchLifecycle)
            .RequireIdempotencyKey()
            .Produces<ReopenProcurementBatchResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> ConfirmEntryAsync(
        ConfirmProcurementEntryRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IConfirmProcurementEntryExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new ConfirmProcurementEntryCommand(
            request.ProcurementDate,
            request.ProcurementProductId,
            request.SourceType,
            request.SupplierId,
            request.FarmerId,
            request.NetQuantity,
            request.UnitPrice,
            request.CompanyPickup);

        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalConfirmProcurementEntryRequest(ConfirmEntryCommandType, command),
                CanonicalCommandJsonOptions));

        var execution = new ConfirmProcurementEntryExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.ExecuteAsync(execution, cancellationToken);
        if (result.IsSuccess)
        {
            return TypedResults.Created(
                $"/api/v1/procurement/entries/{result.Value.ProcurementEntryId}",
                result.Value);
        }

        return CreateFailureResult(
            httpContext,
            result.Error,
            "Procurement Entry confirmation failed.");
    }

    private static async Task<IResult> CloseBatchAsync(
        Guid procurementBatchId,
        CloseProcurementBatchRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ICloseProcurementBatchExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new CloseProcurementBatchCommand(
            procurementBatchId,
            request.ExpectedRowVersion);

        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalCloseProcurementBatchRequest(CloseBatchCommandType, command),
                CanonicalCommandJsonOptions));

        var execution = new CloseProcurementBatchExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.ExecuteAsync(execution, cancellationToken);
        if (result.IsSuccess)
        {
            return TypedResults.Ok(result.Value);
        }

        return CreateFailureResult(
            httpContext,
            result.Error,
            "Procurement Batch close failed.");
    }

    private static async Task<IResult> ReopenBatchAsync(
        Guid procurementBatchId,
        ReopenProcurementBatchRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IReopenProcurementBatchExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new ReopenProcurementBatchCommand(
            procurementBatchId,
            request.ExpectedRowVersion,
            request.ReasonText);

        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalReopenProcurementBatchRequest(ReopenBatchCommandType, command),
                CanonicalCommandJsonOptions));

        var execution = new ReopenProcurementBatchExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.ExecuteAsync(execution, cancellationToken);
        if (result.IsSuccess)
        {
            return TypedResults.Ok(result.Value);
        }

        return CreateFailureResult(
            httpContext,
            result.Error,
            "Procurement Batch reopen failed.");
    }

    private static IResult CreateFailureResult(
        HttpContext httpContext,
        ApplicationError error,
        string title)
    {
        var statusCode = error.Kind switch
        {
            ApplicationErrorKind.Validation => StatusCodes.Status422UnprocessableEntity,
            ApplicationErrorKind.NotFound => StatusCodes.Status404NotFound,
            ApplicationErrorKind.Conflict => StatusCodes.Status409Conflict,
            ApplicationErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            _ => throw new ArgumentOutOfRangeException(nameof(error.Kind), error.Kind, "Unsupported application error kind."),
        };

        return ApiProblemResults.Create(
            httpContext,
            statusCode,
            error.Code,
            title);
    }

    private static JsonSerializerOptions CreateCanonicalCommandJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record CanonicalConfirmProcurementEntryRequest(
        string CommandType,
        ConfirmProcurementEntryCommand Command);

    private sealed record CanonicalCloseProcurementBatchRequest(
        string CommandType,
        CloseProcurementBatchCommand Command);

    private sealed record CanonicalReopenProcurementBatchRequest(
        string CommandType,
        ReopenProcurementBatchCommand Command);
}

public sealed record ConfirmProcurementEntryRequest(
    DateOnly ProcurementDate,
    Guid ProcurementProductId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    ProcurementSourceType SourceType,
    Guid? SupplierId,
    Guid? FarmerId,
    decimal NetQuantity,
    decimal UnitPrice,
    bool CompanyPickup);

public sealed record CloseProcurementBatchRequest(long ExpectedRowVersion);

public sealed record ReopenProcurementBatchRequest(
    long ExpectedRowVersion,
    string? ReasonText);
