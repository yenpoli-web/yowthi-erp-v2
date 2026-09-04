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
using YowThi.Erp.Application.SalesHandling;

namespace YowThi.Erp.Api.SalesHandling;

public static class SalesPackagingItemLifecycleEndpoints
{
    public const string SoftDeleteOperationId = "SalesHandling_SoftDeletePackagingItem";
    public const string SoftDeleteCommandType = "SoftDeleteSalesPackagingItem";
    public const string RestoreOperationId = "SalesHandling_RestorePackagingItem";
    public const string RestoreCommandType = "RestoreSalesPackagingItem";

    private static readonly JsonSerializerOptions CanonicalCommandJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapSalesPackagingItemLifecycleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var salesHandling = endpoints.MapApiV1().MapGroup("/sales-handling");

        salesHandling.MapPost("/packaging-items/{salesPackagingItemId:guid}/soft-delete", SoftDeleteAsync)
            .WithName(SoftDeleteOperationId)
            .RequireAuthorization(CapabilityPolicies.SalesPackagingItemLifecycle)
            .RequireIdempotencyKey()
            .Produces<SalesPackagingItemLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        salesHandling.MapPost("/packaging-items/{salesPackagingItemId:guid}/restore", RestoreAsync)
            .WithName(RestoreOperationId)
            .RequireAuthorization(CapabilityPolicies.SalesPackagingItemLifecycle)
            .RequireIdempotencyKey()
            .Produces<SalesPackagingItemLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> SoftDeleteAsync(
        Guid salesPackagingItemId,
        SalesPackagingItemLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ISalesPackagingItemLifecycleExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1)
        {
            return InvalidTransportVersion(httpContext);
        }

        var command = new SoftDeleteSalesPackagingItemCommand(salesPackagingItemId, request.ExpectedRowVersion);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalSoftDeleteRequest(SoftDeleteCommandType, command),
                CanonicalCommandJsonOptions));
        var execution = new SoftDeleteSalesPackagingItemExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.SoftDeleteAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Sales Packaging Item Soft Delete failed.");
    }

    private static async Task<IResult> RestoreAsync(
        Guid salesPackagingItemId,
        SalesPackagingItemLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ISalesPackagingItemLifecycleExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1)
        {
            return InvalidTransportVersion(httpContext);
        }

        var command = new RestoreSalesPackagingItemCommand(salesPackagingItemId, request.ExpectedRowVersion);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalRestoreRequest(RestoreCommandType, command),
                CanonicalCommandJsonOptions));
        var execution = new RestoreSalesPackagingItemExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.RestoreAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Sales Packaging Item Restore failed.");
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

    private sealed record CanonicalSoftDeleteRequest(string CommandType, SoftDeleteSalesPackagingItemCommand Command);
    private sealed record CanonicalRestoreRequest(string CommandType, RestoreSalesPackagingItemCommand Command);
}

public sealed record SalesPackagingItemLifecycleRequest(long ExpectedRowVersion);
