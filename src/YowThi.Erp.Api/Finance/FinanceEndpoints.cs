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
using YowThi.Erp.Application.Finance;
using YowThi.Erp.Domain.Finance;

namespace YowThi.Erp.Api.Finance;

public static class FinanceEndpoints
{
    public const string AddPayableAdjustmentOperationId = "Finance_AddPayableAdjustment";
    public const string PayPayableOperationId = "Finance_PayPayable";
    public const string ReceiveReceivableOperationId = "Finance_ReceiveReceivable";
    public const string CorrectPaymentAmountOperationId = "Finance_CorrectPaymentAmount";
    public const string CorrectReceiptAmountOperationId = "Finance_CorrectReceiptAmount";

    public const string AddPayableAdjustmentCommandType = "AddPayableAdjustment";
    public const string PayPayableCommandType = "PayPayable";
    public const string ReceiveReceivableCommandType = "ReceiveReceivable";
    public const string CorrectPaymentAmountCommandType = "CorrectPaymentAmount";
    public const string CorrectReceiptAmountCommandType = "CorrectReceiptAmount";

    private static readonly JsonSerializerOptions CanonicalCommandJsonOptions = CreateCanonicalCommandJsonOptions();

    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var finance = endpoints.MapApiV1().MapGroup("/finance");

        finance.MapPost("/payables/{payableId:guid}/adjustments", AddPayableAdjustmentAsync)
            .WithName(AddPayableAdjustmentOperationId)
            .RequireAuthorization(CapabilityPolicies.FinancePay)
            .RequireIdempotencyKey()
            .Produces<AddPayableAdjustmentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        finance.MapPost("/payables/{payableId:guid}/payments", PayPayableAsync)
            .WithName(PayPayableOperationId)
            .RequireAuthorization(CapabilityPolicies.FinancePay)
            .RequireIdempotencyKey()
            .Produces<PayPayableResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        finance.MapPost("/receivables/{receivableId:guid}/receipts", ReceiveReceivableAsync)
            .WithName(ReceiveReceivableOperationId)
            .RequireAuthorization(CapabilityPolicies.FinancePay)
            .RequireIdempotencyKey()
            .Produces<ReceiveReceivableResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        finance.MapPost(
                "/payables/{payableId:guid}/payments/{paymentId:guid}/correct-amount",
                CorrectPaymentAmountAsync)
            .WithName(CorrectPaymentAmountOperationId)
            .RequireAuthorization(CapabilityPolicies.FinanceCorrect)
            .RequireIdempotencyKey()
            .Produces<CorrectPaymentAmountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        finance.MapPost(
                "/receivables/{receivableId:guid}/receipts/{receiptId:guid}/correct-amount",
                CorrectReceiptAmountAsync)
            .WithName(CorrectReceiptAmountOperationId)
            .RequireAuthorization(CapabilityPolicies.FinanceCorrect)
            .RequireIdempotencyKey()
            .Produces<CorrectReceiptAmountResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> AddPayableAdjustmentAsync(
        Guid payableId,
        AddPayableAdjustmentRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IAddPayableAdjustmentExecutor executor,
        CancellationToken cancellationToken)
    {
        if (payableId == Guid.Empty || request.ExpectedOutstandingVersion < 1)
        {
            return TransportValidationFailure(httpContext);
        }

        var command = new AddPayableAdjustmentCommand(
            payableId,
            request.ExpectedOutstandingVersion,
            request.AdjustmentType,
            request.AmountDeltaThb,
            request.ReasonText);
        var execution = new AddPayableAdjustmentExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(CanonicalPayload(AddPayableAdjustmentCommandType, command)),
            actorContext.ActorAccountId,
            command);

        var result = await executor.ExecuteAsync(execution, cancellationToken);
        if (result.IsSuccess)
        {
            var response = new AddPayableAdjustmentResponse(
                result.Value.PayableAdjustmentId,
                result.Value.PayableId,
                result.Value.AmountDeltaThb,
                result.Value.OutstandingThb,
                result.Value.OutstandingVersion,
                result.Value.RecordedAt);

            return TypedResults.Created(
                $"/api/v1/finance/payables/{result.Value.PayableId}/adjustments/{result.Value.PayableAdjustmentId}",
                response);
        }

