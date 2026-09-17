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
using YowThi.Erp.Application.DataProtection;

namespace YowThi.Erp.Api.DataProtection;

public static class OperationalMasterDataProtectionEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapOperationalMasterDataProtectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapApiV1().MapGroup("/data-protection");
        Map(group, "/employees/{id:guid}/hard-delete", "DataProtection_HardDeleteEmployee", HardDeleteEmployeeAsync);
        Map(group, "/sales-packaging-items/{id:guid}/hard-delete", "DataProtection_HardDeleteSalesPackagingItem", HardDeleteSalesPackagingItemAsync);
        Map(group, "/warehouses/{id:guid}/hard-delete", "DataProtection_HardDeleteWarehouse", HardDeleteWarehouseAsync);
        Map(group, "/storage-locations/{id:guid}/hard-delete", "DataProtection_HardDeleteStorageLocation", HardDeleteStorageLocationAsync);
        Map(group, "/containers/{id:guid}/hard-delete", "DataProtection_HardDeleteContainer", HardDeleteContainerAsync);
        return endpoints;
    }

    private static void Map(RouteGroupBuilder group, string path, string operationId, Delegate handler) =>
        group.MapPost(path, handler)
            .WithName(operationId)
            .RequireAuthorization(CapabilityPolicies.DataProtectionHardDelete)
            .RequireIdempotencyKey()
            .RequireRecentDeletionReauthentication()
            .Produces<HardDeleteOperationalMasterResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

    private static Task<IResult> HardDeleteEmployeeAsync(Guid id, OperationalMasterHardDeleteRequest request, HttpContext context,
        [FromServices] IActorContext actor, [FromServices] ICommandRequestHasher hasher, [FromServices] IHardDeleteEmployeeExecutor executor, CancellationToken ct) =>
        ExecuteAsync(id, request, context, actor, hasher, executor.ExecuteAsync, "HardDeleteEmployee", "Employee hard delete failed.", ct);

    private static Task<IResult> HardDeleteSalesPackagingItemAsync(Guid id, OperationalMasterHardDeleteRequest request, HttpContext context,
        [FromServices] IActorContext actor, [FromServices] ICommandRequestHasher hasher, [FromServices] IHardDeleteSalesPackagingItemExecutor executor, CancellationToken ct) =>
        ExecuteAsync(id, request, context, actor, hasher, executor.ExecuteAsync, "HardDeleteSalesPackagingItem", "Sales Packaging Item hard delete failed.", ct);

    private static Task<IResult> HardDeleteWarehouseAsync(Guid id, OperationalMasterHardDeleteRequest request, HttpContext context,
        [FromServices] IActorContext actor, [FromServices] ICommandRequestHasher hasher, [FromServices] IHardDeleteWarehouseExecutor executor, CancellationToken ct) =>
        ExecuteAsync(id, request, context, actor, hasher, executor.ExecuteAsync, "HardDeleteWarehouse", "Warehouse hard delete failed.", ct);

    private static Task<IResult> HardDeleteStorageLocationAsync(Guid id, OperationalMasterHardDeleteRequest request, HttpContext context,
        [FromServices] IActorContext actor, [FromServices] ICommandRequestHasher hasher, [FromServices] IHardDeleteStorageLocationExecutor executor, CancellationToken ct) =>
        ExecuteAsync(id, request, context, actor, hasher, executor.ExecuteAsync, "HardDeleteStorageLocation", "Storage Location hard delete failed.", ct);

    private static Task<IResult> HardDeleteContainerAsync(Guid id, OperationalMasterHardDeleteRequest request, HttpContext context,
        [FromServices] IActorContext actor, [FromServices] ICommandRequestHasher hasher, [FromServices] IHardDeleteContainerExecutor executor, CancellationToken ct) =>
        ExecuteAsync(id, request, context, actor, hasher, executor.ExecuteAsync, "HardDeleteContainer", "Container hard delete failed.", ct);

    private static async Task<IResult> ExecuteAsync(
        Guid id,
        OperationalMasterHardDeleteRequest request,
        HttpContext context,
        IActorContext actor,
        ICommandRequestHasher hasher,
        Func<HardDeleteOperationalMasterExecution, CancellationToken, ValueTask<Application.Common.Results.ApplicationResult<HardDeleteOperationalMasterResult>>> execute,
        string commandType,
        string title,
        CancellationToken ct)
    {
        if (request.ExpectedRowVersion < 1)
            return ApiProblemResults.Create(context, StatusCodes.Status400BadRequest, ApiErrorCodes.RequestValidationFailed, "Request validation failed.");

        var command = new HardDeleteOperationalMasterCommand(id, request.ExpectedRowVersion);
        var payload = JsonPayload.FromUtf8Json(JsonSerializer.SerializeToUtf8Bytes(new CanonicalRequest(commandType, command), JsonOptions));
        var execution = new HardDeleteOperationalMasterExecution(context.GetRequiredCommandId(), hasher.Compute(payload), actor.ActorAccountId, command);
        var result = await execute(execution, ct);
        if (result.IsSuccess) return TypedResults.Ok(result.Value);
        var status = result.Error.Kind switch
        {
            ApplicationErrorKind.Validation => StatusCodes.Status422UnprocessableEntity,
            ApplicationErrorKind.NotFound => StatusCodes.Status404NotFound,
            ApplicationErrorKind.Conflict => StatusCodes.Status409Conflict,
            ApplicationErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            _ => throw new ArgumentOutOfRangeException(nameof(result.Error.Kind)),
        };
        return ApiProblemResults.Create(context, status, result.Error.Code, title);
    }

    private sealed record CanonicalRequest(string CommandType, HardDeleteOperationalMasterCommand Command);
}

public sealed record OperationalMasterHardDeleteRequest(long ExpectedRowVersion);
