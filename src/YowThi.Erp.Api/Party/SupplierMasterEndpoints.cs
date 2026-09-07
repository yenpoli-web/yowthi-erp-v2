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
using YowThi.Erp.Application.Party;

namespace YowThi.Erp.Api.Party;

public static class SupplierMasterEndpoints
{
    public const string ListOperationId = "Party_ListSuppliers";
    public const string CreateOperationId = "Party_CreateSupplier";
    public const string CreateCommandType = "CreateSupplier";
    public const string UpdateOperationId = "Party_UpdateSupplier";
    public const string UpdateCommandType = "UpdateSupplier";

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapSupplierMasterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var party = endpoints.MapApiV1().MapGroup("/party");

        party.MapGet("/suppliers", ListAsync)
            .WithName(ListOperationId)
            .RequireAuthorization(CapabilityPolicies.SupplierLifecycle)
            .Produces<SupplierMasterPage>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        party.MapPost("/suppliers", CreateAsync)
            .WithName(CreateOperationId)
            .RequireAuthorization(CapabilityPolicies.SupplierLifecycle)
            .RequireIdempotencyKey()
            .Produces<SupplierMasterWriteResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        party.MapPost("/suppliers/{supplierId:guid}/update", UpdateAsync)
            .WithName(UpdateOperationId)
            .RequireAuthorization(CapabilityPolicies.SupplierLifecycle)
            .RequireIdempotencyKey()
            .Produces<SupplierMasterWriteResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        [FromServices] ISupplierMasterReader reader,
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
            new SupplierMasterQuery(search, parsedStatus, offset, limit),
            cancellationToken);
        return TypedResults.Ok(page);
    }

    private static async Task<IResult> CreateAsync(
        CreateSupplierRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ISupplierMasterExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new CreateSupplierCommand(
            Normalize(request.NameZhTw),
            Normalize(request.NameThTh),
            Normalize(request.BankName),
            Normalize(request.BankAccount),
            Normalize(request.Phone),
            Normalize(request.Address),
            request.Active);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalCreateSupplierRequest(CreateCommandType, command),
                CanonicalJsonOptions));
        var execution = new CreateSupplierExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.CreateAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Supplier create failed.");
    }

    private static async Task<IResult> UpdateAsync(
        Guid supplierId,
        UpdateSupplierRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ISupplierMasterExecutor executor,
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

        var command = new UpdateSupplierCommand(
            supplierId,
            request.ExpectedRowVersion,
            Normalize(request.NameZhTw),
            Normalize(request.NameThTh),
            Normalize(request.BankName),
            Normalize(request.BankAccount),
            Normalize(request.Phone),
            Normalize(request.Address),
            request.Active);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalUpdateSupplierRequest(UpdateCommandType, command),
                CanonicalJsonOptions));
        var execution = new UpdateSupplierExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.UpdateAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Supplier update failed.");
    }

    private static bool TryParseStatus(string? value, out SupplierMasterStatusFilter status)
    {
        status = value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "all" => SupplierMasterStatusFilter.All,
            "active" => SupplierMasterStatusFilter.Active,
            "inactive" => SupplierMasterStatusFilter.Inactive,
            "deleted" => SupplierMasterStatusFilter.Deleted,
            _ => (SupplierMasterStatusFilter)(-1),
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

    private sealed record CanonicalCreateSupplierRequest(
        string CommandType,
        CreateSupplierCommand Command);

    private sealed record CanonicalUpdateSupplierRequest(
        string CommandType,
        UpdateSupplierCommand Command);
}

public sealed record CreateSupplierRequest(
    string? NameZhTw,
    string? NameThTh,
    string? BankName,
    string? BankAccount,
    string? Phone,
    string? Address,
    bool Active);

public sealed record UpdateSupplierRequest(
    long ExpectedRowVersion,
    string? NameZhTw,
    string? NameThTh,
    string? BankName,
    string? BankAccount,
    string? Phone,
    string? Address,
    bool Active);
