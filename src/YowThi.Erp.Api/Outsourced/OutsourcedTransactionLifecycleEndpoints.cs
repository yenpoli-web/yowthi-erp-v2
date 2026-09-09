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

public static class OutsourcedTransactionLifecycleEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapOutsourcedTransactionLifecycleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapApiV1().MapGroup("/outsourced");

        MapLifecycle(group, "/batches/{outsourcedSupplyBatchId:guid}/soft-delete", "Outsourced_SoftDeleteBatch", SoftDeleteBatchAsync, true);
        MapLifecycle(group, "/batches/{outsourcedSupplyBatchId:guid}/restore", "Outsourced_RestoreBatch", RestoreBatchAsync, false);
        MapHardDelete(group, "/batches/{outsourcedSupplyBatchId:guid}/hard-delete", "Outsourced_HardDeleteBatch", HardDeleteBatchAsync);

        MapLifecycle(group, "/supply-details/{outsourcedSupplyDetailId:guid}/soft-delete", "Outsourced_SoftDeleteDetail", SoftDeleteDetailAsync, true);
        MapLifecycle(group, "/supply-details/{outsourcedSupplyDetailId:guid}/restore", "Outsourced_RestoreDetail", RestoreDetailAsync, false);
        MapHardDelete(group, "/supply-details/{outsourcedSupplyDetailId:guid}/hard-delete", "Outsourced_HardDeleteDetail", HardDeleteDetailAsync);

        return endpoints;
    }

    private static void MapLifecycle(RouteGroupBuilder group, string pattern, string name, Delegate handler, bool reauthentication)
    {
        var endpoint = group.MapPost(pattern, handler)
            .WithName(name)
            .RequireAuthorization(CapabilityPolicies.OutsourcedTransactionLifecycle)
            .RequireIdempotencyKey()
            .Produces<OutsourcedTransactionLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        if (reauthentication)
        {
            endpoint.RequireRecentDeletionReauthentication();
        }
    }

    private static void MapHardDelete(RouteGroupBuilder group, string pattern, string name, Delegate handler)
    {
        group.MapPost(pattern, handler)
            .WithName(name)
            .RequireAuthorization(CapabilityPolicies.OutsourcedTransactionLifecycle)
            .RequireAuthorization(CapabilityPolicies.DataProtectionHardDelete)
            .RequireIdempotencyKey()
            .RequireRecentDeletionReauthentication()
            .Produces<HardDeleteOutsourcedTransactionResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> SoftDeleteBatchAsync(
        Guid outsourcedSupplyBatchId,
        OutsourcedTransactionLifecycleRequest request,
        HttpContext context,
        [FromServices] IActorContext actor,
        [FromServices] ICommandRequestHasher hasher,
        [FromServices] IOutsourcedTransactionLifecycleExecutor executor,
        CancellationToken ct)
    {
        var command = new SoftDeleteOutsourcedSupplyBatchCommand(outsourcedSupplyBatchId, request.ExpectedRowVersion);
        var execution = new SoftDeleteOutsourcedSupplyBatchExecution(context.GetRequiredCommandId(), Hash(hasher, "SoftDeleteOutsourcedSupplyBatch", command), actor.ActorAccountId, command);
        return ToLifecycleResult(context, await executor.SoftDeleteBatchAsync(execution, ct), "Outsourced Supply Batch soft delete failed.");
    }

    private static async Task<IResult> RestoreBatchAsync(
        Guid outsourcedSupplyBatchId,
        OutsourcedTransactionLifecycleRequest request,
        HttpContext context,
        [FromServices] IActorContext actor,
        [FromServices] ICommandRequestHasher hasher,
        [FromServices] IOutsourcedTransactionLifecycleExecutor executor,
        CancellationToken ct)
    {
        var command = new RestoreOutsourcedSupplyBatchCommand(outsourcedSupplyBatchId, request.ExpectedRowVersion);
        var execution = new RestoreOutsourcedSupplyBatchExecution(context.GetRequiredCommandId(), Hash(hasher, "RestoreOutsourcedSupplyBatch", command), actor.ActorAccountId, command);
        return ToLifecycleResult(context, await executor.RestoreBatchAsync(execution, ct), "Outsourced Supply Batch restore failed.");
    }

    private static async Task<IResult> HardDeleteBatchAsync(
        Guid outsourcedSupplyBatchId,
        OutsourcedTransactionLifecycleRequest request,
        HttpContext context,
        [FromServices] IActorContext actor,
        [FromServices] ICommandRequestHasher hasher,
        [FromServices] IHardDeleteOutsourcedTransactionExecutor executor,
        CancellationToken ct)
    {
        var command = new HardDeleteOutsourcedSupplyBatchCommand(outsourcedSupplyBatchId, request.ExpectedRowVersion);
        var execution = new HardDeleteOutsourcedSupplyBatchExecution(context.GetRequiredCommandId(), Hash(hasher, "HardDeleteOutsourcedSupplyBatch", command), actor.ActorAccountId, command);
        return ToHardDeleteResult(context, await executor.HardDeleteBatchAsync(execution, ct), "Outsourced Supply Batch hard delete failed.");
    }

    private static async Task<IResult> SoftDeleteDetailAsync(
        Guid outsourcedSupplyDetailId,
        OutsourcedTransactionLifecycleRequest request,
        HttpContext context,
        [FromServices] IActorContext actor,
        [FromServices] ICommandRequestHasher hasher,
        [FromServices] IOutsourcedTransactionLifecycleExecutor executor,
        CancellationToken ct)
    {
        var command = new SoftDeleteOutsourcedSupplyDetailCommand(outsourcedSupplyDetailId, request.ExpectedRowVersion);
        var execution = new SoftDeleteOutsourcedSupplyDetailExecution(context.GetRequiredCommandId(), Hash(hasher, "SoftDeleteOutsourcedSupplyDetail", command), actor.ActorAccountId, command);
        return ToLifecycleResult(context, await executor.SoftDeleteDetailAsync(execution, ct), "Outsourced Supply Detail soft delete failed.");
    }

    private static async Task<IResult> RestoreDetailAsync(
        Guid outsourcedSupplyDetailId,
        OutsourcedTransactionLifecycleRequest request,
        HttpContext context,
        [FromServices] IActorContext actor,
        [FromServices] ICommandRequestHasher hasher,
        [FromServices] IOutsourcedTransactionLifecycleExecutor executor,
        CancellationToken ct)
    {
        var command = new RestoreOutsourcedSupplyDetailCommand(outsourcedSupplyDetailId, request.ExpectedRowVersion);
        var execution = new RestoreOutsourcedSupplyDetailExecution(context.GetRequiredCommandId(), Hash(hasher, "RestoreOutsourcedSupplyDetail", command), actor.ActorAccountId, command);
        return ToLifecycleResult(context, await executor.RestoreDetailAsync(execution, ct), "Outsourced Supply Detail restore failed.");
    }

    private static async Task<IResult> HardDeleteDetailAsync(
        Guid outsourcedSupplyDetailId,
        OutsourcedTransactionLifecycleRequest request,
        HttpContext context,
        [FromServices] IActorContext actor,
        [FromServices] ICommandRequestHasher hasher,
        [FromServices] IHardDeleteOutsourcedTransactionExecutor executor,
        CancellationToken ct)
    {
        var command = new HardDeleteOutsourcedSupplyDetailCommand(outsourcedSupplyDetailId, request.ExpectedRowVersion);
        var execution = new HardDeleteOutsourcedSupplyDetailExecution(context.GetRequiredCommandId(), Hash(hasher, "HardDeleteOutsourcedSupplyDetail", command), actor.ActorAccountId, command);
        return ToHardDeleteResult(context, await executor.HardDeleteDetailAsync(execution, ct), "Outsourced Supply Detail hard delete failed.");
    }

    private static IResult ToLifecycleResult(
        HttpContext context,
        Application.Common.Results.ApplicationResult<OutsourcedTransactionLifecycleResult> result,
        string title) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : Failure(context, result.Error, title);

    private static IResult ToHardDeleteResult(
        HttpContext context,
        Application.Common.Results.ApplicationResult<HardDeleteOutsourcedTransactionResult> result,
        string title) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : Failure(context, result.Error, title);

    private static CommandRequestHash Hash<T>(ICommandRequestHasher hasher, string commandType, T command) =>
        hasher.Compute(JsonPayload.FromUtf8Json(JsonSerializer.SerializeToUtf8Bytes(new Canonical<T>(commandType, command), JsonOptions)));

    private static IResult Failure(HttpContext context, ApplicationError error, string title)
    {
        var status = error.Kind switch
        {
            ApplicationErrorKind.Validation => StatusCodes.Status422UnprocessableEntity,
            ApplicationErrorKind.NotFound => StatusCodes.Status404NotFound,
            ApplicationErrorKind.Conflict => StatusCodes.Status409Conflict,
            ApplicationErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            _ => throw new ArgumentOutOfRangeException(nameof(error.Kind), error.Kind, "Unsupported application error kind."),
        };
        return ApiProblemResults.Create(context, status, error.Code, title);
    }

    private sealed record Canonical<T>(string CommandType, T Command);
}

public sealed record OutsourcedTransactionLifecycleRequest(long ExpectedRowVersion);
