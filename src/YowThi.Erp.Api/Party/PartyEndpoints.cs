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

public static class PartyEndpoints
{
    public const string SoftDeleteSupplierOperationId = "Party_SoftDeleteSupplier";
    public const string SoftDeleteSupplierCommandType = "SoftDeleteSupplier";
    public const string RestoreSupplierOperationId = "Party_RestoreSupplier";
    public const string RestoreSupplierCommandType = "RestoreSupplier";
    public const string SoftDeleteCustomerOperationId = "Party_SoftDeleteCustomer";
    public const string SoftDeleteCustomerCommandType = "SoftDeleteCustomer";
    public const string RestoreCustomerOperationId = "Party_RestoreCustomer";
    public const string RestoreCustomerCommandType = "RestoreCustomer";
    public const string SoftDeleteOutsourcedVendorOperationId = "Party_SoftDeleteOutsourcedVendor";
    public const string SoftDeleteOutsourcedVendorCommandType = "SoftDeleteOutsourcedVendor";
    public const string RestoreOutsourcedVendorOperationId = "Party_RestoreOutsourcedVendor";
    public const string RestoreOutsourcedVendorCommandType = "RestoreOutsourcedVendor";

    private static readonly JsonSerializerOptions CanonicalCommandJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapPartyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var party = endpoints.MapApiV1().MapGroup("/party");

        party.MapPost("/suppliers/{supplierId:guid}/soft-delete", SoftDeleteSupplierAsync)
            .WithName(SoftDeleteSupplierOperationId)
            .RequireAuthorization(CapabilityPolicies.SupplierLifecycle)
            .RequireIdempotencyKey()
            .Produces<SupplierLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        party.MapPost("/suppliers/{supplierId:guid}/restore", RestoreSupplierAsync)
            .WithName(RestoreSupplierOperationId)
            .RequireAuthorization(CapabilityPolicies.SupplierLifecycle)
            .RequireIdempotencyKey()
            .Produces<SupplierLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        party.MapPost("/customers/{customerId:guid}/soft-delete", SoftDeleteCustomerAsync)
            .WithName(SoftDeleteCustomerOperationId)
            .RequireAuthorization(CapabilityPolicies.CustomerLifecycle)
            .RequireIdempotencyKey()
            .Produces<CustomerLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        party.MapPost("/customers/{customerId:guid}/restore", RestoreCustomerAsync)
            .WithName(RestoreCustomerOperationId)
            .RequireAuthorization(CapabilityPolicies.CustomerLifecycle)
            .RequireIdempotencyKey()
            .Produces<CustomerLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        party.MapPost("/outsourced-vendors/{outsourcedVendorId:guid}/soft-delete", SoftDeleteOutsourcedVendorAsync)
            .WithName(SoftDeleteOutsourcedVendorOperationId)
            .RequireAuthorization(CapabilityPolicies.OutsourcedVendorLifecycle)
            .RequireIdempotencyKey()
            .Produces<OutsourcedVendorLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        party.MapPost("/outsourced-vendors/{outsourcedVendorId:guid}/restore", RestoreOutsourcedVendorAsync)
            .WithName(RestoreOutsourcedVendorOperationId)
            .RequireAuthorization(CapabilityPolicies.OutsourcedVendorLifecycle)
            .RequireIdempotencyKey()
            .Produces<OutsourcedVendorLifecycleResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> SoftDeleteSupplierAsync(
        Guid supplierId,
        SupplierLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ISupplierLifecycleExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1)
        {
            return InvalidTransportVersion(httpContext);
        }

