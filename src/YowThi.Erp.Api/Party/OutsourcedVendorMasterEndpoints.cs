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

public static class OutsourcedVendorMasterEndpoints
{
    public const string ListOperationId = "Party_ListOutsourcedVendors";
    public const string CreateOperationId = "Party_CreateOutsourcedVendor";
    public const string CreateCommandType = "CreateOutsourcedVendor";
    public const string UpdateOperationId = "Party_UpdateOutsourcedVendor";
    public const string UpdateCommandType = "UpdateOutsourcedVendor";

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapOutsourcedVendorMasterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var party = endpoints.MapApiV1().MapGroup("/party");

        party.MapGet("/outsourced-vendors", ListAsync)
            .WithName(ListOperationId)
            .RequireAuthorization(CapabilityPolicies.OutsourcedVendorLifecycle)
            .Produces<OutsourcedVendorMasterPage>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        party.MapPost("/outsourced-vendors", CreateAsync)
            .WithName(CreateOperationId)
            .RequireAuthorization(CapabilityPolicies.OutsourcedVendorLifecycle)
            .RequireIdempotencyKey()
            .Produces<OutsourcedVendorMasterWriteResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        party.MapPost("/outsourced-vendors/{outsourcedVendorId:guid}/update", UpdateAsync)
            .WithName(UpdateOperationId)
            .RequireAuthorization(CapabilityPolicies.OutsourcedVendorLifecycle)
            .RequireIdempotencyKey()
            .Produces<OutsourcedVendorMasterWriteResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        [FromServices] IOutsourcedVendorMasterReader reader,
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
            new OutsourcedVendorMasterQuery(search, parsedStatus, offset, limit),
            cancellationToken);
        return TypedResults.Ok(page);
    }

    private static async Task<IResult> CreateAsync(
        CreateOutsourcedVendorRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IOutsourcedVendorMasterExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new CreateOutsourcedVendorCommand(
            Normalize(request.NameZhTw),
            Normalize(request.NameThTh),
            Normalize(request.BankName),
            Normalize(request.BankAccount),
            Normalize(request.Phone),
            Normalize(request.Address),
            request.Active);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalCreateOutsourcedVendorRequest(CreateCommandType, command),
                CanonicalJsonOptions));
        var execution = new CreateOutsourcedVendorExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.CreateAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Outsourced Vendor create failed.");
    }

    private static async Task<IResult> UpdateAsync(
        Guid outsourcedVendorId,
        UpdateOutsourcedVendorRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IOutsourcedVendorMasterExecutor executor,
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

        var command = new UpdateOutsourcedVendorCommand(
            outsourcedVendorId,
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
                new CanonicalUpdateOutsourcedVendorRequest(UpdateCommandType, command),
                CanonicalJsonOptions));
        var execution = new UpdateOutsourcedVendorExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.UpdateAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Outsourced Vendor update failed.");
    }

    private static bool TryParseStatus(string? value, out OutsourcedVendorMasterStatusFilter status)
    {
        status = value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "all" => OutsourcedVendorMasterStatusFilter.All,
            "active" => OutsourcedVendorMasterStatusFilter.Active,
            "inactive" => OutsourcedVendorMasterStatusFilter.Inactive,
            "deleted" => OutsourcedVendorMasterStatusFilter.Deleted,
            _ => (OutsourcedVendorMasterStatusFilter)(-1),
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

    private sealed record CanonicalCreateOutsourcedVendorRequest(
        string CommandType,
        CreateOutsourcedVendorCommand Command);

    private sealed record CanonicalUpdateOutsourcedVendorRequest(
        string CommandType,
        UpdateOutsourcedVendorCommand Command);
}

public sealed record CreateOutsourcedVendorRequest(
    string? NameZhTw,
    string? NameThTh,
    string? BankName,
    string? BankAccount,
    string? Phone,
    string? Address,
    bool Active);

public sealed record UpdateOutsourcedVendorRequest(
    long ExpectedRowVersion,
    string? NameZhTw,
    string? NameThTh,
    string? BankName,
    string? BankAccount,
    string? Phone,
    string? Address,
    bool Active);
