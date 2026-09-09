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
using YowThi.Erp.Application.Sales;

namespace YowThi.Erp.Api.Sales;

public static class SalesTransactionLifecycleEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapSalesTransactionLifecycleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapApiV1().MapGroup("/sales");

        MapLifecycle(group, "/{salesId:guid}/soft-delete", "Sales_SoftDeleteSale", SoftDeleteSaleAsync, true);
        MapLifecycle(group, "/{salesId:guid}/restore", "Sales_RestoreSale", RestoreSaleAsync, false);
        MapHardDelete(group, "/{salesId:guid}/hard-delete", "Sales_HardDeleteSale", HardDeleteSaleAsync);

        MapLifecycle(group, "/details/{salesDetailId:guid}/soft-delete", "Sales_SoftDeleteDetail", SoftDeleteDetailAsync, true);
        MapLifecycle(group, "/details/{salesDetailId:guid}/restore", "Sales_RestoreDetail", RestoreDetailAsync, false);
        MapHardDelete(group, "/details/{salesDetailId:guid}/hard-delete", "Sales_HardDeleteDetail", HardDeleteDetailAsync);

        return endpoints;
    }

    private static void MapLifecycle(RouteGroupBuilder group, string pattern, string name, Delegate handler, bool reauth)
    {
        var endpoint = group.MapPost(pattern, handler)
            .WithName(name)
            .RequireAuthorization(CapabilityPolicies.SalesTransactionLifecycle)
            .RequireIdempotencyKey()
            .Produces<SalesTransactionLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        if (reauth) endpoint.RequireRecentDeletionReauthentication();
    }

    private static void MapHardDelete(RouteGroupBuilder group, string pattern, string name, Delegate handler)
    {
        group.MapPost(pattern, handler)
            .WithName(name)
            .RequireAuthorization(CapabilityPolicies.SalesTransactionLifecycle)
            .RequireAuthorization(CapabilityPolicies.DataProtectionHardDelete)
            .RequireIdempotencyKey()
            .RequireRecentDeletionReauthentication()
            .Produces<HardDeleteSalesTransactionResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> SoftDeleteSaleAsync(Guid salesId, SalesTransactionLifecycleRequest request, HttpContext context,
        [FromServices] IActorContext actor, [FromServices] ICommandRequestHasher hasher,
        [FromServices] ISalesTransactionLifecycleExecutor executor, CancellationToken ct)
    {
        var command = new SoftDeleteSaleCommand(salesId, request.ExpectedRowVersion);
        var execution = new SoftDeleteSaleExecution(context.GetRequiredCommandId(), Hash(hasher, "SoftDeleteSale", command), actor.ActorAccountId, command);
        return ToResult(context, await executor.SoftDeleteSaleAsync(execution, ct), "Sales soft delete failed.");
    }

    private static async Task<IResult> RestoreSaleAsync(Guid salesId, SalesTransactionLifecycleRequest request, HttpContext context,
        [FromServices] IActorContext actor, [FromServices] ICommandRequestHasher hasher,
        [FromServices] ISalesTransactionLifecycleExecutor executor, CancellationToken ct)
    {
        var command = new RestoreSaleCommand(salesId, request.ExpectedRowVersion);
        var execution = new RestoreSaleExecution(context.GetRequiredCommandId(), Hash(hasher, "RestoreSale", command), actor.ActorAccountId, command);
        return ToResult(context, await executor.RestoreSaleAsync(execution, ct), "Sales restore failed.");
    }

    private static async Task<IResult> HardDeleteSaleAsync(Guid salesId, SalesTransactionLifecycleRequest request, HttpContext context,
        [FromServices] IActorContext actor, [FromServices] ICommandRequestHasher hasher,
        [FromServices] IHardDeleteSalesTransactionExecutor executor, CancellationToken ct)
    {
        var command = new HardDeleteSaleCommand(salesId, request.ExpectedRowVersion);
        var execution = new HardDeleteSaleExecution(context.GetRequiredCommandId(), Hash(hasher, "HardDeleteSale", command), actor.ActorAccountId, command);
        return ToHardDeleteResult(context, await executor.HardDeleteSaleAsync(execution, ct), "Sales hard delete failed.");
    }

    private static async Task<IResult> SoftDeleteDetailAsync(Guid salesDetailId, SalesTransactionLifecycleRequest request, HttpContext context,
        [FromServices] IActorContext actor, [FromServices] ICommandRequestHasher hasher,
        [FromServices] ISalesTransactionLifecycleExecutor executor, CancellationToken ct)
    {
        var command = new SoftDeleteSalesDetailCommand(salesDetailId, request.ExpectedRowVersion);
        var execution = new SoftDeleteSalesDetailExecution(context.GetRequiredCommandId(), Hash(hasher, "SoftDeleteSalesDetail", command), actor.ActorAccountId, command);
        return ToResult(context, await executor.SoftDeleteDetailAsync(execution, ct), "Sales detail soft delete failed.");
    }

    private static async Task<IResult> RestoreDetailAsync(Guid salesDetailId, SalesTransactionLifecycleRequest request, HttpContext context,
        [FromServices] IActorContext actor, [FromServices] ICommandRequestHasher hasher,
        [FromServices] ISalesTransactionLifecycleExecutor executor, CancellationToken ct)
    {
        var command = new RestoreSalesDetailCommand(salesDetailId, request.ExpectedRowVersion);
        var execution = new RestoreSalesDetailExecution(context.GetRequiredCommandId(), Hash(hasher, "RestoreSalesDetail", command), actor.ActorAccountId, command);
        return ToResult(context, await executor.RestoreDetailAsync(execution, ct), "Sales detail restore failed.");
    }

    private static async Task<IResult> HardDeleteDetailAsync(Guid salesDetailId, SalesTransactionLifecycleRequest request, HttpContext context,
        [FromServices] IActorContext actor, [FromServices] ICommandRequestHasher hasher,
        [FromServices] IHardDeleteSalesTransactionExecutor executor, CancellationToken ct)
    {
        var command = new HardDeleteSalesDetailCommand(salesDetailId, request.ExpectedRowVersion);
        var execution = new HardDeleteSalesDetailExecution(context.GetRequiredCommandId(), Hash(hasher, "HardDeleteSalesDetail", command), actor.ActorAccountId, command);
        return ToHardDeleteResult(context, await executor.HardDeleteDetailAsync(execution, ct), "Sales detail hard delete failed.");
    }

    private static IResult ToResult(HttpContext context, Application.Common.Results.ApplicationResult<SalesTransactionLifecycleResult> result, string title) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : Failure(context, result.Error, title);

    private static IResult ToHardDeleteResult(HttpContext context, Application.Common.Results.ApplicationResult<HardDeleteSalesTransactionResult> result, string title) =>
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

public sealed record SalesTransactionLifecycleRequest(long ExpectedRowVersion);
