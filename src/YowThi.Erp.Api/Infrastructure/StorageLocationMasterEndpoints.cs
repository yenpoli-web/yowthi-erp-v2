using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Idempotency;
using YowThi.Erp.Api.Localization;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Serialization;
using YowThi.Erp.Application.Infrastructure;

namespace YowThi.Erp.Api.Infrastructure;

public static class StorageLocationMasterEndpoints
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapStorageLocationMasterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapApiV1().MapGroup("/infrastructure");

        group.MapGet("/storage-locations", ListAsync)
            .WithName("Infrastructure_ListStorageLocations")
            .RequireAuthorization(CapabilityPolicies.InfrastructureStorageLocationManage)
            .Produces<StorageLocationMasterPage>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/storage-locations/warehouse-options", WarehouseOptionsAsync)
            .WithName("Infrastructure_ListStorageLocationWarehouseOptions")
            .RequireAuthorization(CapabilityPolicies.InfrastructureStorageLocationManage)
            .Produces<WarehouseMasterOptions>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/storage-locations", CreateAsync)
            .WithName("Infrastructure_CreateStorageLocation")
            .RequireAuthorization(CapabilityPolicies.InfrastructureStorageLocationManage)
            .RequireIdempotencyKey()
            .Produces<StorageLocationMasterWriteResult>(StatusCodes.Status200OK);

        group.MapPost("/storage-locations/{storageLocationId:guid}/update", UpdateAsync)
            .WithName("Infrastructure_UpdateStorageLocation")
            .RequireAuthorization(CapabilityPolicies.InfrastructureStorageLocationManage)
            .RequireIdempotencyKey()
            .Produces<StorageLocationMasterWriteResult>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IStorageLocationMasterReader reader,
        string? search,
        string? status,
        Guid? warehouseId,
        int offset = 0,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0 || limit is < 1 or > 200 || warehouseId == Guid.Empty
            || !InfrastructureMasterEndpointSupport.TryParseStatus(status, out var parsedStatus))
        {
            return TypedResults.BadRequest();
        }

        return TypedResults.Ok(await reader.GetAsync(
            new StorageLocationMasterQuery(search, parsedStatus, warehouseId, offset, limit, localeResolver.Resolve(httpContext.Request)),
            cancellationToken));
    }

    private static async Task<IResult> WarehouseOptionsAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IStorageLocationMasterReader reader,
        string? search,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 200)
        {
            return TypedResults.BadRequest();
        }

        return TypedResults.Ok(await reader.GetWarehouseOptionsAsync(
            localeResolver.Resolve(httpContext.Request), search, limit, cancellationToken));
    }

    private static async Task<IResult> CreateAsync(
        CreateStorageLocationRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IStorageLocationMasterExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new CreateStorageLocationCommand(
            request.WarehouseId,
            InfrastructureMasterEndpointSupport.Normalize(request.Code),
            InfrastructureMasterEndpointSupport.Normalize(request.NameZhTw),
            InfrastructureMasterEndpointSupport.Normalize(request.NameThTh),
            request.Active);
        var payload = JsonPayload.FromUtf8Json(JsonSerializer.SerializeToUtf8Bytes(
            new { commandType = "CreateStorageLocation", command }, CanonicalJsonOptions));
        var result = await executor.CreateAsync(new CreateStorageLocationExecution(
            httpContext.GetRequiredCommandId(), requestHasher.Compute(payload), actorContext.ActorAccountId, command), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : InfrastructureMasterEndpointSupport.Failure(httpContext, result.Error, "Storage Location create failed.");
    }

    private static async Task<IResult> UpdateAsync(
        Guid storageLocationId,
        UpdateStorageLocationRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IStorageLocationMasterExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new UpdateStorageLocationCommand(
            storageLocationId,
            request.ExpectedRowVersion,
            request.WarehouseId,
            InfrastructureMasterEndpointSupport.Normalize(request.Code),
            InfrastructureMasterEndpointSupport.Normalize(request.NameZhTw),
            InfrastructureMasterEndpointSupport.Normalize(request.NameThTh),
            request.Active);
        var payload = JsonPayload.FromUtf8Json(JsonSerializer.SerializeToUtf8Bytes(
            new { commandType = "UpdateStorageLocation", command }, CanonicalJsonOptions));
        var result = await executor.UpdateAsync(new UpdateStorageLocationExecution(
            httpContext.GetRequiredCommandId(), requestHasher.Compute(payload), actorContext.ActorAccountId, command), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : InfrastructureMasterEndpointSupport.Failure(httpContext, result.Error, "Storage Location update failed.");
    }
}

public sealed record CreateStorageLocationRequest(
    Guid WarehouseId,
    string? Code,
    string? NameZhTw,
    string? NameThTh,
    bool Active);

public sealed record UpdateStorageLocationRequest(
    long ExpectedRowVersion,
    Guid WarehouseId,
    string? Code,
    string? NameZhTw,
    string? NameThTh,
    bool Active);
