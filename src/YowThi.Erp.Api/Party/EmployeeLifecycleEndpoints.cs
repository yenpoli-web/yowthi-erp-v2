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

public static class EmployeeLifecycleEndpoints
{
    public const string SoftDeleteEmployeeOperationId = "Party_SoftDeleteEmployee";
    public const string SoftDeleteEmployeeCommandType = "SoftDeleteEmployee";
    public const string RestoreEmployeeOperationId = "Party_RestoreEmployee";
    public const string RestoreEmployeeCommandType = "RestoreEmployee";

    private static readonly JsonSerializerOptions CanonicalCommandJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapEmployeeLifecycleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var party = endpoints.MapApiV1().MapGroup("/party");

        party.MapPost("/employees/{employeeId:guid}/soft-delete", SoftDeleteEmployeeAsync)
            .WithName(SoftDeleteEmployeeOperationId)
            .RequireAuthorization(CapabilityPolicies.EmployeeLifecycle)
            .RequireIdempotencyKey()
            .Produces<EmployeeLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        party.MapPost("/employees/{employeeId:guid}/restore", RestoreEmployeeAsync)
            .WithName(RestoreEmployeeOperationId)
            .RequireAuthorization(CapabilityPolicies.EmployeeLifecycle)
            .RequireIdempotencyKey()
            .Produces<EmployeeLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> SoftDeleteEmployeeAsync(
        Guid employeeId,
        EmployeeLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IEmployeeLifecycleExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1)
        {
            return InvalidTransportVersion(httpContext);
        }

        var command = new SoftDeleteEmployeeCommand(employeeId, request.ExpectedRowVersion);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalSoftDeleteEmployeeRequest(SoftDeleteEmployeeCommandType, command),
                CanonicalCommandJsonOptions));
        var execution = new SoftDeleteEmployeeExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.SoftDeleteAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Employee Soft Delete failed.");
    }

    private static async Task<IResult> RestoreEmployeeAsync(
        Guid employeeId,
        EmployeeLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IEmployeeLifecycleExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1)
        {
            return InvalidTransportVersion(httpContext);
        }

        var command = new RestoreEmployeeCommand(employeeId, request.ExpectedRowVersion);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalRestoreEmployeeRequest(RestoreEmployeeCommandType, command),
                CanonicalCommandJsonOptions));
        var execution = new RestoreEmployeeExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.RestoreAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Employee Restore failed.");
    }

    private static IResult InvalidTransportVersion(HttpContext httpContext) =>
        ApiProblemResults.Create(
            httpContext,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.RequestValidationFailed,
            "Request validation failed.");

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

    private sealed record CanonicalSoftDeleteEmployeeRequest(
        string CommandType,
        SoftDeleteEmployeeCommand Command);

    private sealed record CanonicalRestoreEmployeeRequest(
        string CommandType,
        RestoreEmployeeCommand Command);
}

public sealed record EmployeeLifecycleRequest(long ExpectedRowVersion);
