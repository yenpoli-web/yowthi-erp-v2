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
using YowThi.Erp.Application.Security;

namespace YowThi.Erp.Api.Security;

public static class SecurityAccountEndpoints
{
    public const string ListOperationId = "Security_ListAccounts";
    public const string CreateOperationId = "Security_CreateAccount";
    public const string CreateCommandType = "CreateSecurityAccount";
    public const string UpdateOperationId = "Security_UpdateAccount";
    public const string UpdateCommandType = "UpdateSecurityAccount";

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapSecurityAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var security = endpoints.MapApiV1().MapGroup("/security");

        security.MapGet("/accounts", ListAsync)
            .WithName(ListOperationId)
            .RequireAuthorization(CapabilityPolicies.SecurityAccountManage)
            .Produces<SecurityAccountManagementPage>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        security.MapPost("/accounts", CreateAsync)
            .WithName(CreateOperationId)
            .RequireAuthorization(CapabilityPolicies.SecurityAccountManage)
            .RequireIdempotencyKey()
            .Produces<SecurityAccountWriteResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        security.MapPost("/accounts/{accountId:guid}/update", UpdateAsync)
            .WithName(UpdateOperationId)
            .RequireAuthorization(CapabilityPolicies.SecurityAccountManage)
            .RequireIdempotencyKey()
            .Produces<SecurityAccountWriteResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        [FromServices] ISecurityAccountManagementReader reader,
        string? search,
        CancellationToken cancellationToken)
    {
        var page = await reader.GetAsync(search, cancellationToken);
        return TypedResults.Ok(page);
    }

    private static async Task<IResult> CreateAsync(
        SecurityAccountRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ISecurityAccountManagementExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new CreateSecurityAccountCommand(
            request.DisplayName,
            request.Active,
            request.IdentityIssuer,
            request.IdentitySubject,
            request.Capabilities ?? []);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalCreateRequest(CreateCommandType, command),
                CanonicalJsonOptions));
        var execution = new CreateSecurityAccountExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.CreateAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Account create failed.");
    }

    private static async Task<IResult> UpdateAsync(
        Guid accountId,
        SecurityAccountUpdateRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ISecurityAccountManagementExecutor executor,
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

        var command = new UpdateSecurityAccountCommand(
            accountId,
            request.ExpectedRowVersion,
            request.DisplayName,
            request.Active,
            request.IdentityIssuer,
            request.IdentitySubject,
            request.Capabilities ?? []);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalUpdateRequest(UpdateCommandType, command),
                CanonicalJsonOptions));
        var execution = new UpdateSecurityAccountExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.UpdateAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Account update failed.");
    }

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

    private sealed record CanonicalCreateRequest(string CommandType, CreateSecurityAccountCommand Command);
    private sealed record CanonicalUpdateRequest(string CommandType, UpdateSecurityAccountCommand Command);
}

public sealed record SecurityAccountRequest(
    string DisplayName,
    bool Active,
    string? IdentityIssuer,
    string? IdentitySubject,
    IReadOnlyList<string>? Capabilities);

public sealed record SecurityAccountUpdateRequest(
    long ExpectedRowVersion,
    string DisplayName,
    bool Active,
    string? IdentityIssuer,
    string? IdentitySubject,
    IReadOnlyList<string>? Capabilities);