        return FinanceFailure(httpContext, result.Error, "Payable adjustment failed.");
    }

    private static async Task<IResult> PayPayableAsync(
        Guid payableId,
        PayPayableRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IPayPayableExecutor executor,
        CancellationToken cancellationToken)
    {
        if (payableId == Guid.Empty || request.ExpectedOutstandingVersion < 1)
        {
            return TransportValidationFailure(httpContext);
        }

        var command = new PayPayableCommand(
            payableId,
            request.AmountThb,
            request.ExpectedOutstandingVersion);
        var execution = new PayPayableExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(CanonicalPayload(PayPayableCommandType, command)),
            actorContext.ActorAccountId,
            command);

        var result = await executor.ExecuteAsync(execution, cancellationToken);
        if (result.IsSuccess)
        {
            var response = new PayPayableResponse(
                result.Value.PaymentId,
                result.Value.PayableId,
                result.Value.AmountThb,
                result.Value.OutstandingThb,
                result.Value.OutstandingVersion,
                result.Value.ConfirmedAt);

            return TypedResults.Created(
                $"/api/v1/finance/payables/{result.Value.PayableId}/payments/{result.Value.PaymentId}",
                response);
        }

        return FinanceFailure(httpContext, result.Error, "Payable payment failed.");
    }

    private static async Task<IResult> ReceiveReceivableAsync(
        Guid receivableId,
        ReceiveReceivableRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] IReceiveReceivableExecutor executor,
        CancellationToken cancellationToken)
    {
        if (receivableId == Guid.Empty || request.ExpectedOutstandingVersion < 1)
        {
            return TransportValidationFailure(httpContext);
        }

        var command = new ReceiveReceivableCommand(
            receivableId,
            request.AmountThb,
            request.ExpectedOutstandingVersion);
        var execution = new ReceiveReceivableExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(CanonicalPayload(ReceiveReceivableCommandType, command)),
            actorContext.ActorAccountId,
            command);

        var result = await executor.ExecuteAsync(execution, cancellationToken);
        if (result.IsSuccess)
        {
            var response = new ReceiveReceivableResponse(
                result.Value.ReceiptId,
                result.Value.ReceivableId,
                result.Value.AmountThb,
                result.Value.OutstandingThb,
                result.Value.OutstandingVersion,
                result.Value.ConfirmedAt);

            return TypedResults.Created(
                $"/api/v1/finance/receivables/{result.Value.ReceivableId}/receipts/{result.Value.ReceiptId}",
                response);
        }

        return FinanceFailure(httpContext, result.Error, "Receivable receipt failed.");
    }

    private static async Task<IResult> CorrectPaymentAmountAsync(
        Guid payableId,
        Guid paymentId,
        CorrectPaymentAmountRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ICorrectPaymentAmountExecutor executor,
        CancellationToken cancellationToken)
    {
        if (payableId == Guid.Empty || paymentId == Guid.Empty || request.ExpectedOutstandingVersion < 1)
        {
            return TransportValidationFailure(httpContext);
        }

        var command = new CorrectPaymentAmountCommand(
            payableId,
            paymentId,
            request.CorrectedAmountThb,
            request.ExpectedOutstandingVersion,
            request.ReasonText);
        var execution = new CorrectPaymentAmountExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(CanonicalPayload(CorrectPaymentAmountCommandType, command)),
            actorContext.ActorAccountId,
            command);

        var result = await executor.ExecuteAsync(execution, cancellationToken);
        if (result.IsSuccess)
        {
            return TypedResults.Ok(new CorrectPaymentAmountResponse(
                result.Value.PaymentId,
                result.Value.PayableId,
                result.Value.PreviousAmountThb,
                result.Value.CorrectedAmountThb,
                result.Value.OutstandingThb,
                result.Value.OutstandingVersion,
                result.Value.CorrectedAt));
        }

        return FinanceFailure(httpContext, result.Error, "Payment amount correction failed.");
    }

    private static async Task<IResult> CorrectReceiptAmountAsync(
        Guid receivableId,
        Guid receiptId,
        CorrectReceiptAmountRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ICorrectReceiptAmountExecutor executor,
        CancellationToken cancellationToken)
    {
        if (receivableId == Guid.Empty || receiptId == Guid.Empty || request.ExpectedOutstandingVersion < 1)
        {
            return TransportValidationFailure(httpContext);
        }

        var command = new CorrectReceiptAmountCommand(
            receivableId,
            receiptId,
            request.CorrectedAmountThb,
            request.ExpectedOutstandingVersion,
            request.ReasonText);
        var execution = new CorrectReceiptAmountExecution(
            httpContext.GetRequiredCommandId(),
            requestHasher.Compute(CanonicalPayload(CorrectReceiptAmountCommandType, command)),
            actorContext.ActorAccountId,
            command);

        var result = await executor.ExecuteAsync(execution, cancellationToken);
        if (result.IsSuccess)
        {
            return TypedResults.Ok(new CorrectReceiptAmountResponse(
                result.Value.ReceiptId,
                result.Value.ReceivableId,
                result.Value.PreviousAmountThb,
                result.Value.CorrectedAmountThb,
                result.Value.OutstandingThb,
                result.Value.OutstandingVersion,
                result.Value.CorrectedAt));
        }

        return FinanceFailure(httpContext, result.Error, "Receipt amount correction failed.");
    }

    private static JsonPayload CanonicalPayload<TCommand>(string commandType, TCommand command) =>
        JsonPayload.FromUtf8Json(
            JsonSerializer.SerializeToUtf8Bytes(
                new CanonicalFinanceCommand<TCommand>(commandType, command),
                CanonicalCommandJsonOptions));

    private static IResult TransportValidationFailure(HttpContext httpContext) =>
        ApiProblemResults.Create(
            httpContext,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.RequestValidationFailed,
            "Request validation failed.");

    private static IResult FinanceFailure(
        HttpContext httpContext,
        ApplicationError error,
        string detail)
    {
        var statusCode = error.Kind switch
        {
            ApplicationErrorKind.Validation => StatusCodes.Status422UnprocessableEntity,
            ApplicationErrorKind.NotFound => StatusCodes.Status404NotFound,
            ApplicationErrorKind.Conflict => StatusCodes.Status409Conflict,
            ApplicationErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            _ => throw new ArgumentOutOfRangeException(
                nameof(error.Kind),
                error.Kind,
                "Unsupported application error kind."),
        };

        return ApiProblemResults.Create(httpContext, statusCode, error.Code, detail);
    }

    private static JsonSerializerOptions CreateCanonicalCommandJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record CanonicalFinanceCommand<TCommand>(
        string CommandType,
        TCommand Command);
}

