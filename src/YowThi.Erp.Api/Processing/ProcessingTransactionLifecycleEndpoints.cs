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
using YowThi.Erp.Application.Processing;

namespace YowThi.Erp.Api.Processing;

public static class ProcessingTransactionLifecycleEndpoints
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapProcessingTransactionLifecycleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapApiV1().MapGroup("/processing");

        MapLifecycle(group, "/executions/{processingExecutionId:guid}/soft-delete", "Processing_SoftDeleteExecution", SoftDeleteExecutionAsync, true);
        MapLifecycle(group, "/executions/{processingExecutionId:guid}/restore", "Processing_RestoreExecution", RestoreExecutionAsync, false);
        MapHardDelete(group, "/executions/{processingExecutionId:guid}/hard-delete", "Processing_HardDeleteExecution", HardDeleteExecutionAsync);

        MapLifecycle(group, "/executions/{processingExecutionId:guid}/input/soft-delete", "Processing_SoftDeleteExecutionInput", SoftDeleteInputAsync, true);
        MapLifecycle(group, "/executions/{processingExecutionId:guid}/input/restore", "Processing_RestoreExecutionInput", RestoreInputAsync, false);
        MapHardDelete(group, "/executions/{processingExecutionId:guid}/input/hard-delete", "Processing_HardDeleteExecutionInput", HardDeleteInputAsync);

        MapLifecycle(group, "/execution-outputs/{processingExecutionOutputId:guid}/soft-delete", "Processing_SoftDeleteExecutionOutput", SoftDeleteOutputAsync, true);
        MapLifecycle(group, "/execution-outputs/{processingExecutionOutputId:guid}/restore", "Processing_RestoreExecutionOutput", RestoreOutputAsync, false);
        MapHardDelete(group, "/execution-outputs/{processingExecutionOutputId:guid}/hard-delete", "Processing_HardDeleteExecutionOutput", HardDeleteOutputAsync);

        return endpoints;
    }

    private static void MapLifecycle(
        RouteGroupBuilder group,
        string pattern,
        string name,
        Delegate handler,
        bool requiresReauthentication)
    {
        var endpoint = group.MapPost(pattern, handler)
            .WithName(name)
            .RequireAuthorization(CapabilityPolicies.ProcessingTransactionLifecycle)
            .RequireIdempotencyKey()
            .Produces<ProcessingTransactionLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        if (requiresReauthentication)
        {
            endpoint.RequireRecentDeletionReauthentication();
        }
    }

    private static void MapHardDelete(RouteGroupBuilder group, string pattern, string name, Delegate handler)
    {
        group.MapPost(pattern, handler)
            .WithName(name)
            .RequireAuthorization(CapabilityPolicies.ProcessingTransactionLifecycle)
            .RequireAuthorization(CapabilityPolicies.DataProtectionHardDelete)
            .RequireIdempotencyKey()
            .RequireRecentDeletionReauthentication()
            .Produces<HardDeleteProcessingTransactionResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> SoftDeleteExecutionAsync(
        Guid processingExecutionId,
        ProcessingTransactionLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IProcessingTransactionLifecycleExecutor executor,
        CancellationToken ct)
    {
        var command = new SoftDeleteProcessingExecutionCommand(processingExecutionId, request.ExpectedRowVersion);
        var execution = new SoftDeleteProcessingExecutionExecution(
            httpContext.GetRequiredCommandId(), Hash(requestHasher, "SoftDeleteProcessingExecution", command), actorContext.ActorAccountId, command);
        return ToResult(httpContext, await executor.SoftDeleteExecutionAsync(execution, ct), "Processing Execution soft delete failed.");
    }

    private static async Task<IResult> RestoreExecutionAsync(
        Guid processingExecutionId,
        ProcessingTransactionLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IProcessingTransactionLifecycleExecutor executor,
        CancellationToken ct)
    {
        var command = new RestoreProcessingExecutionCommand(processingExecutionId, request.ExpectedRowVersion);
        var execution = new RestoreProcessingExecutionExecution(
            httpContext.GetRequiredCommandId(), Hash(requestHasher, "RestoreProcessingExecution", command), actorContext.ActorAccountId, command);
        return ToResult(httpContext, await executor.RestoreExecutionAsync(execution, ct), "Processing Execution restore failed.");
    }

    private static async Task<IResult> HardDeleteExecutionAsync(
        Guid processingExecutionId,
        ProcessingTransactionLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IHardDeleteProcessingTransactionExecutor executor,
        CancellationToken ct)
    {
        var command = new HardDeleteProcessingExecutionCommand(processingExecutionId, request.ExpectedRowVersion);
        var execution = new HardDeleteProcessingExecutionExecution(
            httpContext.GetRequiredCommandId(), Hash(requestHasher, "HardDeleteProcessingExecution", command), actorContext.ActorAccountId, command);
        return ToHardDeleteResult(httpContext, await executor.HardDeleteExecutionAsync(execution, ct), "Processing Execution hard delete failed.");
    }

    private static async Task<IResult> SoftDeleteInputAsync(
        Guid processingExecutionId,
        ProcessingTransactionLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IProcessingTransactionLifecycleExecutor executor,
        CancellationToken ct)
    {
        var command = new SoftDeleteProcessingExecutionInputCommand(processingExecutionId, request.ExpectedRowVersion);
        var execution = new SoftDeleteProcessingExecutionInputExecution(
            httpContext.GetRequiredCommandId(), Hash(requestHasher, "SoftDeleteProcessingExecutionInput", command), actorContext.ActorAccountId, command);
        return ToResult(httpContext, await executor.SoftDeleteInputAsync(execution, ct), "Processing Execution Input soft delete failed.");
    }

    private static async Task<IResult> RestoreInputAsync(
        Guid processingExecutionId,
        ProcessingTransactionLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IProcessingTransactionLifecycleExecutor executor,
        CancellationToken ct)
    {
        var command = new RestoreProcessingExecutionInputCommand(processingExecutionId, request.ExpectedRowVersion);
        var execution = new RestoreProcessingExecutionInputExecution(
            httpContext.GetRequiredCommandId(), Hash(requestHasher, "RestoreProcessingExecutionInput", command), actorContext.ActorAccountId, command);
        return ToResult(httpContext, await executor.RestoreInputAsync(execution, ct), "Processing Execution Input restore failed.");
    }

    private static async Task<IResult> HardDeleteInputAsync(
        Guid processingExecutionId,
        ProcessingTransactionLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IHardDeleteProcessingTransactionExecutor executor,
        CancellationToken ct)
    {
        var command = new HardDeleteProcessingExecutionInputCommand(processingExecutionId, request.ExpectedRowVersion);
        var execution = new HardDeleteProcessingExecutionInputExecution(
            httpContext.GetRequiredCommandId(), Hash(requestHasher, "HardDeleteProcessingExecutionInput", command), actorContext.ActorAccountId, command);
        return ToHardDeleteResult(httpContext, await executor.HardDeleteInputAsync(execution, ct), "Processing Execution Input hard delete failed.");
    }

    private static async Task<IResult> SoftDeleteOutputAsync(
        Guid processingExecutionOutputId,
        ProcessingTransactionLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IProcessingTransactionLifecycleExecutor executor,
        CancellationToken ct)
    {
        var command = new SoftDeleteProcessingExecutionOutputCommand(processingExecutionOutputId, request.ExpectedRowVersion);
        var execution = new SoftDeleteProcessingExecutionOutputExecution(
            httpContext.GetRequiredCommandId(), Hash(requestHasher, "SoftDeleteProcessingExecutionOutput", command), actorContext.ActorAccountId, command);
        return ToResult(httpContext, await executor.SoftDeleteOutputAsync(execution, ct), "Processing Execution Output soft delete failed.");
    }

    private static async Task<IResult> RestoreOutputAsync(
        Guid processingExecutionOutputId,
        ProcessingTransactionLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IProcessingTransactionLifecycleExecutor executor,
        CancellationToken ct)
    {
        var command = new RestoreProcessingExecutionOutputCommand(processingExecutionOutputId, request.ExpectedRowVersion);
        var execution = new RestoreProcessingExecutionOutputExecution(
            httpContext.GetRequiredCommandId(), Hash(requestHasher, "RestoreProcessingExecutionOutput", command), actorContext.ActorAccountId, command);
        return ToResult(httpContext, await executor.RestoreOutputAsync(execution, ct), "Processing Execution Output restore failed.");
    }

    private static async Task<IResult> HardDeleteOutputAsync(
        Guid processingExecutionOutputId,
        ProcessingTransactionLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IHardDeleteProcessingTransactionExecutor executor,
        CancellationToken ct)
    {
        var command = new HardDeleteProcessingExecutionOutputCommand(processingExecutionOutputId, request.ExpectedRowVersion);
        var execution = new HardDeleteProcessingExecutionOutputExecution(
            httpContext.GetRequiredCommandId(), Hash(requestHasher, "HardDeleteProcessingExecutionOutput", command), actorContext.ActorAccountId, command);
        return ToHardDeleteResult(httpContext, await executor.HardDeleteOutputAsync(execution, ct), "Processing Execution Output hard delete failed.");
    }

    private static IResult ToResult(
        HttpContext httpContext,
        Application.Common.Results.ApplicationResult<ProcessingTransactionLifecycleResult> result,
        string title) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : Failure(httpContext, result.Error, title);

    private static IResult ToHardDeleteResult(
        HttpContext httpContext,
        Application.Common.Results.ApplicationResult<HardDeleteProcessingTransactionResult> result,
        string title) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : Failure(httpContext, result.Error, title);

    private static CommandRequestHash Hash<TCommand>(ICommandRequestHasher requestHasher, string commandType, TCommand command) =>
        requestHasher.Compute(JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(new CanonicalCommand<TCommand>(commandType, command), CanonicalJsonOptions)));

    private static IResult Failure(HttpContext httpContext, ApplicationError error, string title)
    {
        var status = error.Kind switch
        {
            ApplicationErrorKind.Validation => StatusCodes.Status422UnprocessableEntity,
            ApplicationErrorKind.NotFound => StatusCodes.Status404NotFound,
            ApplicationErrorKind.Conflict => StatusCodes.Status409Conflict,
            ApplicationErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            _ => throw new ArgumentOutOfRangeException(nameof(error.Kind), error.Kind, "Unsupported application error kind."),
        };
        return ApiProblemResults.Create(httpContext, status, error.Code, title);
    }

    private sealed record CanonicalCommand<TCommand>(string CommandType, TCommand Command);
}

public sealed record ProcessingTransactionLifecycleRequest(long ExpectedRowVersion);
