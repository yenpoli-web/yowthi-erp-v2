using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Idempotency;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Serialization;
using YowThi.Erp.Application.Infrastructure;

namespace YowThi.Erp.Api.Infrastructure;

public static class WarehouseMasterEndpoints
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapWarehouseMasterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapApiV1().MapGroup("/infrastructure");
        group.MapGet("/warehouses", ListAsync)
            .WithName("Infrastructure_ListWarehouses")
            .RequireAuthorization(CapabilityPolicies.InfrastructureWarehouseManage)
            .Produces<WarehouseMasterPage>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/warehouses", CreateAsync)
            .WithName("Infrastructure_CreateWarehouse")
            .RequireAuthorization(CapabilityPolicies.InfrastructureWarehouseManage)
            .RequireIdempotencyKey()
            .Produces<WarehouseMasterWriteResult>(StatusCodes.Status200OK);

        group.MapPost("/warehouses/{warehouseId:guid}/update", UpdateAsync)
            .WithName("Infrastructure_UpdateWarehouse")
            .RequireAuthorization(CapabilityPolicies.InfrastructureWarehouseManage)
            .RequireIdempotencyKey()
            .Produces<WarehouseMasterWriteResult>(StatusCodes.Status200OK);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        [FromServices] IWarehouseMasterReader reader,
        string? search,
        string? status,
        int offset = 0,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0 || limit is < 1 or > 200 || !InfrastructureMasterEndpointSupport.TryParseStatus(status, out var parsedStatus))
            return TypedResults.BadRequest();
        return TypedResults.Ok(await reader.GetAsync(new WarehouseMasterQuery(search, parsedStatus, offset, limit), cancellationToken));
    }

    private static async Task<IResult> CreateAsync(
        CreateWarehouseRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IWarehouseMasterExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new CreateWarehouseCommand(
            InfrastructureMasterEndpointSupport.Normalize(request.Code),
            InfrastructureMasterEndpointSupport.Normalize(request.NameZhTw),
            InfrastructureMasterEndpointSupport.Normalize(request.NameThTh),
            request.Active);
        var payload = JsonPayload.FromUtf8Json(JsonSerializer.SerializeToUtf8Bytes(new { commandType = "CreateWarehouse", command }, CanonicalJsonOptions));
        var result = await executor.CreateAsync(new CreateWarehouseExecution(
            httpContext.GetRequiredCommandId(), requestHasher.Compute(payload), actorContext.ActorAccountId, command), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : InfrastructureMasterEndpointSupport.Failure(httpContext, result.Error, "Warehouse create failed.");
    }

    private static async Task<IResult> UpdateAsync(
        Guid warehouseId,
        UpdateWarehouseRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IWarehouseMasterExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new UpdateWarehouseCommand(
            warehouseId,
            request.ExpectedRowVersion,
            InfrastructureMasterEndpointSupport.Normalize(request.Code),
            InfrastructureMasterEndpointSupport.Normalize(request.NameZhTw),
            InfrastructureMasterEndpointSupport.Normalize(request.NameThTh),
            request.Active);
        var payload = JsonPayload.FromUtf8Json(JsonSerializer.SerializeToUtf8Bytes(new { commandType = "UpdateWarehouse", command }, CanonicalJsonOptions));
        var result = await executor.UpdateAsync(new UpdateWarehouseExecution(
            httpContext.GetRequiredCommandId(), requestHasher.Compute(payload), actorContext.ActorAccountId, command), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : InfrastructureMasterEndpointSupport.Failure(httpContext, result.Error, "Warehouse update failed.");
    }
}

public sealed record CreateWarehouseRequest(string? Code, string? NameZhTw, string? NameThTh, bool Active);
public sealed record UpdateWarehouseRequest(long ExpectedRowVersion, string? Code, string? NameZhTw, string? NameThTh, bool Active);
