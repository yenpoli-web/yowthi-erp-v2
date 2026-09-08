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

public static class SalesProductGroupMasterEndpoints
{
    public const string ListOperationId = "Product_ListSalesProductGroups";
    public const string CreateOperationId = "Product_CreateSalesProductGroup";
    public const string CreateCommandType = "CreateSalesProductGroup";
    public const string UpdateOperationId = "Product_UpdateSalesProductGroup";
    public const string UpdateCommandType = "UpdateSalesProductGroup";

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapSalesProductGroupMasterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var product = endpoints.MapApiV1().MapGroup("/product");

        product.MapGet("/sales-product-groups", ListAsync)
            .WithName(ListOperationId)
            .RequireAuthorization(CapabilityPolicies.SalesProductGroupLifecycle)
            .Produces<SalesProductGroupMasterPage>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        product.MapPost("/sales-product-groups", CreateAsync)
            .WithName(CreateOperationId)
            .RequireAuthorization(CapabilityPolicies.SalesProductGroupLifecycle)
            .RequireIdempotencyKey()
            .Produces<SalesProductGroupMasterWriteResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        product.MapPost("/sales-product-groups/{salesProductGroupId:guid}/update", UpdateAsync)
            .WithName(UpdateOperationId)
            .RequireAuthorization(CapabilityPolicies.SalesProductGroupLifecycle)
            .RequireIdempotencyKey()
            .Produces<SalesProductGroupMasterWriteResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        [FromServices] ISalesProductGroupMasterReader reader,
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
            new SalesProductGroupMasterQuery(search, parsedStatus, offset, limit),
            cancellationToken);
        return TypedResults.Ok(page);
    }

    private static async Task<IResult> CreateAsync(
        CreateSalesProductGroupRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ISalesProductGroupMasterExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new CreateSalesProductGroupCommand(
            Normalize(request.NameZhTw),
            Normalize(request.NameThTh),
            request.Active);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalCreateSalesProductGroupRequest(CreateCommandType, command),
                CanonicalJsonOptions));
        var execution = new CreateSalesProductGroupExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.CreateAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Sales Product Group create failed.");
    }

    private static async Task<IResult> UpdateAsync(
        Guid salesProductGroupId,
        UpdateSalesProductGroupRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ISalesProductGroupMasterExecutor executor,
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

        var command = new UpdateSalesProductGroupCommand(
            salesProductGroupId,
            request.ExpectedRowVersion,
            Normalize(request.NameZhTw),
            Normalize(request.NameThTh),
            request.Active);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalUpdateSalesProductGroupRequest(UpdateCommandType, command),
                CanonicalJsonOptions));
        var execution = new UpdateSalesProductGroupExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.UpdateAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Sales Product Group update failed.");
    }

    private static bool TryParseStatus(string? value, out SalesProductGroupMasterStatusFilter status)
    {
        status = value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "all" => SalesProductGroupMasterStatusFilter.All,
            "active" => SalesProductGroupMasterStatusFilter.Active,
            "inactive" => SalesProductGroupMasterStatusFilter.Inactive,
            "deleted" => SalesProductGroupMasterStatusFilter.Deleted,
            _ => (SalesProductGroupMasterStatusFilter)(-1),
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

    private sealed record CanonicalCreateSalesProductGroupRequest(
        string CommandType,
        CreateSalesProductGroupCommand Command);

    private sealed record CanonicalUpdateSalesProductGroupRequest(
        string CommandType,
        UpdateSalesProductGroupCommand Command);
}

public sealed record CreateSalesProductGroupRequest(
    string? NameZhTw,
    string? NameThTh,
    bool Active);

public sealed record UpdateSalesProductGroupRequest(
    long ExpectedRowVersion,
    string? NameZhTw,
    string? NameThTh,
    bool Active);
