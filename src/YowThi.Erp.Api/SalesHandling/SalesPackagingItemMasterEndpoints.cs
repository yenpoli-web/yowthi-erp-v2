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
using YowThi.Erp.Application.SalesHandling;

namespace YowThi.Erp.Api.SalesHandling;

public static class SalesPackagingItemMasterEndpoints
{
    public const string ListOperationId = "SalesHandling_ListPackagingItems";
    public const string CreateOperationId = "SalesHandling_CreatePackagingItem";
    public const string CreateCommandType = "CreateSalesPackagingItem";
    public const string UpdateOperationId = "SalesHandling_UpdatePackagingItem";
    public const string UpdateCommandType = "UpdateSalesPackagingItem";

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapSalesPackagingItemMasterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var salesHandling = endpoints.MapApiV1().MapGroup("/sales-handling");

        salesHandling.MapGet("/packaging-items", ListAsync)
            .WithName(ListOperationId)
            .RequireAuthorization(CapabilityPolicies.SalesPackagingItemLifecycle)
            .Produces<SalesPackagingItemMasterPage>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        salesHandling.MapPost("/packaging-items", CreateAsync)
            .WithName(CreateOperationId)
            .RequireAuthorization(CapabilityPolicies.SalesPackagingItemLifecycle)
            .RequireIdempotencyKey()
            .Produces<SalesPackagingItemMasterWriteResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        salesHandling.MapPost("/packaging-items/{salesPackagingItemId:guid}/update", UpdateAsync)
            .WithName(UpdateOperationId)
            .RequireAuthorization(CapabilityPolicies.SalesPackagingItemLifecycle)
            .RequireIdempotencyKey()
            .Produces<SalesPackagingItemMasterWriteResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        [FromServices] ISalesPackagingItemMasterReader reader,
        string? search,
        string? status,
        int offset = 0,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0 || limit is < 1 or > 200 || !TryParseStatus(status, out var parsedStatus))
        {
            return TypedResults.BadRequest();
        }

        var page = await reader.GetAsync(
            new SalesPackagingItemMasterQuery(search, parsedStatus, offset, limit),
            cancellationToken);
        return TypedResults.Ok(page);
    }

    private static async Task<IResult> CreateAsync(
        CreateSalesPackagingItemRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ISalesPackagingItemMasterExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new CreateSalesPackagingItemCommand(
            Normalize(request.NameZhTw),
            Normalize(request.NameThTh),
            request.Active);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalCreateSalesPackagingItemRequest(CreateCommandType, command),
                CanonicalJsonOptions));
        var execution = new CreateSalesPackagingItemExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.CreateAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Sales Packaging Item create failed.");
    }

    private static async Task<IResult> UpdateAsync(
        Guid salesPackagingItemId,
        UpdateSalesPackagingItemRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ISalesPackagingItemMasterExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1)
        {
            return ApiProblemResults.Create(
                httpContext,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.RequestValidationFailed,
                "Request validation failed.");
        }

        var command = new UpdateSalesPackagingItemCommand(
            salesPackagingItemId,
            request.ExpectedRowVersion,
            Normalize(request.NameZhTw),
            Normalize(request.NameThTh),
            request.Active);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalUpdateSalesPackagingItemRequest(UpdateCommandType, command),
                CanonicalJsonOptions));
        var execution = new UpdateSalesPackagingItemExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.UpdateAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Sales Packaging Item update failed.");
    }

    private static bool TryParseStatus(string? value, out SalesPackagingItemMasterStatusFilter status)
    {
        status = value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "all" => SalesPackagingItemMasterStatusFilter.All,
            "active" => SalesPackagingItemMasterStatusFilter.Active,
            "inactive" => SalesPackagingItemMasterStatusFilter.Inactive,
            "deleted" => SalesPackagingItemMasterStatusFilter.Deleted,
            _ => (SalesPackagingItemMasterStatusFilter)(-1),
        };
        return Enum.IsDefined(status);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

    private sealed record CanonicalCreateSalesPackagingItemRequest(
        string CommandType,
        CreateSalesPackagingItemCommand Command);

    private sealed record CanonicalUpdateSalesPackagingItemRequest(
        string CommandType,
        UpdateSalesPackagingItemCommand Command);
}

public sealed record CreateSalesPackagingItemRequest(
    string? NameZhTw,
    string? NameThTh,
    bool Active);

public sealed record UpdateSalesPackagingItemRequest(
    long ExpectedRowVersion,
    string? NameZhTw,
    string? NameThTh,
    bool Active);
