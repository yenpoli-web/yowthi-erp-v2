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
using YowThi.Erp.Application.Labor;

namespace YowThi.Erp.Api.Labor;

public static class LaborEndpoints
{
    public const string ConfirmEmployeeDailyWageOperationId = "Labor_ConfirmEmployeeDailyWage";
    public const string ConfirmEmployeeDailyWageCommandType = "ConfirmEmployeeDailyWage";

    private static readonly JsonSerializerOptions CanonicalCommandJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapLaborEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var labor = endpoints.MapApiV1().MapGroup("/labor");

        labor.MapPost("/daily-wages", ConfirmEmployeeDailyWageAsync)
            .WithName(ConfirmEmployeeDailyWageOperationId)
            .RequireAuthorization(CapabilityPolicies.LaborDailyWageConfirm)
            .RequireIdempotencyKey()
            .Produces<ConfirmEmployeeDailyWageResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> ConfirmEmployeeDailyWageAsync(
        ConfirmEmployeeDailyWageRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IConfirmEmployeeDailyWageExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.WorkDate == default
            || request.EmployeeId == Guid.Empty
            || request.ProcessingWageRateOverrides is null)
        {
            return ApiProblemResults.Create(
                httpContext,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.RequestValidationFailed,
                "Request validation failed.");
        }

        var rateOverrides = request.ProcessingWageRateOverrides
            .Select(item => new ProcessingWageRateOverride(
                item.ProcessingModuleOutputId,
                item.ConfiguredWageRateSnapshot,
                item.AppliedWageRate))
            .ToArray();

        var command = new ConfirmEmployeeDailyWageCommand(
            request.WorkDate,
            request.EmployeeId,
            rateOverrides);

        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalConfirmEmployeeDailyWageRequest(
                    ConfirmEmployeeDailyWageCommandType,
                    command),
                CanonicalCommandJsonOptions));

        var execution = new ConfirmEmployeeDailyWageExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.ExecuteAsync(execution, cancellationToken);
        if (result.IsSuccess)
        {
            var response = new ConfirmEmployeeDailyWageResponse(
                result.Value.EmployeeDailyWageId,
                result.Value.RowVersion,
                result.Value.PayableId,
                result.Value.ProcessingWageTotalThb,
                result.Value.SalesPackagingWageTotalThb,
                result.Value.TotalWageThb);

            return TypedResults.Created(
                $"/api/v1/labor/daily-wages/{result.Value.EmployeeDailyWageId}",
                response);
        }

        var statusCode = result.Error.Kind switch
        {
            ApplicationErrorKind.Validation => StatusCodes.Status422UnprocessableEntity,
            ApplicationErrorKind.NotFound => StatusCodes.Status404NotFound,
            ApplicationErrorKind.Conflict => StatusCodes.Status409Conflict,
            ApplicationErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            _ => throw new ArgumentOutOfRangeException(
                nameof(result.Error.Kind),
                result.Error.Kind,
                "Unsupported application error kind."),
        };

        return ApiProblemResults.Create(
            httpContext,
            statusCode,
            result.Error.Code,
            "Employee Daily Wage confirmation failed.");
    }

    private sealed record CanonicalConfirmEmployeeDailyWageRequest(
        string CommandType,
        ConfirmEmployeeDailyWageCommand Command);
}

public sealed record ConfirmEmployeeDailyWageRequest(
    DateOnly WorkDate,
    Guid EmployeeId,
    IReadOnlyList<ProcessingWageRateOverrideRequest>? ProcessingWageRateOverrides);

public sealed record ProcessingWageRateOverrideRequest(
    Guid ProcessingModuleOutputId,
    decimal ConfiguredWageRateSnapshot,
    decimal AppliedWageRate);

public sealed record ConfirmEmployeeDailyWageResponse(
    Guid EmployeeDailyWageId,
    long RowVersion,
    Guid PayableId,
    long ProcessingWageTotalThb,
    long SalesPackagingWageTotalThb,
    long TotalWageThb);
