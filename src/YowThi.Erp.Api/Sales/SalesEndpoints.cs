using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Api.Idempotency;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Serialization;
using YowThi.Erp.Application.Sales;
using YowThi.Erp.Domain.Inventory;

namespace YowThi.Erp.Api.Sales;

public static class SalesEndpoints
{
    public const string ConfirmSalesOperationId = "Sales_Confirm";
    public const string ConfirmSalesCommandType = "ConfirmSales";
    public const string CorrectAllocationOperationId = "Sales_CreateAllocationRevision";
    public const string CorrectAllocationCommandType = "CorrectSalesAllocation";

    private static readonly JsonSerializerOptions CanonicalCommandJsonOptions = CreateCanonicalCommandJsonOptions();

    public static IEndpointRouteBuilder MapSalesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var sales = endpoints.MapApiV1().MapGroup("/sales");

        sales.MapPost("/{salesId:guid}/confirm", ConfirmSalesAsync)
            .WithName(ConfirmSalesOperationId)
            .RequireAuthorization(CapabilityPolicies.SalesConfirm)
            .RequireIdempotencyKey()
            .Produces<ConfirmSalesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        sales.MapPost("/{salesId:guid}/allocation-revisions", CorrectSalesAllocationAsync)
            .WithName(CorrectAllocationOperationId)
            .RequireAuthorization(CapabilityPolicies.SalesCorrectAllocation)
            .RequireIdempotencyKey()
            .Produces<CorrectSalesAllocationResult>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> ConfirmSalesAsync(
        Guid salesId,
        ConfirmSalesRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IConfirmSalesExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1 || request.ManualAllocationOverrides is null)
        {
            return InvalidTransportRequest(httpContext);
        }

        var manualAllocationOverrides = request.ManualAllocationOverrides
            .Select(allocation => new SalesManualAllocationOverride(
                allocation.SalesDetailId,
                allocation.Origin,
                allocation.ProcurementBatchId,
                allocation.OutsourcedSupplyBatchId,
                allocation.AllocatedQuantity))
            .ToArray();

        var command = new ConfirmSalesCommand(
            salesId,
            request.ExpectedRowVersion,
            manualAllocationOverrides);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalConfirmSalesRequest(ConfirmSalesCommandType, command),
                CanonicalCommandJsonOptions));
        var execution = new ConfirmSalesExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.ExecuteAsync(execution, cancellationToken);
        if (result.IsSuccess)
        {
            return TypedResults.Ok(new ConfirmSalesResponse(
                result.Value.SalesId,
                result.Value.SalesRowVersion,
                result.Value.ReceivableId,
                result.Value.AllocationRevisionId,
                result.Value.InventoryOperationId));
        }

        return CreateFailureResult(httpContext, result.Error, "Sales confirmation failed.");
    }

    private static async Task<IResult> CorrectSalesAllocationAsync(
        Guid salesId,
        CorrectSalesAllocationRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ICorrectSalesAllocationExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1
            || !Enum.TryParse<SalesAllocationCorrectionMode>(request.Mode, ignoreCase: false, out var mode)
            || !Enum.IsDefined(mode)
            || request.Allocations is null)
        {
            return InvalidTransportRequest(httpContext);
        }

        var allocations = new List<SalesAllocationCorrectionInput>();
        foreach (var item in request.Allocations)
        {
            if (!Enum.TryParse<InventoryOrigin>(item.Origin, ignoreCase: false, out var origin)
                || !Enum.IsDefined(origin))
            {
                return InvalidTransportRequest(httpContext);
            }

            allocations.Add(new SalesAllocationCorrectionInput(
                item.SalesDetailId,
                origin,
                item.ProcurementBatchId,
                item.OutsourcedSupplyBatchId,
                item.AllocatedQuantity));
        }

        var command = new CorrectSalesAllocationCommand(
            salesId,
            request.ExpectedRowVersion,
            mode,
            allocations);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalCorrectSalesAllocationRequest(CorrectAllocationCommandType, command),
                CanonicalCommandJsonOptions));
        var execution = new CorrectSalesAllocationExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.ExecuteAsync(execution, cancellationToken);
        if (result.IsFailure)
        {
            return CreateFailureResult(httpContext, result.Error, "Sales allocation correction failed.");
        }

        return TypedResults.Created(
            $"/api/v1/sales/{salesId}/allocation-revisions/{result.Value.AllocationRevisionId}",
            result.Value);
    }

    private static JsonSerializerOptions CreateCanonicalCommandJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static IResult InvalidTransportRequest(HttpContext httpContext) =>
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

    private sealed record CanonicalConfirmSalesRequest(
        string CommandType,
        ConfirmSalesCommand Command);

    private sealed record CanonicalCorrectSalesAllocationRequest(
        string CommandType,
        CorrectSalesAllocationCommand Command);
}

public sealed record ConfirmSalesRequest(
    long ExpectedRowVersion,
    IReadOnlyList<SalesManualAllocationOverrideRequest>? ManualAllocationOverrides);

public sealed record SalesManualAllocationOverrideRequest(
    Guid SalesDetailId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    InventoryOrigin Origin,
    Guid? ProcurementBatchId,
    Guid? OutsourcedSupplyBatchId,
    decimal AllocatedQuantity);

public sealed record ConfirmSalesResponse(
    Guid SalesId,
    long RowVersion,
    Guid ReceivableId,
    Guid AllocationRevisionId,
    Guid InventoryOperationId);

public sealed record CorrectSalesAllocationRequest(
    long ExpectedRowVersion,
    string Mode,
    IReadOnlyList<SalesAllocationCorrectionRequestItem>? Allocations);

public sealed record SalesAllocationCorrectionRequestItem(
    Guid SalesDetailId,
    string Origin,
    Guid? ProcurementBatchId,
    Guid? OutsourcedSupplyBatchId,
    decimal AllocatedQuantity);