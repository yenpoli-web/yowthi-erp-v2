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
            return ApiProblemResults.Create(
                httpContext,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.RequestValidationFailed,
                "Request validation failed.");
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

        var statusCode = result.Error.Kind switch
        {
            ApplicationErrorKind.Validation => StatusCodes.Status422UnprocessableEntity,
            ApplicationErrorKind.NotFound => StatusCodes.Status404NotFound,
            ApplicationErrorKind.Conflict => StatusCodes.Status409Conflict,
            ApplicationErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            _ => throw new ArgumentOutOfRangeException(nameof(result.Error.Kind), result.Error.Kind, "Unsupported application error kind."),
        };

        return ApiProblemResults.Create(
            httpContext,
            statusCode,
            result.Error.Code,
            "Sales confirmation failed.");
    }

    private static JsonSerializerOptions CreateCanonicalCommandJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record CanonicalConfirmSalesRequest(
        string CommandType,
        ConfirmSalesCommand Command);
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
