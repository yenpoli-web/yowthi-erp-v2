using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Idempotency;
using YowThi.Erp.Api.Localization;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Serialization;
using YowThi.Erp.Application.Product;

namespace YowThi.Erp.Api.Product;

public static class ProcurementProductMasterEndpoints
{
    public const string ListOperationId = "Product_ListProcurementProducts";
    public const string CreateOperationId = "Product_CreateProcurementProduct";
    public const string UpdateOperationId = "Product_UpdateProcurementProduct";
    public const string CreateCommandType = "CreateProcurementProduct";
    public const string UpdateCommandType = "UpdateProcurementProduct";
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapProcurementProductMasterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var product = endpoints.MapApiV1().MapGroup("/product");

        product.MapGet("/procurement-products", ListAsync)
            .WithName(ListOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcurementProductManage)
            .Produces<ProcurementProductMasterPage>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        product.MapPost("/procurement-products", CreateAsync)
            .WithName(CreateOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcurementProductManage)
            .RequireIdempotencyKey()
            .Produces<ProcurementProductMasterWriteResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        product.MapPost("/procurement-products/{procurementProductId:guid}/update", UpdateAsync)
            .WithName(UpdateOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcurementProductManage)
            .RequireIdempotencyKey()
            .Produces<ProcurementProductMasterWriteResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IProcurementProductMasterReader reader,
        string? search,
        string? status,
        int offset = 0,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0 || limit is < 1 or > 200 || !ProductMasterEndpointSupport.TryParseStatus(status, out var parsedStatus))
        {
            return TypedResults.BadRequest();
        }

        return TypedResults.Ok(await reader.GetAsync(
            new ProductMasterQuery(search, parsedStatus, offset, limit, localeResolver.Resolve(httpContext.Request)),
            cancellationToken));
    }

    private static async Task<IResult> CreateAsync(
        CreateProcurementProductRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IProcurementProductMasterExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new CreateProcurementProductCommand(
            ProductMasterEndpointSupport.Normalize(request.NameZhTw),
            ProductMasterEndpointSupport.Normalize(request.NameThTh),
            request.UnitCode?.Trim() ?? string.Empty,
            request.DefaultStorageLocationId,
            request.Active);
        var payload = JsonPayload.FromUtf8Json(JsonSerializer.SerializeToUtf8Bytes(
            new CanonicalCreateRequest(CreateCommandType, command), CanonicalJsonOptions));
        var execution = new CreateProcurementProductExecution(
            httpContext.GetRequiredCommandId(), requestHasher.Compute(payload), actorContext.ActorAccountId, command);
        var result = await executor.CreateAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : ProductMasterEndpointSupport.Failure(httpContext, result.Error, "Procurement Product create failed.");
    }

    private static async Task<IResult> UpdateAsync(
        Guid procurementProductId,
        UpdateProcurementProductRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IProcurementProductMasterExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new UpdateProcurementProductCommand(
            procurementProductId,
            request.ExpectedRowVersion,
            ProductMasterEndpointSupport.Normalize(request.NameZhTw),
            ProductMasterEndpointSupport.Normalize(request.NameThTh),
            request.UnitCode?.Trim() ?? string.Empty,
            request.DefaultStorageLocationId,
            request.Active);
        var payload = JsonPayload.FromUtf8Json(JsonSerializer.SerializeToUtf8Bytes(
            new CanonicalUpdateRequest(UpdateCommandType, command), CanonicalJsonOptions));
        var execution = new UpdateProcurementProductExecution(
            httpContext.GetRequiredCommandId(), requestHasher.Compute(payload), actorContext.ActorAccountId, command);
        var result = await executor.UpdateAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : ProductMasterEndpointSupport.Failure(httpContext, result.Error, "Procurement Product update failed.");
    }

    private sealed record CanonicalCreateRequest(string CommandType, CreateProcurementProductCommand Command);
    private sealed record CanonicalUpdateRequest(string CommandType, UpdateProcurementProductCommand Command);
}

public sealed record CreateProcurementProductRequest(
    string? NameZhTw,
    string? NameThTh,
    string? UnitCode,
    Guid? DefaultStorageLocationId,
    bool Active);

public sealed record UpdateProcurementProductRequest(
    long ExpectedRowVersion,
    string? NameZhTw,
    string? NameThTh,
    string? UnitCode,
    Guid? DefaultStorageLocationId,
    bool Active);
