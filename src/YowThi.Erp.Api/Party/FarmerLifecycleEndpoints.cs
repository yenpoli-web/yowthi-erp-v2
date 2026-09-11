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
using YowThi.Erp.Application.Party;

namespace YowThi.Erp.Api.Party;

public static class FarmerLifecycleEndpoints
{
    public const string SoftDeleteFarmerOperationId = "Party_SoftDeleteFarmer";
    public const string SoftDeleteFarmerCommandType = "SoftDeleteFarmer";
    public const string RestoreFarmerOperationId = "Party_RestoreFarmer";
    public const string RestoreFarmerCommandType = "RestoreFarmer";

    private static readonly JsonSerializerOptions CanonicalCommandJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapFarmerLifecycleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var party = endpoints.MapApiV1().MapGroup("/party");

        party.MapPost("/farmers/{farmerId:guid}/soft-delete", SoftDeleteFarmerAsync)
            .WithName(SoftDeleteFarmerOperationId)
            .RequireAuthorization(CapabilityPolicies.FarmerLifecycle)
            .RequireRecentDeletionReauthentication()
            .RequireIdempotencyKey()
            .Produces<FarmerLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        party.MapPost("/farmers/{farmerId:guid}/restore", RestoreFarmerAsync)
            .WithName(RestoreFarmerOperationId)
            .RequireAuthorization(CapabilityPolicies.FarmerLifecycle)
            .RequireIdempotencyKey()
            .Produces<FarmerLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> SoftDeleteFarmerAsync(
        Guid farmerId,
        FarmerLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IFarmerLifecycleExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1)
        {
            return InvalidTransportVersion(httpContext);
        }

        var command = new SoftDeleteFarmerCommand(farmerId, request.ExpectedRowVersion);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalSoftDeleteFarmerRequest(SoftDeleteFarmerCommandType, command),
                CanonicalCommandJsonOptions));
        var execution = new SoftDeleteFarmerExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.SoftDeleteAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Farmer Soft Delete failed.");
    }

    private static async Task<IResult> RestoreFarmerAsync(
        Guid farmerId,
        FarmerLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IFarmerLifecycleExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1)
        {
            return InvalidTransportVersion(httpContext);
        }

        var command = new RestoreFarmerCommand(farmerId, request.ExpectedRowVersion);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalRestoreFarmerRequest(RestoreFarmerCommandType, command),
                CanonicalCommandJsonOptions));
        var execution = new RestoreFarmerExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.RestoreAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Farmer Restore failed.");
    }

    private static IResult InvalidTransportVersion(HttpContext httpContext) =>
        ApiProblemResults.Create(
            httpContext,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.RequestValidationFailed,
            "Request validation failed.");

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

        return ApiProblemResults.Create(httpContext, statusCode, error.Code, title);
    }

    private sealed record CanonicalSoftDeleteFarmerRequest(
        string CommandType,
        SoftDeleteFarmerCommand Command);

    private sealed record CanonicalRestoreFarmerRequest(
        string CommandType,
        RestoreFarmerCommand Command);
}

public sealed record FarmerLifecycleRequest(long ExpectedRowVersion);
