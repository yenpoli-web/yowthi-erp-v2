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
using YowThi.Erp.Application.DataProtection;
using YowThi.Erp.Application.Processing;

namespace YowThi.Erp.Api.DataProtection;

public static class DataProtectionEndpoints
{
    public const string HardDeleteSupplierOperationId = "DataProtection_HardDeleteSupplier";
    public const string HardDeleteSupplierCommandType = "HardDeleteSupplier";
    public const string HardDeleteCustomerOperationId = "DataProtection_HardDeleteCustomer";
    public const string HardDeleteCustomerCommandType = "HardDeleteCustomer";
    public const string HardDeleteOutsourcedVendorOperationId = "DataProtection_HardDeleteOutsourcedVendor";
    public const string HardDeleteOutsourcedVendorCommandType = "HardDeleteOutsourcedVendor";
    public const string HardDeleteFarmerOperationId = "DataProtection_HardDeleteFarmer";
    public const string HardDeleteFarmerCommandType = "HardDeleteFarmer";
    public const string HardDeleteProcessingExecutionOperationId = "DataProtection_HardDeleteProcessingExecution";
    public const string HardDeleteProcessingExecutionInputOperationId = "DataProtection_HardDeleteProcessingExecutionInput";
    public const string HardDeleteProcessingExecutionOutputOperationId = "DataProtection_HardDeleteProcessingExecutionOutput";

    private static readonly JsonSerializerOptions CanonicalCommandJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapDataProtectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var dataProtection = endpoints.MapApiV1().MapGroup("/data-protection");

        dataProtection.MapPost("/suppliers/{supplierId:guid}/hard-delete", HardDeleteSupplierAsync)
            .WithName(HardDeleteSupplierOperationId)
            .RequireAuthorization(CapabilityPolicies.DataProtectionHardDelete)
            .RequireIdempotencyKey()
            .RequireRecentDeletionReauthentication()
            .Produces<HardDeleteSupplierResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        dataProtection.MapPost("/customers/{customerId:guid}/hard-delete", HardDeleteCustomerAsync)
            .WithName(HardDeleteCustomerOperationId)
            .RequireAuthorization(CapabilityPolicies.DataProtectionHardDelete)
            .RequireIdempotencyKey()
            .RequireRecentDeletionReauthentication()
            .Produces<HardDeleteCustomerResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        dataProtection.MapPost("/outsourced-vendors/{outsourcedVendorId:guid}/hard-delete", HardDeleteOutsourcedVendorAsync)
            .WithName(HardDeleteOutsourcedVendorOperationId)
            .RequireAuthorization(CapabilityPolicies.DataProtectionHardDelete)
            .RequireIdempotencyKey()
            .RequireRecentDeletionReauthentication()
            .Produces<HardDeleteOutsourcedVendorResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        dataProtection.MapPost("/farmers/{farmerId:guid}/hard-delete", HardDeleteFarmerAsync)
            .WithName(HardDeleteFarmerOperationId)
            .RequireAuthorization(CapabilityPolicies.DataProtectionHardDelete)
            .RequireIdempotencyKey()
            .RequireRecentDeletionReauthentication()
            .Produces<HardDeleteFarmerResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        dataProtection.MapPost("/processing-executions/{processingExecutionId:guid}/hard-delete", HardDeleteProcessingExecutionAsync)
            .WithName(HardDeleteProcessingExecutionOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcessingTransactionLifecycle)
            .RequireAuthorization(CapabilityPolicies.DataProtectionHardDelete)
            .RequireIdempotencyKey()
            .RequireRecentDeletionReauthentication()
            .Produces<HardDeleteProcessingTransactionResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        dataProtection.MapPost("/processing-execution-inputs/{processingExecutionId:guid}/hard-delete", HardDeleteProcessingExecutionInputAsync)
            .WithName(HardDeleteProcessingExecutionInputOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcessingTransactionLifecycle)
            .RequireAuthorization(CapabilityPolicies.DataProtectionHardDelete)
            .RequireIdempotencyKey()
            .RequireRecentDeletionReauthentication()
            .Produces<HardDeleteProcessingTransactionResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        dataProtection.MapPost("/processing-execution-outputs/{processingExecutionOutputId:guid}/hard-delete", HardDeleteProcessingExecutionOutputAsync)
            .WithName(HardDeleteProcessingExecutionOutputOperationId)
            .RequireAuthorization(CapabilityPolicies.ProcessingTransactionLifecycle)
            .RequireAuthorization(CapabilityPolicies.DataProtectionHardDelete)
            .RequireIdempotencyKey()
            .RequireRecentDeletionReauthentication()
            .Produces<HardDeleteProcessingTransactionResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> HardDeleteSupplierAsync(
        Guid supplierId,
        HardDeleteSupplierRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IHardDeleteSupplierExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1)
            return InvalidTransportVersion(httpContext);

