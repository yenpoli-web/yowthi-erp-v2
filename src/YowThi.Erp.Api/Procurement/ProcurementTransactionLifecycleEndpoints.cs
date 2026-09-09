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
using YowThi.Erp.Application.Procurement;

namespace YowThi.Erp.Api.Procurement;

public static class ProcurementTransactionLifecycleEndpoints
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapProcurementTransactionLifecycleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapApiV1().MapGroup("/procurement");

        group.MapPost("/batches/{procurementBatchId:guid}/soft-delete", SoftDeleteBatchAsync)
            .WithName("Procurement_SoftDeleteBatch")
            .RequireAuthorization(CapabilityPolicies.ProcurementTransactionLifecycle)
            .RequireIdempotencyKey()
            .RequireRecentDeletionReauthentication()
            .Produces<ProcurementTransactionLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/batches/{procurementBatchId:guid}/restore", RestoreBatchAsync)
            .WithName("Procurement_RestoreBatch")
            .RequireAuthorization(CapabilityPolicies.ProcurementTransactionLifecycle)
            .RequireIdempotencyKey()
            .Produces<ProcurementTransactionLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/batches/{procurementBatchId:guid}/hard-delete", HardDeleteBatchAsync)
            .WithName("Procurement_HardDeleteBatch")
            .RequireAuthorization(CapabilityPolicies.ProcurementTransactionLifecycle)
            .RequireAuthorization(CapabilityPolicies.DataProtectionHardDelete)
            .RequireIdempotencyKey()
            .RequireRecentDeletionReauthentication()
            .Produces<HardDeleteProcurementBatchResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/entries/{procurementEntryId:guid}/soft-delete", SoftDeleteEntryAsync)
            .WithName("Procurement_SoftDeleteEntry")
            .RequireAuthorization(CapabilityPolicies.ProcurementTransactionLifecycle)
            .RequireIdempotencyKey()
            .RequireRecentDeletionReauthentication()
            .Produces<ProcurementTransactionLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/entries/{procurementEntryId:guid}/restore", RestoreEntryAsync)
            .WithName("Procurement_RestoreEntry")
            .RequireAuthorization(CapabilityPolicies.ProcurementTransactionLifecycle)
            .RequireIdempotencyKey()
            .Produces<ProcurementTransactionLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/entries/{procurementEntryId:guid}/hard-delete", HardDeleteEntryAsync)
            .WithName("Procurement_HardDeleteEntry")
            .RequireAuthorization(CapabilityPolicies.ProcurementTransactionLifecycle)
            .RequireAuthorization(CapabilityPolicies.DataProtectionHardDelete)
            .RequireIdempotencyKey()
            .RequireRecentDeletionReauthentication()
            .Produces<HardDeleteProcurementEntryResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> SoftDeleteBatchAsync(
        Guid procurementBatchId,
        ProcurementTransactionLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IProcurementTransactionLifecycleExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new SoftDeleteProcurementBatchCommand(procurementBatchId, request.ExpectedRowVersion);
        var execution = new SoftDeleteProcurementBatchExecution(
            httpContext.GetRequiredCommandId(),
            Hash(requestHasher, "SoftDeleteProcurementBatch", command),
            actorContext.ActorAccountId,
            command);
        return ToResult(httpContext, await executor.SoftDeleteBatchAsync(execution, cancellationToken), "Procurement Batch soft delete failed.");
    }

    private static async Task<IResult> RestoreBatchAsync(
        Guid procurementBatchId,
        ProcurementTransactionLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IProcurementTransactionLifecycleExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new RestoreProcurementBatchCommand(procurementBatchId, request.ExpectedRowVersion);
        var execution = new RestoreProcurementBatchExecution(
            httpContext.GetRequiredCommandId(),
            Hash(requestHasher, "RestoreProcurementBatch", command),
            actorContext.ActorAccountId,
            command);
        return ToResult(httpContext, await executor.RestoreBatchAsync(execution, cancellationToken), "Procurement Batch restore failed.");
    }

    private static async Task<IResult> HardDeleteBatchAsync(
        Guid procurementBatchId,
        ProcurementTransactionLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IHardDeleteProcurementBatchExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new HardDeleteProcurementBatchCommand(procurementBatchId, request.ExpectedRowVersion);
        var execution = new HardDeleteProcurementBatchExecution(
            httpContext.GetRequiredCommandId(),
            Hash(requestHasher, "HardDeleteProcurementBatch", command),
            actorContext.ActorAccountId,
            command);
        var result = await executor.ExecuteAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : Failure(httpContext, result.Error, "Procurement Batch hard delete failed.");
    }

    private static async Task<IResult> SoftDeleteEntryAsync(
        Guid procurementEntryId,
        ProcurementTransactionLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IProcurementTransactionLifecycleExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new SoftDeleteProcurementEntryCommand(procurementEntryId, request.ExpectedRowVersion);
        var execution = new SoftDeleteProcurementEntryExecution(
            httpContext.GetRequiredCommandId(),
            Hash(requestHasher, "SoftDeleteProcurementEntry", command),
            actorContext.ActorAccountId,
            command);
        return ToResult(httpContext, await executor.SoftDeleteEntryAsync(execution, cancellationToken), "Procurement Entry soft delete failed.");
    }

    private static async Task<IResult> RestoreEntryAsync(
        Guid procurementEntryId,
        ProcurementTransactionLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IProcurementTransactionLifecycleExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new RestoreProcurementEntryCommand(procurementEntryId, request.ExpectedRowVersion);
        var execution = new RestoreProcurementEntryExecution(
            httpContext.GetRequiredCommandId(),
            Hash(requestHasher, "RestoreProcurementEntry", command),
            actorContext.ActorAccountId,
            command);
        return ToResult(httpContext, await executor.RestoreEntryAsync(execution, cancellationToken), "Procurement Entry restore failed.");
    }

    private static async Task<IResult> HardDeleteEntryAsync(
        Guid procurementEntryId,
        ProcurementTransactionLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IHardDeleteProcurementEntryExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new HardDeleteProcurementEntryCommand(procurementEntryId, request.ExpectedRowVersion);
        var execution = new HardDeleteProcurementEntryExecution(
            httpContext.GetRequiredCommandId(),
            Hash(requestHasher, "HardDeleteProcurementEntry", command),
            actorContext.ActorAccountId,
            command);
        var result = await executor.ExecuteAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : Failure(httpContext, result.Error, "Procurement Entry hard delete failed.");
    }

    private static IResult ToResult(
        HttpContext httpContext,
        Application.Common.Results.ApplicationResult<ProcurementTransactionLifecycleResult> result,
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

public sealed record ProcurementTransactionLifecycleRequest(long ExpectedRowVersion);
