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

public static class ProductMasterLifecycleEndpoints
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapProductMasterLifecycleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var api = endpoints.MapApiV1();
        var procurement = api.MapGroup("/product/procurement-products");
        procurement.MapPost("/{procurementProductId:guid}/soft-delete", SoftDeleteProcurementProductAsync)
            .WithName("Product_SoftDeleteProcurementProduct")
            .RequireAuthorization(CapabilityPolicies.ProcurementProductManage)
            .RequireIdempotencyKey()
            .RequireRecentDeletionReauthentication()
            .Produces<ProcurementProductLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        procurement.MapPost("/{procurementProductId:guid}/restore", RestoreProcurementProductAsync)
            .WithName("Product_RestoreProcurementProduct")
            .RequireAuthorization(CapabilityPolicies.ProcurementProductManage)
            .RequireIdempotencyKey()
            .Produces<ProcurementProductLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        var sales = api.MapGroup("/product/sales-products");
        sales.MapPost("/{salesProductId:guid}/soft-delete", SoftDeleteSalesProductAsync)
            .WithName("Product_SoftDeleteSalesProduct")
            .RequireAuthorization(CapabilityPolicies.SalesProductManage)
            .RequireIdempotencyKey()
            .RequireRecentDeletionReauthentication()
            .Produces<SalesProductLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        sales.MapPost("/{salesProductId:guid}/restore", RestoreSalesProductAsync)
            .WithName("Product_RestoreSalesProduct")
            .RequireAuthorization(CapabilityPolicies.SalesProductManage)
            .RequireIdempotencyKey()
            .Produces<SalesProductLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> SoftDeleteProcurementProductAsync(
        Guid procurementProductId,
        ProductMasterLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IProcurementProductLifecycleExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new SoftDeleteProcurementProductCommand(procurementProductId, request.ExpectedRowVersion);
        var execution = new SoftDeleteProcurementProductExecution(
            httpContext.GetRequiredCommandId(),
            Hash(requestHasher, "SoftDeleteProcurementProduct", command),
            actorContext.ActorAccountId,
            command);
        return ToResult(httpContext, await executor.SoftDeleteAsync(execution, cancellationToken), "Procurement Product soft delete failed.");
    }

    private static async Task<IResult> RestoreProcurementProductAsync(
        Guid procurementProductId,
        ProductMasterLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IProcurementProductLifecycleExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new RestoreProcurementProductCommand(procurementProductId, request.ExpectedRowVersion);
        var execution = new RestoreProcurementProductExecution(
            httpContext.GetRequiredCommandId(),
            Hash(requestHasher, "RestoreProcurementProduct", command),
            actorContext.ActorAccountId,
            command);
        return ToResult(httpContext, await executor.RestoreAsync(execution, cancellationToken), "Procurement Product restore failed.");
    }

    private static async Task<IResult> SoftDeleteSalesProductAsync(
        Guid salesProductId,
        ProductMasterLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ISalesProductLifecycleExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new SoftDeleteSalesProductCommand(salesProductId, request.ExpectedRowVersion);
        var execution = new SoftDeleteSalesProductExecution(
            httpContext.GetRequiredCommandId(),
            Hash(requestHasher, "SoftDeleteSalesProduct", command),
            actorContext.ActorAccountId,
            command);
        return ToResult(httpContext, await executor.SoftDeleteAsync(execution, cancellationToken), "Sales Product soft delete failed.");
    }

    private static async Task<IResult> RestoreSalesProductAsync(
        Guid salesProductId,
        ProductMasterLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ISalesProductLifecycleExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new RestoreSalesProductCommand(salesProductId, request.ExpectedRowVersion);
        var execution = new RestoreSalesProductExecution(
            httpContext.GetRequiredCommandId(),
            Hash(requestHasher, "RestoreSalesProduct", command),
            actorContext.ActorAccountId,
            command);
        return ToResult(httpContext, await executor.RestoreAsync(execution, cancellationToken), "Sales Product restore failed.");
    }

    private static CommandRequestHash Hash<TCommand>(ICommandRequestHasher requestHasher, string commandType, TCommand command) =>
        requestHasher.Compute(JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(new CanonicalCommand<TCommand>(commandType, command), CanonicalJsonOptions)));

    private static IResult ToResult<TResult>(
        HttpContext httpContext,
        Application.Common.Results.ApplicationResult<TResult> result,
        string title) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : Failure(httpContext, result.Error, title);

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

public sealed record ProductMasterLifecycleRequest(long ExpectedRowVersion);
