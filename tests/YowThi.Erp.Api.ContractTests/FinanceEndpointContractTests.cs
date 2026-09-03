using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Api.Finance;
using YowThi.Erp.Api.Hosting;
using YowThi.Erp.Api.Idempotency;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Serialization;
using YowThi.Erp.Application.Finance;
using YowThi.Erp.Domain.Finance;

namespace YowThi.Erp.Api.ContractTests;

public sealed class FinanceEndpointContractTests
{
    [Fact]
    public async Task Finance_write_endpoints_match_v1_contract()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapFinanceEndpoints();

        AssertEndpointContract(
            GetEndpoint(app, "/api/v1/finance/payables/{payableId:guid}/adjustments"),
            FinanceEndpoints.AddPayableAdjustmentOperationId);
        AssertEndpointContract(
            GetEndpoint(app, "/api/v1/finance/payables/{payableId:guid}/payments"),
            FinanceEndpoints.PayPayableOperationId);
        AssertEndpointContract(
            GetEndpoint(app, "/api/v1/finance/receivables/{receivableId:guid}/receipts"),
            FinanceEndpoints.ReceiveReceivableOperationId);
    }

    [Fact]
    public async Task Add_payable_adjustment_uses_route_id_string_enum_and_expected_outstanding_version()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapFinanceEndpoints();

        var payableId = Guid.NewGuid();
        var adjustmentId = Guid.NewGuid();
        fixture.AdjustmentExecutor.Result = ApplicationResult<AddPayableAdjustmentResult>.Success(
            new AddPayableAdjustmentResult(adjustmentId, payableId, -125, 875, 8, DateTimeOffset.UtcNow));

        var response = await InvokeAsync(
            app,
            GetEndpoint(app, "/api/v1/finance/payables/{payableId:guid}/adjustments"),
            "payableId",
            payableId,
            Guid.NewGuid(),
            """{"expectedOutstandingVersion":7,"adjustmentType":"SUPPLIER_QUALITY_WEIGHT_DEDUCTION","amountDeltaThb":-125,"reasonText":"quality"}""");

        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        Assert.NotNull(fixture.AdjustmentExecutor.LastExecution);
        Assert.Equal(payableId, fixture.AdjustmentExecutor.LastExecution.Command.PayableId);
        Assert.Equal(7, fixture.AdjustmentExecutor.LastExecution.Command.ExpectedOutstandingVersion);
        Assert.Equal(
            PayableAdjustmentType.SUPPLIER_QUALITY_WEIGHT_DEDUCTION,
            fixture.AdjustmentExecutor.LastExecution.Command.AdjustmentType);
        Assert.Equal(-125, fixture.AdjustmentExecutor.LastExecution.Command.AmountDeltaThb);
        Assert.Equal("quality", fixture.AdjustmentExecutor.LastExecution.Command.ReasonText);

        AssertCanonical(
            fixture.Hasher.LastPayloadJson,
            FinanceEndpoints.AddPayableAdjustmentCommandType,
            command =>
            {
                Assert.Equal(payableId, command.GetProperty("payableId").GetGuid());
                Assert.Equal(7, command.GetProperty("expectedOutstandingVersion").GetInt64());
                Assert.Equal(
                    "SUPPLIER_QUALITY_WEIGHT_DEDUCTION",
                    command.GetProperty("adjustmentType").GetString());
                Assert.Equal(-125, command.GetProperty("amountDeltaThb").GetInt64());
            });

        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(adjustmentId, json.RootElement.GetProperty("payableAdjustmentId").GetGuid());
        Assert.Equal(payableId, json.RootElement.GetProperty("payableId").GetGuid());
        Assert.Equal(875, json.RootElement.GetProperty("outstandingThb").GetInt64());
        Assert.Equal(8, json.RootElement.GetProperty("outstandingVersion").GetInt64());
    }

    [Fact]
    public async Task Pay_payable_preserves_null_amount_for_full_settlement_in_canonical_command()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapFinanceEndpoints();

        var payableId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var confirmedAt = DateTimeOffset.UtcNow;
        fixture.PaymentExecutor.Result = ApplicationResult<PayPayableResult>.Success(
            new PayPayableResult(paymentId, payableId, 700, 0, 6, confirmedAt));

        var response = await InvokeAsync(
            app,
            GetEndpoint(app, "/api/v1/finance/payables/{payableId:guid}/payments"),
            "payableId",
            payableId,
            Guid.NewGuid(),
            """{"amountThb":null,"expectedOutstandingVersion":5}""");

        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        Assert.NotNull(fixture.PaymentExecutor.LastExecution);
        Assert.Equal(payableId, fixture.PaymentExecutor.LastExecution.Command.PayableId);
        Assert.Null(fixture.PaymentExecutor.LastExecution.Command.AmountThb);
        Assert.Equal(5, fixture.PaymentExecutor.LastExecution.Command.ExpectedOutstandingVersion);

        AssertCanonical(
            fixture.Hasher.LastPayloadJson,
            FinanceEndpoints.PayPayableCommandType,
            command =>
            {
                Assert.Equal(payableId, command.GetProperty("payableId").GetGuid());
                Assert.Equal(JsonValueKind.Null, command.GetProperty("amountThb").ValueKind);
                Assert.Equal(5, command.GetProperty("expectedOutstandingVersion").GetInt64());
            });

        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(paymentId, json.RootElement.GetProperty("paymentId").GetGuid());
        Assert.Equal(700, json.RootElement.GetProperty("amountThb").GetInt64());
        Assert.Equal(0, json.RootElement.GetProperty("outstandingThb").GetInt64());
        Assert.Equal(6, json.RootElement.GetProperty("outstandingVersion").GetInt64());
    }

    [Fact]
    public async Task Receive_receivable_uses_route_id_amount_and_expected_outstanding_version()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapFinanceEndpoints();

        var receivableId = Guid.NewGuid();
        var receiptId = Guid.NewGuid();
        fixture.ReceiptExecutor.Result = ApplicationResult<ReceiveReceivableResult>.Success(
            new ReceiveReceivableResult(receiptId, receivableId, 400, 600, 4, DateTimeOffset.UtcNow));

        var response = await InvokeAsync(
            app,
            GetEndpoint(app, "/api/v1/finance/receivables/{receivableId:guid}/receipts"),
            "receivableId",
            receivableId,
            Guid.NewGuid(),
            """{"amountThb":400,"expectedOutstandingVersion":3}""");

        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        Assert.NotNull(fixture.ReceiptExecutor.LastExecution);
        Assert.Equal(receivableId, fixture.ReceiptExecutor.LastExecution.Command.ReceivableId);
        Assert.Equal(400, fixture.ReceiptExecutor.LastExecution.Command.AmountThb);
        Assert.Equal(3, fixture.ReceiptExecutor.LastExecution.Command.ExpectedOutstandingVersion);

        AssertCanonical(
            fixture.Hasher.LastPayloadJson,
            FinanceEndpoints.ReceiveReceivableCommandType,
            command =>
            {
                Assert.Equal(receivableId, command.GetProperty("receivableId").GetGuid());
                Assert.Equal(400, command.GetProperty("amountThb").GetInt64());
                Assert.Equal(3, command.GetProperty("expectedOutstandingVersion").GetInt64());
            });

        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(receiptId, json.RootElement.GetProperty("receiptId").GetGuid());
        Assert.Equal(receivableId, json.RootElement.GetProperty("receivableId").GetGuid());
        Assert.Equal(600, json.RootElement.GetProperty("outstandingThb").GetInt64());
        Assert.Equal(4, json.RootElement.GetProperty("outstandingVersion").GetInt64());
    }

    [Fact]
    public async Task Finance_write_rejects_invalid_expected_outstanding_version_as_transport_validation()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapFinanceEndpoints();

        var response = await InvokeAsync(
            app,
            GetEndpoint(app, "/api/v1/finance/payables/{payableId:guid}/payments"),
            "payableId",
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"amountThb":100,"expectedOutstandingVersion":0}""");

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Null(fixture.PaymentExecutor.LastExecution);
        AssertProblemCode(response.Body, ApiErrorCodes.RequestValidationFailed);
    }

    [Fact]
    public async Task Pay_payable_maps_outstanding_change_and_transport_block_to_conflict()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapFinanceEndpoints();

        fixture.PaymentExecutor.Result = ApplicationResult<PayPayableResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Conflict,
                FinanceApplicationErrorCodes.OutstandingChanged));
        var changed = await InvokeAsync(
            app,
            GetEndpoint(app, "/api/v1/finance/payables/{payableId:guid}/payments"),
            "payableId",
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"amountThb":100,"expectedOutstandingVersion":2}""");
        Assert.Equal(StatusCodes.Status409Conflict, changed.StatusCode);
        AssertProblemCode(changed.Body, FinanceApplicationErrorCodes.OutstandingChanged);

        fixture.PaymentExecutor.Result = ApplicationResult<PayPayableResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Conflict,
                FinanceApplicationErrorCodes.TransportSettlementUnavailable));
        var transport = await InvokeAsync(
            app,
            GetEndpoint(app, "/api/v1/finance/payables/{payableId:guid}/payments"),
            "payableId",
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"amountThb":100,"expectedOutstandingVersion":2}""");
        Assert.Equal(StatusCodes.Status409Conflict, transport.StatusCode);
        AssertProblemCode(transport.Body, FinanceApplicationErrorCodes.TransportSettlementUnavailable);
    }

    [Fact]
    public async Task Finance_semantic_validation_and_over_collection_keep_stable_problem_codes()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapFinanceEndpoints();

        fixture.AdjustmentExecutor.Result = ApplicationResult<AddPayableAdjustmentResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Validation,
                FinanceApplicationErrorCodes.InvalidInput));
        var invalidAdjustment = await InvokeAsync(
            app,
            GetEndpoint(app, "/api/v1/finance/payables/{payableId:guid}/adjustments"),
            "payableId",
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"expectedOutstandingVersion":1,"adjustmentType":"SUPPLIER_QUALITY_WEIGHT_DEDUCTION","amountDeltaThb":0,"reasonText":null}""");
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, invalidAdjustment.StatusCode);
        AssertProblemCode(invalidAdjustment.Body, FinanceApplicationErrorCodes.InvalidInput);

        fixture.ReceiptExecutor.Result = ApplicationResult<ReceiveReceivableResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Conflict,
                FinanceApplicationErrorCodes.ReceiptExceedsOutstanding));
        var overCollection = await InvokeAsync(
            app,
            GetEndpoint(app, "/api/v1/finance/receivables/{receivableId:guid}/receipts"),
            "receivableId",
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"amountThb":1000,"expectedOutstandingVersion":3}""");
        Assert.Equal(StatusCodes.Status409Conflict, overCollection.StatusCode);
        AssertProblemCode(overCollection.Body, FinanceApplicationErrorCodes.ReceiptExceedsOutstanding);
    }

    private static void AssertEndpointContract(RouteEndpoint endpoint, string operationId)
    {
        Assert.Contains(HttpMethods.Post, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        Assert.Equal(operationId, endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            metadata => metadata.Policy == CapabilityPolicies.FinancePay);
        Assert.NotNull(endpoint.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());

        var statuses = endpoint.Metadata
            .GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode)
            .ToHashSet();
        Assert.Contains(StatusCodes.Status201Created, statuses);
        Assert.Contains(StatusCodes.Status400BadRequest, statuses);
        Assert.Contains(StatusCodes.Status401Unauthorized, statuses);
        Assert.Contains(StatusCodes.Status403Forbidden, statuses);
        Assert.Contains(StatusCodes.Status404NotFound, statuses);
        Assert.Contains(StatusCodes.Status409Conflict, statuses);
        Assert.Contains(StatusCodes.Status422UnprocessableEntity, statuses);
    }

    private static void AssertCanonical(
        string? payloadJson,
        string commandType,
        Action<JsonElement> assertCommand)
    {
        Assert.NotNull(payloadJson);
        using var canonical = JsonDocument.Parse(payloadJson);
        Assert.Equal(commandType, canonical.RootElement.GetProperty("commandType").GetString());
        assertCommand(canonical.RootElement.GetProperty("command"));
    }

    private static void AssertProblemCode(string body, string expectedCode)
    {
        using var document = JsonDocument.Parse(body);
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
        Assert.Equal($"urn:yowthi:error:{expectedCode}", document.RootElement.GetProperty("type").GetString());
    }

    private static async Task<HttpInvocationResult> InvokeAsync(
        WebApplication app,
        RouteEndpoint endpoint,
        string routeKey,
        Guid routeId,
        Guid commandId,
        string body)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        await using var requestBody = new MemoryStream(bodyBytes);
        await using var responseBody = new MemoryStream();

        var context = new DefaultHttpContext
        {
            RequestServices = app.Services,
        };
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new RequestBodyDetectionFeature());
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = endpoint.RoutePattern.RawText!
            .Replace($"{{{routeKey}:guid}}", routeId.ToString(), StringComparison.Ordinal);
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = bodyBytes.Length;
        context.Request.Body = requestBody;
        context.Request.Headers[IdempotencyKeyEndpointFilter.HeaderName] = commandId.ToString();
        context.Request.RouteValues[routeKey] = routeId.ToString();
        context.Response.Body = responseBody;
        context.SetEndpoint(endpoint);

        Assert.NotNull(endpoint.RequestDelegate);
        await endpoint.RequestDelegate(context);

        responseBody.Position = 0;
        using var reader = new StreamReader(responseBody, Encoding.UTF8, leaveOpen: true);
        var responseText = await reader.ReadToEndAsync();
        return new HttpInvocationResult(context.Response.StatusCode, responseText);
    }

    private static RouteEndpoint GetEndpoint(WebApplication app, string rawText) =>
        Assert.Single(GetRouteEndpoints(app), endpoint => endpoint.RoutePattern.RawText == rawText);

    private static AppFixture CreateApp()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = Environments.Development,
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{YowThi.Erp.Api.Localization.ApiLocalizationOptions.SectionName}:DefaultLocale"] = "th-TH",
        });
        builder.AddYowThiApi();

        var adjustmentExecutor = new StubAddPayableAdjustmentExecutor();
        var paymentExecutor = new StubPayPayableExecutor();
        var receiptExecutor = new StubReceiveReceivableExecutor();
        var hasher = new CapturingRequestHasher();
        builder.Services.AddSingleton<IActorContext>(
            new StubActorContext(ActorAccountId.From(Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339d0"))));
        builder.Services.AddSingleton<ICommandRequestHasher>(hasher);
        builder.Services.AddSingleton<IAddPayableAdjustmentExecutor>(adjustmentExecutor);
        builder.Services.AddSingleton<IPayPayableExecutor>(paymentExecutor);
        builder.Services.AddSingleton<IReceiveReceivableExecutor>(receiptExecutor);

        var app = builder.Build();
        app.UseYowThiApiInfrastructure();
        app.MapYowThiTechnicalEndpoints();
        return new AppFixture(app, adjustmentExecutor, paymentExecutor, receiptExecutor, hasher);
    }

    private static RouteEndpoint[] GetRouteEndpoints(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

    private sealed record AppFixture(
        WebApplication App,
        StubAddPayableAdjustmentExecutor AdjustmentExecutor,
        StubPayPayableExecutor PaymentExecutor,
        StubReceiveReceivableExecutor ReceiptExecutor,
        CapturingRequestHasher Hasher);

    private sealed record HttpInvocationResult(int StatusCode, string Body);

    private sealed class RequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }

    private sealed class StubActorContext(ActorAccountId actorAccountId) : IActorContext
    {
        public ActorAccountId ActorAccountId { get; } = actorAccountId;
    }

    private sealed class CapturingRequestHasher : ICommandRequestHasher
    {
        public string? LastPayloadJson { get; private set; }

        public CommandRequestHash Compute(JsonPayload canonicalCommandPayload)
        {
            LastPayloadJson = Encoding.UTF8.GetString(canonicalCommandPayload.Utf8Json.Span);
            return CommandRequestHash.FromSha256(
                Enumerable.Repeat((byte)0x5A, CommandRequestHash.Sha256Length).ToArray());
        }
    }

    private sealed class StubAddPayableAdjustmentExecutor : IAddPayableAdjustmentExecutor
    {
        public ApplicationResult<AddPayableAdjustmentResult> Result { get; set; } =
            ApplicationResult<AddPayableAdjustmentResult>.Success(
                new AddPayableAdjustmentResult(
                    Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339d1"),
                    Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339d2"),
                    -1,
                    99,
                    2,
                    DateTimeOffset.UnixEpoch));

        public AddPayableAdjustmentExecution? LastExecution { get; private set; }

        public ValueTask<ApplicationResult<AddPayableAdjustmentResult>> ExecuteAsync(
            AddPayableAdjustmentExecution execution,
            CancellationToken cancellationToken)
        {
            LastExecution = execution;
            return ValueTask.FromResult(Result);
        }
    }

    private sealed class StubPayPayableExecutor : IPayPayableExecutor
    {
        public ApplicationResult<PayPayableResult> Result { get; set; } =
            ApplicationResult<PayPayableResult>.Success(
                new PayPayableResult(
                    Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339d3"),
                    Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339d4"),
                    1,
                    0,
                    2,
                    DateTimeOffset.UnixEpoch));

        public PayPayableExecution? LastExecution { get; private set; }

        public ValueTask<ApplicationResult<PayPayableResult>> ExecuteAsync(
            PayPayableExecution execution,
            CancellationToken cancellationToken)
        {
            LastExecution = execution;
            return ValueTask.FromResult(Result);
        }
    }

    private sealed class StubReceiveReceivableExecutor : IReceiveReceivableExecutor
    {
        public ApplicationResult<ReceiveReceivableResult> Result { get; set; } =
            ApplicationResult<ReceiveReceivableResult>.Success(
                new ReceiveReceivableResult(
                    Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339d5"),
                    Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339d6"),
                    1,
                    0,
                    2,
                    DateTimeOffset.UnixEpoch));

        public ReceiveReceivableExecution? LastExecution { get; private set; }

        public ValueTask<ApplicationResult<ReceiveReceivableResult>> ExecuteAsync(
            ReceiveReceivableExecution execution,
            CancellationToken cancellationToken)
        {
            LastExecution = execution;
            return ValueTask.FromResult(Result);
        }
    }
}
