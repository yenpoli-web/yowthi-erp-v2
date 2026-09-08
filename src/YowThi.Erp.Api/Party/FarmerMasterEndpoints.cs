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

public static class FarmerMasterEndpoints
{
    public const string ListOperationId = "Party_ListFarmers";
    public const string CreateOperationId = "Party_CreateFarmer";
    public const string CreateCommandType = "CreateFarmer";
    public const string UpdateOperationId = "Party_UpdateFarmer";
    public const string UpdateCommandType = "UpdateFarmer";

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapFarmerMasterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var party = endpoints.MapApiV1().MapGroup("/party");

        party.MapGet("/farmers", ListAsync)
            .WithName(ListOperationId)
            .RequireAuthorization(CapabilityPolicies.FarmerLifecycle)
            .Produces<FarmerMasterPage>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        party.MapPost("/farmers", CreateAsync)
            .WithName(CreateOperationId)
            .RequireAuthorization(CapabilityPolicies.FarmerLifecycle)
            .RequireIdempotencyKey()
            .Produces<FarmerMasterWriteResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        party.MapPost("/farmers/{farmerId:guid}/update", UpdateAsync)
            .WithName(UpdateOperationId)
            .RequireAuthorization(CapabilityPolicies.FarmerLifecycle)
            .RequireIdempotencyKey()
            .Produces<FarmerMasterWriteResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        [FromServices] IFarmerMasterReader reader,
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
            new FarmerMasterQuery(search, parsedStatus, offset, limit),
            cancellationToken);
        return TypedResults.Ok(page);
    }

    private static async Task<IResult> CreateAsync(
        CreateFarmerRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IFarmerMasterExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new CreateFarmerCommand(
            Normalize(request.NameZhTw),
            Normalize(request.NameThTh),
            Normalize(request.BankName),
            Normalize(request.BankAccount),
            Normalize(request.Phone),
            Normalize(request.Address),
            request.Active,
            Normalize(request.Code));
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalCreateFarmerRequest(CreateCommandType, command),
                CanonicalJsonOptions));
        var execution = new CreateFarmerExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.CreateAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Farmer create failed.");
    }

    private static async Task<IResult> UpdateAsync(
        Guid farmerId,
        UpdateFarmerRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IFarmerMasterExecutor executor,
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

        var command = new UpdateFarmerCommand(
            farmerId,
            request.ExpectedRowVersion,
            Normalize(request.NameZhTw),
            Normalize(request.NameThTh),
            Normalize(request.BankName),
            Normalize(request.BankAccount),
            Normalize(request.Phone),
            Normalize(request.Address),
            request.Active,
            Normalize(request.Code));
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalUpdateFarmerRequest(UpdateCommandType, command),
                CanonicalJsonOptions));
        var execution = new UpdateFarmerExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.UpdateAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Farmer update failed.");
    }

    private static bool TryParseStatus(string? value, out FarmerMasterStatusFilter status)
    {
        status = value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "all" => FarmerMasterStatusFilter.All,
            "active" => FarmerMasterStatusFilter.Active,
            "inactive" => FarmerMasterStatusFilter.Inactive,
            "deleted" => FarmerMasterStatusFilter.Deleted,
            _ => (FarmerMasterStatusFilter)(-1),
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

    private sealed record CanonicalCreateFarmerRequest(
        string CommandType,
        CreateFarmerCommand Command);

    private sealed record CanonicalUpdateFarmerRequest(
        string CommandType,
        UpdateFarmerCommand Command);
}

public sealed record CreateFarmerRequest(
    string? NameZhTw,
    string? NameThTh,
    string? BankName,
    string? BankAccount,
    string? Phone,
    string? Address,
    bool Active,
    string? Code = null);

public sealed record UpdateFarmerRequest(
    long ExpectedRowVersion,
    string? NameZhTw,
    string? NameThTh,
    string? BankName,
    string? BankAccount,
    string? Phone,
    string? Address,
    bool Active,
    string? Code = null);
