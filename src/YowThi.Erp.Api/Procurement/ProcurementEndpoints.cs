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
using YowThi.Erp.Application.Procurement;
using YowThi.Erp.Domain.Procurement;

namespace YowThi.Erp.Api.Procurement;

public static class ProcurementEndpoints
{
    public const string ConfirmEntryOperationId = "Procurement_ConfirmEntry";
    public const string ConfirmEntryCommandType = "ConfirmProcurementEntry";

    private static readonly JsonSerializerOptions CanonicalCommandJsonOptions = CreateCanonicalCommandJsonOptions();

    public static IEndpointRouteBuilder MapProcurementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var procurement = endpoints.MapApiV1().MapGroup("/procurement");

        procurement.MapPost("/entries", ConfirmEntryAsync)
            .WithName(ConfirmEntryOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcurementConfirm)
            .RequireIdempotencyKey()
            .Produces<ConfirmProcurementEntryResult>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> ConfirmEntryAsync(
        ConfirmProcurementEntryRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IConfirmProcurementEntryExecutor executor,
        CancellationToken cancellationToken)
    {
        var command = new ConfirmProcurementEntryCommand(
            request.ProcurementDate,
            request.ProcurementProductId,
            request.SourceType,
            request.SupplierId,
            request.FarmerId,
            request.NetQuantity,
            request.UnitPrice,
            request.CompanyPickup,
            request.ReceiptStorageLocationId);

        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalConfirmProcurementEntryRequest(ConfirmEntryCommandType, command),
                CanonicalCommandJsonOptions));

        var execution = new ConfirmProcurementEntryExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.ExecuteAsync(execution, cancellationToken);
        if (result.IsSuccess)
        {
            return TypedResults.Created(
                $"/api/v1/procurement/entries/{result.Value.ProcurementEntryId}",
                result.Value);
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
            "Procurement Entry confirmation failed.");
    }

    private static JsonSerializerOptions CreateCanonicalCommandJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record CanonicalConfirmProcurementEntryRequest(
        string CommandType,
        ConfirmProcurementEntryCommand Command);
}

public sealed record ConfirmProcurementEntryRequest(
    DateOnly ProcurementDate,
    Guid ProcurementProductId,
    ProcurementSourceType SourceType,
    Guid? SupplierId,
    Guid? FarmerId,
    decimal NetQuantity,
    decimal UnitPrice,
    bool CompanyPickup,
    Guid? ReceiptStorageLocationId);
