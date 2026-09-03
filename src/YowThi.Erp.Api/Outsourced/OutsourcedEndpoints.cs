using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Api.Idempotency;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Serialization;
using YowThi.Erp.Application.Outsourced;

namespace YowThi.Erp.Api.Outsourced;

public static class OutsourcedEndpoints
{
    public const string ConfirmSupplyDetailOperationId = "Outsourced_ConfirmSupplyDetail";
    public const string ConfirmSupplyDetailCommandType = "ConfirmOutsourcedSupplyDetail";
    public const string CloseBatchOperationId = "Outsourced_CloseBatch";
    public const string CloseBatchCommandType = "CloseOutsourcedSupplyBatch";

    private static readonly JsonSerializerOptions CanonicalCommandJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapOutsourcedEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var outsourced = endpoints.MapApiV1().MapGroup("/outsourced");

        outsourced.MapPost("/supply-details", ConfirmSupplyDetailAsync)
            .WithName(ConfirmSupplyDetailOperationId)
            .RequireAuthorization(CapabilityPolicies.OutsourcedConfirm)
            .RequireIdempotencyKey()
            .Produces<ConfirmOutsourcedSupplyDetailResult>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        outsourced.MapPost("/batches/{outsourcedSupplyBatchId:guid}/close", CloseBatchAsync)
            .WithName(CloseBatchOperationId)
            .RequireAuthorization(CapabilityPolicies.OutsourcedConfirm)
            .RequireIdempotencyKey()
            .Produces<CloseOutsourcedSupplyBatchResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> ConfirmSupplyDetailAsync(
        ConfirmOutsourcedSupplyDetailRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IConfirmOutsourcedSupplyDetailExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new ConfirmOutsourcedSupplyDetailCommand(
            request.SupplyDate,
            request.OutsourcedVendorId,
            request.SalesProductId,
            request.Quantity,
            request.UnitPrice,
            request.ReceiptStorageLocationId);

        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalConfirmOutsourcedSupplyDetailRequest(ConfirmSupplyDetailCommandType, command),
                CanonicalCommandJsonOptions));

        var execution = new ConfirmOutsourcedSupplyDetailExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.ExecuteAsync(execution, cancellationToken);
        if (result.IsSuccess)
        {
            return TypedResults.Created(
                $"/api/v1/outsourced/supply-details/{result.Value.OutsourcedSupplyDetailId}",
                result.Value);
        }

        return CreateFailureResult(
            httpContext,
            result.Error,
            "Outsourced Supply Detail confirmation failed.");
    }

    private static async Task<IResult> CloseBatchAsync(
        Guid outsourcedSupplyBatchId,
        CloseOutsourcedSupplyBatchRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ICloseOutsourcedSupplyBatchExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new CloseOutsourcedSupplyBatchCommand(
            outsourcedSupplyBatchId,
            request.ExpectedRowVersion);

        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalCloseOutsourcedSupplyBatchRequest(CloseBatchCommandType, command),
                CanonicalCommandJsonOptions));

        var execution = new CloseOutsourcedSupplyBatchExecution(
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
            "Outsourced Supply Batch close failed.");
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

    private sealed record CanonicalConfirmOutsourcedSupplyDetailRequest(
        string CommandType,
        ConfirmOutsourcedSupplyDetailCommand Command);

    private sealed record CanonicalCloseOutsourcedSupplyBatchRequest(
        string CommandType,
        CloseOutsourcedSupplyBatchCommand Command);
}

public sealed record ConfirmOutsourcedSupplyDetailRequest(
    DateOnly SupplyDate,
    Guid OutsourcedVendorId,
    Guid SalesProductId,
    decimal Quantity,
    decimal UnitPrice,
    Guid? ReceiptStorageLocationId);

public sealed record CloseOutsourcedSupplyBatchRequest(long ExpectedRowVersion);