        var command = new SoftDeleteSupplierCommand(supplierId, request.ExpectedRowVersion);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalSoftDeleteSupplierRequest(SoftDeleteSupplierCommandType, command),
                CanonicalCommandJsonOptions));
        var execution = new SoftDeleteSupplierExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.SoftDeleteAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Supplier Soft Delete failed.");
    }

    private static async Task<IResult> RestoreSupplierAsync(
        Guid supplierId,
        SupplierLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ISupplierLifecycleExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1)
        {
            return InvalidTransportVersion(httpContext);
        }

        var command = new RestoreSupplierCommand(supplierId, request.ExpectedRowVersion);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalRestoreSupplierRequest(RestoreSupplierCommandType, command),
                CanonicalCommandJsonOptions));
        var execution = new RestoreSupplierExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.RestoreAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Supplier Restore failed.");
    }

    private static async Task<IResult> SoftDeleteCustomerAsync(
        Guid customerId,
        CustomerLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ICustomerLifecycleExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1)
        {
            return InvalidTransportVersion(httpContext);
        }

        var command = new SoftDeleteCustomerCommand(customerId, request.ExpectedRowVersion);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalSoftDeleteCustomerRequest(SoftDeleteCustomerCommandType, command),
                CanonicalCommandJsonOptions));
        var execution = new SoftDeleteCustomerExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.SoftDeleteAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Customer Soft Delete failed.");
    }

    private static async Task<IResult> RestoreCustomerAsync(
        Guid customerId,
        CustomerLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ICustomerLifecycleExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1)
        {
            return InvalidTransportVersion(httpContext);
        }

        var command = new RestoreCustomerCommand(customerId, request.ExpectedRowVersion);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalRestoreCustomerRequest(RestoreCustomerCommandType, command),
                CanonicalCommandJsonOptions));
        var execution = new RestoreCustomerExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.RestoreAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Customer Restore failed.");
    }

    private static async Task<IResult> SoftDeleteOutsourcedVendorAsync(
        Guid outsourcedVendorId,
        OutsourcedVendorLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IOutsourcedVendorLifecycleExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1)
        {
            return InvalidTransportVersion(httpContext);
        }

        var command = new SoftDeleteOutsourcedVendorCommand(outsourcedVendorId, request.ExpectedRowVersion);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalSoftDeleteOutsourcedVendorRequest(SoftDeleteOutsourcedVendorCommandType, command),
                CanonicalCommandJsonOptions));
        var execution = new SoftDeleteOutsourcedVendorExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.SoftDeleteAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Outsourced Vendor Soft Delete failed.");
    }

    private static async Task<IResult> RestoreOutsourcedVendorAsync(
        Guid outsourcedVendorId,
        OutsourcedVendorLifecycleRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IOutsourcedVendorLifecycleExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1)
        {
            return InvalidTransportVersion(httpContext);
        }

        var command = new RestoreOutsourcedVendorCommand(outsourcedVendorId, request.ExpectedRowVersion);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalRestoreOutsourcedVendorRequest(RestoreOutsourcedVendorCommandType, command),
                CanonicalCommandJsonOptions));
        var execution = new RestoreOutsourcedVendorExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.RestoreAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Outsourced Vendor Restore failed.");
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

    private sealed record CanonicalSoftDeleteSupplierRequest(
        string CommandType,
        SoftDeleteSupplierCommand Command);

    private sealed record CanonicalRestoreSupplierRequest(
        string CommandType,
        RestoreSupplierCommand Command);

    private sealed record CanonicalSoftDeleteCustomerRequest(
        string CommandType,
        SoftDeleteCustomerCommand Command);

    private sealed record CanonicalRestoreCustomerRequest(
        string CommandType,
        RestoreCustomerCommand Command);

    private sealed record CanonicalSoftDeleteOutsourcedVendorRequest(
        string CommandType,
        SoftDeleteOutsourcedVendorCommand Command);

    private sealed record CanonicalRestoreOutsourcedVendorRequest(
        string CommandType,
        RestoreOutsourcedVendorCommand Command);
}

public sealed record SupplierLifecycleRequest(long ExpectedRowVersion);
public sealed record CustomerLifecycleRequest(long ExpectedRowVersion);
public sealed record OutsourcedVendorLifecycleRequest(long ExpectedRowVersion);
