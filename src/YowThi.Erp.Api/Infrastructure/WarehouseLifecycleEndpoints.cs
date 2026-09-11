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
using YowThi.Erp.Application.Infrastructure;

namespace YowThi.Erp.Api.Infrastructure;

public static class WarehouseLifecycleEndpoints
{
    public const string SoftDeleteOperationId = "Infrastructure_SoftDeleteWarehouse";
    public const string SoftDeleteCommandType = "SoftDeleteWarehouse";
    public const string RestoreOperationId = "Infrastructure_RestoreWarehouse";
    public const string RestoreCommandType = "RestoreWarehouse";

    private static readonly JsonSerializerOptions CanonicalCommandJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapWarehouseLifecycleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var infrastructure = endpoints.MapApiV1().MapGroup("/infrastructure");

        infrastructure.MapPost("/warehouses/{warehouseId:guid}/soft-delete", SoftDeleteAsync)
            .WithName(SoftDeleteOperationId)
            .RequireAuthorization(CapabilityPolicies.InfrastructureWarehouseLifecycle)
            .RequireRecentDeletionReauthentication()
            .RequireIdempotencyKey()
            .Produces<WarehouseLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        infrastructure.MapPost("/warehouses/{warehouseId:guid}/restore", RestoreAsync)
            .WithName(RestoreOperationId)
            .RequireAuthorization(CapabilityPolicies.InfrastructureWarehouseLifecycle)
            .RequireIdempotencyKey()
            .Produces<WarehouseLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> SoftDeleteAsync(
        Guid warehouseId,
        WarehouseLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IWarehouseLifecycleExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1)
            return InvalidTransportVersion(httpContext);

        var command = new SoftDeleteWarehouseCommand(warehouseId, request.ExpectedRowVersion);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalSoftDeleteRequest(SoftDeleteCommandType, command),
                CanonicalCommandJsonOptions));
        var execution = new SoftDeleteWarehouseExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.SoftDeleteAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Warehouse Soft Delete failed.");
    }

    private static async Task<IResult> RestoreAsync(
        Guid warehouseId,
        WarehouseLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IWarehouseLifecycleExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1)
            return InvalidTransportVersion(httpContext);

        var command = new RestoreWarehouseCommand(warehouseId, request.ExpectedRowVersion);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalRestoreRequest(RestoreCommandType, command),
                CanonicalCommandJsonOptions));
        var execution = new RestoreWarehouseExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.RestoreAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Warehouse Restore failed.");
    }

    private static IResult InvalidTransportVersion(HttpContext httpContext) =>
        ApiProblemResults.Create(
            httpContext,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.RequestValidationFailed,
            "Request validation failed.");

    private static IResult CreateFailureResult(HttpContext httpContext, ApplicationError error, string title)
    {
        var statusCode = error.Kind switch
        {
            ApplicationErrorKind.Validation => StatusCodes.Status422UnprocessableEntity,
            ApplicationErrorKind.NotFound => StatusCodes.Status404NotFound,
            ApplicationErrorKind.Conflict => StatusCodes.Status409Conflict,
            ApplicationErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            _ => throw new ArgumentOutOfRangeException(nameof(error.Kind), error.Kind, "Unsupported application error kind."),
        };

        return ApiProblemResults.Create(httpContext, statusCode, error.Code, title);
    }

    private sealed record CanonicalSoftDeleteRequest(string CommandType, SoftDeleteWarehouseCommand Command);
    private sealed record CanonicalRestoreRequest(string CommandType, RestoreWarehouseCommand Command);
}

public sealed record WarehouseLifecycleRequest(long ExpectedRowVersion);