        var command = new HardDeleteSupplierCommand(supplierId, request.ExpectedRowVersion);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalHardDeleteSupplierRequest(HardDeleteSupplierCommandType, command),
                CanonicalCommandJsonOptions));
        var execution = new HardDeleteSupplierExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.ExecuteAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Supplier Hard Delete failed.");
    }

    private static async Task<IResult> HardDeleteCustomerAsync(
        Guid customerId,
        HardDeleteCustomerRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IHardDeleteCustomerExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1)
            return InvalidTransportVersion(httpContext);

        var command = new HardDeleteCustomerCommand(customerId, request.ExpectedRowVersion);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalHardDeleteCustomerRequest(HardDeleteCustomerCommandType, command),
                CanonicalCommandJsonOptions));
        var execution = new HardDeleteCustomerExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.ExecuteAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Customer Hard Delete failed.");
    }

    private static async Task<IResult> HardDeleteOutsourcedVendorAsync(
        Guid outsourcedVendorId,
        HardDeleteOutsourcedVendorRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IHardDeleteOutsourcedVendorExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1)
            return InvalidTransportVersion(httpContext);

        var command = new HardDeleteOutsourcedVendorCommand(outsourcedVendorId, request.ExpectedRowVersion);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalHardDeleteOutsourcedVendorRequest(HardDeleteOutsourcedVendorCommandType, command),
                CanonicalCommandJsonOptions));
        var execution = new HardDeleteOutsourcedVendorExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.ExecuteAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Outsourced Vendor Hard Delete failed.");
    }

    private static async Task<IResult> HardDeleteFarmerAsync(
        Guid farmerId,
        HardDeleteFarmerRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IHardDeleteFarmerExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1)
            return InvalidTransportVersion(httpContext);

        var command = new HardDeleteFarmerCommand(farmerId, request.ExpectedRowVersion);
        var canonicalPayload = JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalHardDeleteFarmerRequest(HardDeleteFarmerCommandType, command),
                CanonicalCommandJsonOptions));
        var execution = new HardDeleteFarmerExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(canonicalPayload),
            actorContext.ActorAccountId,
            command);

        var result = await executor.ExecuteAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Farmer Hard Delete failed.");
    }

    private static async Task<IResult> HardDeleteProcessingExecutionAsync(
        Guid processingExecutionId,
        HardDeleteProcessingRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IHardDeleteProcessingTransactionExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1) return InvalidTransportVersion(httpContext);
        var command = new HardDeleteProcessingExecutionCommand(processingExecutionId, request.ExpectedRowVersion);
        var execution = new HardDeleteProcessingExecutionExecution(
            httpContext.GetRequiredCommandId(),
            Hash(requestHasher, "HardDeleteProcessingExecution", command),
            actorContext.ActorAccountId,
            command);
        var result = await executor.HardDeleteExecutionAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Processing Execution Hard Delete failed.");
    }

    private static async Task<IResult> HardDeleteProcessingExecutionInputAsync(
        Guid processingExecutionId,
        HardDeleteProcessingRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IHardDeleteProcessingTransactionExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1) return InvalidTransportVersion(httpContext);
        var command = new HardDeleteProcessingExecutionInputCommand(processingExecutionId, request.ExpectedRowVersion);
        var execution = new HardDeleteProcessingExecutionInputExecution(
            httpContext.GetRequiredCommandId(),
            Hash(requestHasher, "HardDeleteProcessingExecutionInput", command),
            actorContext.ActorAccountId,
            command);
        var result = await executor.HardDeleteInputAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Processing Execution Input Hard Delete failed.");
    }

    private static async Task<IResult> HardDeleteProcessingExecutionOutputAsync(
        Guid processingExecutionOutputId,
        HardDeleteProcessingRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IHardDeleteProcessingTransactionExecutor executor,
        CancellationToken cancellationToken)
    {
        if (request.ExpectedRowVersion < 1) return InvalidTransportVersion(httpContext);
        var command = new HardDeleteProcessingExecutionOutputCommand(processingExecutionOutputId, request.ExpectedRowVersion);
        var execution = new HardDeleteProcessingExecutionOutputExecution(
            httpContext.GetRequiredCommandId(),
            Hash(requestHasher, "HardDeleteProcessingExecutionOutput", command),
            actorContext.ActorAccountId,
            command);
        var result = await executor.HardDeleteOutputAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : CreateFailureResult(httpContext, result.Error, "Processing Execution Output Hard Delete failed.");
    }

    private static CommandRequestHash Hash<TCommand>(
        ICommandRequestHasher requestHasher,
        string commandType,
        TCommand command) =>
        requestHasher.Compute(JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalCommand<TCommand>(commandType, command),
                CanonicalCommandJsonOptions)));

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

    private sealed record CanonicalHardDeleteSupplierRequest(
        string CommandType,
        HardDeleteSupplierCommand Command);

    private sealed record CanonicalHardDeleteCustomerRequest(
        string CommandType,
        HardDeleteCustomerCommand Command);

    private sealed record CanonicalHardDeleteOutsourcedVendorRequest(
        string CommandType,
        HardDeleteOutsourcedVendorCommand Command);

    private sealed record CanonicalHardDeleteFarmerRequest(
        string CommandType,
        HardDeleteFarmerCommand Command);

    private sealed record CanonicalCommand<TCommand>(string CommandType, TCommand Command);
}

public sealed record HardDeleteSupplierRequest(long ExpectedRowVersion);
public sealed record HardDeleteCustomerRequest(long ExpectedRowVersion);
public sealed record HardDeleteOutsourcedVendorRequest(long ExpectedRowVersion);
public sealed record HardDeleteFarmerRequest(long ExpectedRowVersion);
public sealed record HardDeleteProcessingRequest(long ExpectedRowVersion);