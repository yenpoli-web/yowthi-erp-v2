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
using YowThi.Erp.Application.Product;

namespace YowThi.Erp.Api.Product;

public static class SalesProductGroupLifecycleEndpoints
{
    public const string SoftDeleteOperationId = "Product_SoftDeleteSalesProductGroup";
    public const string SoftDeleteCommandType = "SoftDeleteSalesProductGroup";
    public const string RestoreOperationId = "Product_RestoreSalesProductGroup";
    public const string RestoreCommandType = "RestoreSalesProductGroup";

    private static readonly JsonSerializerOptions CanonicalCommandJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapSalesProductGroupLifecycleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var product = endpoints.MapApiV1().MapGroup("/product");

        product.MapPost("/sales-product-groups/{salesProductGroupId:guid}/soft-delete", SoftDeleteAsync)
            .WithName(SoftDeleteOperationId)
            .RequireAuthorization(CapabilityPolicies.SalesProductGroupLifecycle)
            .RequireRecentDeletionReauthentication()
            .RequireIdempotencyKey()
            .Produces<SalesProductGroupLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        product.MapPost("/sales-product-groups/{salesProductGroupId:guid}/restore", RestoreAsync)
            .WithName(RestoreOperationId)
            .RequireAuthorization(CapabilityPolicies.SalesProductGroupLifecycle)
            .RequireIdempotencyKey()
            .Produces<SalesProductGroupLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> SoftDeleteAsync(
        Guid salesProductGroupId,
        SalesProductGroupLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ISalesProductGroupLifecycleExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1)
        {
            return InvalidTransportVersion(httpContext);
        }

        var command = new SoftDeleteSalesProductGroupCommand(salesProductGroupId, request.ExpectedRowVersion);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalSoftDeleteRequest(SoftDeleteCommandType, command),
                CanonicalCommandJsonOptions));
        var execution = new SoftDeleteSalesProductGroupExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.SoftDeleteAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Sales Product Group Soft Delete failed.");
    }

    private static async Task<IResult> RestoreAsync(
        Guid salesProductGroupId,
        SalesProductGroupLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ISalesProductGroupLifecycleExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1)
        {
            return InvalidTransportVersion(httpContext);
        }

        var command = new RestoreSalesProductGroupCommand(salesProductGroupId, request.ExpectedRowVersion);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalRestoreRequest(RestoreCommandType, command),
                CanonicalCommandJsonOptions));
        var execution = new RestoreSalesProductGroupExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.RestoreAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Sales Product Group Restore failed.");
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

    private sealed record CanonicalSoftDeleteRequest(string CommandType, SoftDeleteSalesProductGroupCommand Command);
    private sealed record CanonicalRestoreRequest(string CommandType, RestoreSalesProductGroupCommand Command);
}

public sealed record SalesProductGroupLifecycleRequest(long ExpectedRowVersion);
