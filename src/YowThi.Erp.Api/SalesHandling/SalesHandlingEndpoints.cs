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

public static class SalesHandlingEndpoints
{
    public const string RecordPackagingWorkOperationId = "SalesHandling_RecordPackagingWork";
    public const string RecordPackagingWorkCommandType = "RecordSalesPackagingWork";

    private static readonly JsonSerializerOptions CanonicalCommandJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapSalesHandlingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var salesHandling = endpoints.MapApiV1().MapGroup("/sales-handling");

        salesHandling.MapPost("/work-records", RecordPackagingWorkAsync)
            .WithName(RecordPackagingWorkOperationId)
            .RequireAuthorization(CapabilityPolicies.SalesHandlingWorkRecord)
            .RequireIdempotencyKey()
            .Produces<RecordSalesPackagingWorkResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> RecordPackagingWorkAsync(
        RecordSalesPackagingWorkRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IRecordSalesPackagingWorkExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.SalesId == Guid.Empty
            || request.WorkDate == default
            || request.EmployeeId == Guid.Empty
            || request.SalesPackagingItemId == Guid.Empty)
        {
            return ApiProblemResults.Create(
                httpContext,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.RequestValidationFailed,
                "Request validation failed.");
        }

        var command = new RecordSalesPackagingWorkCommand(
            request.SalesId,
            request.WorkDate,
            request.EmployeeId,
            request.SalesPackagingItemId,
            request.ConfirmedWageThb);

        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalRecordSalesPackagingWorkRequest(
                    RecordPackagingWorkCommandType,
                    command),
                CanonicalCommandJsonOptions));

        var execution = new RecordSalesPackagingWorkExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.ExecuteAsync(execution, cancellationToken);
        if (result.IsSuccess)
        {
            var response = new RecordSalesPackagingWorkResponse(
                result.Value.SalesPackagingWorkRecordId,
                result.Value.RowVersion);

            return TypedResults.Created(
                $"/api/v1/sales-handling/work-records/{result.Value.SalesPackagingWorkRecordId}",
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
            "Sales Packaging Work recording failed.");
    }

    private sealed record CanonicalRecordSalesPackagingWorkRequest(
        string CommandType,
        RecordSalesPackagingWorkCommand Command);
}

public sealed record RecordSalesPackagingWorkRequest(
    Guid SalesId,
    DateOnly WorkDate,
    Guid EmployeeId,
    Guid SalesPackagingItemId,
    long ConfirmedWageThb);

public sealed record RecordSalesPackagingWorkResponse(
    Guid SalesPackagingWorkRecordId,
    long RowVersion);