public sealed record AddPayableAdjustmentRequest(
    long ExpectedOutstandingVersion,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    PayableAdjustmentType AdjustmentType,
    long AmountDeltaThb,
    string? ReasonText);

public sealed record PayPayableRequest(
    long? AmountThb,
    long ExpectedOutstandingVersion);

public sealed record ReceiveReceivableRequest(
    long? AmountThb,
    long ExpectedOutstandingVersion);

public sealed record CorrectPaymentAmountRequest(
    long CorrectedAmountThb,
    long ExpectedOutstandingVersion,
    string? ReasonText);

public sealed record CorrectReceiptAmountRequest(
    long CorrectedAmountThb,
    long ExpectedOutstandingVersion,
    string? ReasonText);

public sealed record AddPayableAdjustmentResponse(
    Guid PayableAdjustmentId,
    Guid PayableId,
    long AmountDeltaThb,
    long OutstandingThb,
    long OutstandingVersion,
    DateTimeOffset RecordedAt);

public sealed record PayPayableResponse(
    Guid PaymentId,
    Guid PayableId,
    long AmountThb,
    long OutstandingThb,
    long OutstandingVersion,
    DateTimeOffset ConfirmedAt);

public sealed record ReceiveReceivableResponse(
    Guid ReceiptId,
    Guid ReceivableId,
    long AmountThb,
    long OutstandingThb,
    long OutstandingVersion,
    DateTimeOffset ConfirmedAt);

public sealed record CorrectPaymentAmountResponse(
    Guid PaymentId,
    Guid PayableId,
    long PreviousAmountThb,
    long CorrectedAmountThb,
    long OutstandingThb,
    long OutstandingVersion,
    DateTimeOffset CorrectedAt);

public sealed record CorrectReceiptAmountResponse(
    Guid ReceiptId,
    Guid ReceivableId,
    long PreviousAmountThb,
    long CorrectedAmountThb,
    long OutstandingThb,
    long OutstandingVersion,
    DateTimeOffset CorrectedAt);
