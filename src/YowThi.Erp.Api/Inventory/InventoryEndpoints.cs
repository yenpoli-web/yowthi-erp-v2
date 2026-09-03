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
using YowThi.Erp.Application.Inventory;
using YowThi.Erp.Domain.Inventory;

namespace YowThi.Erp.Api.Inventory;

public static class InventoryEndpoints
{
    public const string TransferInventoryOperationId = "Inventory_TransferInventory";
    public const string AdjustInventoryOperationId = "Inventory_AdjustInventory";

    public const string TransferInventoryCommandType = "TransferInventory";
    public const string AdjustInventoryCommandType = "AdjustInventory";

    private static readonly JsonSerializerOptions CanonicalCommandJsonOptions = CreateCanonicalCommandJsonOptions();

    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var inventory = endpoints.MapApiV1().MapGroup("/inventory");

        inventory.MapPost("/transfers", TransferInventoryAsync)
            .WithName(TransferInventoryOperationId)
            .RequireAuthorization(CapabilityPolicies.InventoryAdjust)
            .RequireIdempotencyKey()
            .Produces<TransferInventoryResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        inventory.MapPost("/adjustments", AdjustInventoryAsync)
            .WithName(AdjustInventoryOperationId)
            .RequireAuthorization(CapabilityPolicies.InventoryAdjust)
            .RequireIdempotencyKey()
            .Produces<AdjustInventoryResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> TransferInventoryAsync(
        TransferInventoryRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ITransferInventoryExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.InventoryIdentity is null
            || request.SourceStorageLocationId == Guid.Empty
            || request.DestinationStorageLocationId == Guid.Empty)
        {
            return TransportValidationFailure(httpContext);
        }

        var command = new TransferInventoryCommand(
            request.InventoryIdentity.ToApplication(),
            request.SourceStorageLocationId,
            request.DestinationStorageLocationId,
            request.Quantity);
        var execution = new TransferInventoryExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(CanonicalPayload(TransferInventoryCommandType, command)),
            actorContext.ActorAccountId,
            command);

        var result = await executor.ExecuteAsync(execution, cancellationToken);
        if (result.IsSuccess)
        {
            var response = new TransferInventoryResponse(
                result.Value.InventoryOperationId,
                result.Value.TransferOutMovementId,
                result.Value.TransferInMovementId,
                result.Value.Quantity);

            return TypedResults.Created(
                $"/api/v1/inventory/transfers/{result.Value.InventoryOperationId}",
                response);
        }

        return InventoryFailure(httpContext, result.Error, "Inventory transfer failed.");
    }

    private static async Task<IResult> AdjustInventoryAsync(
        AdjustInventoryRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IAdjustInventoryExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.InventoryIdentity is null || request.StorageLocationId == Guid.Empty)
        {
            return TransportValidationFailure(httpContext);
        }

        var command = new AdjustInventoryCommand(
            request.InventoryIdentity.ToApplication(),
            request.StorageLocationId,
            request.QuantityDelta,
            request.ReasonText ?? string.Empty);
        var execution = new AdjustInventoryExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(CanonicalPayload(AdjustInventoryCommandType, command)),
            actorContext.ActorAccountId,
            command);

        var result = await executor.ExecuteAsync(execution, cancellationToken);
        if (result.IsSuccess)
        {
            var response = new AdjustInventoryResponse(
                result.Value.InventoryOperationId,
                result.Value.AdjustmentMovementId,
                result.Value.QuantityDelta);

            return TypedResults.Created(
                $"/api/v1/inventory/adjustments/{result.Value.InventoryOperationId}",
                response);
        }

        return InventoryFailure(httpContext, result.Error, "Inventory adjustment failed.");
    }

    private static JsonPayload CanonicalPayload<TCommand>(string commandType, TCommand command) =>
        JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalInventoryCommand<TCommand>(commandType, command),
                CanonicalCommandJsonOptions));

    private static IResult TransportValidationFailure(HttpContext httpContext) =>
        ApiProblemResults.Create(
            httpContext,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.RequestValidationFailed,
            "Request validation failed.");

    private static IResult InventoryFailure(
        HttpContext httpContext,
        ApplicationError error,
        string detail)
    {
        var statusCode = error.Kind switch
        {
            ApplicationErrorKind.Validation => StatusCodes.Status422UnprocessableEntity,
            ApplicationErrorKind.NotFound => StatusCodes.Status404NotFound,
            ApplicationErrorKind.Conflict => StatusCodes.Status409Conflict,
            ApplicationErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            _ => throw new ArgumentOutOfRangeException(
                nameof(error.Kind),
                error.Kind,
                "Unsupported application error kind."),
        };

        return ApiProblemResults.Create(httpContext, statusCode, error.Code, detail);
    }

    private static JsonSerializerOptions CreateCanonicalCommandJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record CanonicalInventoryCommand<TCommand>(
        string CommandType,
        TCommand Command);
}

public sealed record InventoryPositionIdentityRequest(
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    InventoryOrigin Origin,
    Guid? ProcurementBatchId,
    Guid? OutsourcedSupplyBatchId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    InventoryObjectKind InventoryObjectKind,
    Guid? ProcurementProductId,
    Guid? ProcessMaterialId,
    Guid? SalesProductId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    InventoryRawSourceKind? RawSourceKind,
    Guid? SupplierId)
{
    public InventoryPositionIdentity ToApplication() =>
        new(
            Origin,
            ProcurementBatchId,
            OutsourcedSupplyBatchId,
            InventoryObjectKind,
            ProcurementProductId,
            ProcessMaterialId,
            SalesProductId,
            RawSourceKind,
            SupplierId);
}

public sealed record TransferInventoryRequest(
    InventoryPositionIdentityRequest? InventoryIdentity,
    Guid SourceStorageLocationId,
    Guid DestinationStorageLocationId,
    decimal Quantity);

public sealed record AdjustInventoryRequest(
    InventoryPositionIdentityRequest? InventoryIdentity,
    Guid StorageLocationId,
    decimal QuantityDelta,
    string? ReasonText);

public sealed record TransferInventoryResponse(
    Guid InventoryOperationId,
    Guid TransferOutMovementId,
    Guid TransferInMovementId,
    decimal Quantity);

public sealed record AdjustInventoryResponse(
    Guid InventoryOperationId,
    Guid AdjustmentMovementId,
    decimal QuantityDelta);
