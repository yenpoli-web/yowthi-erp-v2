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

namespace YowThi.Erp.Api.ContractTests;

public sealed class PayableAdjustmentCorrectionEndpointContractTests
{
    private const string Route =
        "/api/v1/finance/payables/{payableId:guid}/adjustments/{adjustmentId:guid}/correct";

    [Fact]
    public async Task Payable_adjustment_correction_route_is_target_specific_authorized_idempotent_v1_post()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapFinanceEndpoints();

        var endpoint = GetEndpoint(app);
        Assert.Contains(HttpMethods.Post, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        Assert.Equal(
            FinanceEndpoints.CorrectPayableAdjustmentOperationId,
            endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            metadata => metadata.Policy == CapabilityPolicies.FinanceCorrect);
        Assert.DoesNotContain(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            metadata => metadata.Policy is CapabilityPolicies.FinancePay or CapabilityPolicies.DataProtectionHardDelete);
        Assert.NotNull(endpoint.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());

        var statuses = endpoint.Metadata
            .GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode)
            .ToHashSet();
        Assert.Contains(StatusCodes.Status200OK, statuses);
        Assert.Contains(StatusCodes.Status400BadRequest, statuses);
        Assert.Contains(StatusCodes.Status401Unauthorized, statuses);
        Assert.Contains(StatusCodes.Status403Forbidden, statuses);
        Assert.Contains(StatusCodes.Status404NotFound, statuses);
        Assert.Contains(StatusCodes.Status409Conflict, statuses);
        Assert.Contains(StatusCodes.Status422UnprocessableEntity, statuses);
    }

    [Fact]
    public async Task Payable_adjustment_correction_canonical_command_contains_route_ids_complete_corrected_state_and_correction_reason()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapFinanceEndpoints();

        var payableId = Guid.NewGuid();
        var adjustmentId = Guid.NewGuid();
        fixture.Executor.Result = ApplicationResult<CorrectPayableAdjustmentResult>.Success(
            new CorrectPayableAdjustmentResult(
                adjustmentId,
                payableId,
                -2000,
                -1000,
                "original reason",
                "corrected reason",
                9000,
                3,
                DateTimeOffset.UtcNow));

        var response = await InvokeAsync(
            app,
            payableId,
            adjustmentId,
            Guid.NewGuid(),
            """{"correctedAmountDeltaThb":-1000,"correctedReasonText":"corrected reason","expectedOutstandingVersion":2,"correctionReasonText":"entry correction"}""");

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.NotNull(fixture.Executor.LastExecution);
        Assert.Equal(payableId, fixture.Executor.LastExecution.Command.PayableId);
        Assert.Equal(adjustmentId, fixture.Executor.LastExecution.Command.PayableAdjustmentId);
        Assert.Equal(-1000, fixture.Executor.LastExecution.Command.CorrectedAmountDeltaThb);
        Assert.Equal("corrected reason", fixture.Executor.LastExecution.Command.CorrectedReasonText);
        Assert.Equal(2, fixture.Executor.LastExecution.Command.ExpectedOutstandingVersion);
        Assert.Equal("entry correction", fixture.Executor.LastExecution.Command.CorrectionReasonText);

        Assert.NotNull(fixture.Hasher.LastPayloadJson);
        using (var canonical = JsonDocument.Parse(fixture.Hasher.LastPayloadJson))
        {
            Assert.Equal(
                FinanceEndpoints.CorrectPayableAdjustmentCommandType,
                canonical.RootElement.GetProperty("commandType").GetString());
            var command = canonical.RootElement.GetProperty("command");
            Assert.Equal(payableId, command.GetProperty("payableId").GetGuid());
            Assert.Equal(adjustmentId, command.GetProperty("payableAdjustmentId").GetGuid());
            Assert.Equal(-1000, command.GetProperty("correctedAmountDeltaThb").GetInt64());
            Assert.Equal("corrected reason", command.GetProperty("correctedReasonText").GetString());
            Assert.Equal(2, command.GetProperty("expectedOutstandingVersion").GetInt64());
            Assert.Equal("entry correction", command.GetProperty("correctionReasonText").GetString());
        }

        using var body = JsonDocument.Parse(response.Body);
        Assert.Equal(adjustmentId, body.RootElement.GetProperty("payableAdjustmentId").GetGuid());
        Assert.Equal(-2000, body.RootElement.GetProperty("previousAmountDeltaThb").GetInt64());
        Assert.Equal(-1000, body.RootElement.GetProperty("correctedAmountDeltaThb").GetInt64());
        Assert.Equal("original reason", body.RootElement.GetProperty("previousReasonText").GetString());
        Assert.Equal("corrected reason", body.RootElement.GetProperty("correctedReasonText").GetString());
        Assert.Equal(9000, body.RootElement.GetProperty("outstandingThb").GetInt64());
        Assert.Equal(3, body.RootElement.GetProperty("outstandingVersion").GetInt64());
    }

    [Fact]
    public async Task Payable_adjustment_correction_maps_transport_semantic_and_current_state_failures()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapFinanceEndpoints();

        var invalid = await InvokeAsync(
            app,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"correctedAmountDeltaThb":-1000,"correctedReasonText":null,"expectedOutstandingVersion":0,"correctionReasonText":null}""");
        Assert.Equal(StatusCodes.Status400BadRequest, invalid.StatusCode);
        Assert.Equal(0, fixture.Executor.InvocationCount);
        AssertProblemCode(invalid.Body, ApiErrorCodes.RequestValidationFailed);

        fixture.Executor.Result = ApplicationResult<CorrectPayableAdjustmentResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Validation,
                FinanceApplicationErrorCodes.AdjustmentCorrectionNoChange));
        var noChange = await InvokeAsync(
            app,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"correctedAmountDeltaThb":-1000,"correctedReasonText":null,"expectedOutstandingVersion":2,"correctionReasonText":null}""");
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, noChange.StatusCode);
        AssertProblemCode(noChange.Body, FinanceApplicationErrorCodes.AdjustmentCorrectionNoChange);

        fixture.Executor.Result = ApplicationResult<CorrectPayableAdjustmentResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Conflict,
                FinanceApplicationErrorCodes.OutstandingChanged));
        var conflict = await InvokeAsync(
            app,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"correctedAmountDeltaThb":-1000,"correctedReasonText":null,"expectedOutstandingVersion":2,"correctionReasonText":null}""");
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        AssertProblemCode(conflict.Body, FinanceApplicationErrorCodes.OutstandingChanged);
    }

    private static void AssertProblemCode(string body, string expectedCode)
    {
        using var document = JsonDocument.Parse(body);
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
        Assert.Equal($"urn:yowthi:error:{expectedCode}", document.RootElement.GetProperty("type").GetString());
    }

    private static async Task<HttpInvocationResult> InvokeAsync(
        WebApplication app,
        Guid payableId,
        Guid adjustmentId,
        Guid commandId,
        string body)
    {
        var endpoint = GetEndpoint(app);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        await using var requestBody = new MemoryStream(bodyBytes);
        await using var responseBody = new MemoryStream();

        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new RequestBodyDetectionFeature());
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = endpoint.RoutePattern.RawText!
            .Replace("{payableId:guid}", payableId.ToString(), StringComparison.Ordinal)
            .Replace("{adjustmentId:guid}", adjustmentId.ToString(), StringComparison.Ordinal);
        context.Request.RouteValues["payableId"] = payableId.ToString();
        context.Request.RouteValues["adjustmentId"] = adjustmentId.ToString();
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = bodyBytes.Length;
        context.Request.Body = requestBody;
        context.Request.Headers[IdempotencyKeyEndpointFilter.HeaderName] = commandId.ToString();
        context.Response.Body = responseBody;
        context.SetEndpoint(endpoint);

        Assert.NotNull(endpoint.RequestDelegate);
        await endpoint.RequestDelegate(context);

        responseBody.Position = 0;
        using var reader = new StreamReader(responseBody, Encoding.UTF8, leaveOpen: true);
        return new HttpInvocationResult(context.Response.StatusCode, await reader.ReadToEndAsync());
    }

    private static RouteEndpoint GetEndpoint(WebApplication app) =>
        Assert.Single(
            ((IEndpointRouteBuilder)app).DataSources
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>(),
            endpoint => endpoint.RoutePattern.RawText == Route);

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

        var executor = new StubCorrectPayableAdjustmentExecutor();
        var hasher = new CapturingRequestHasher();
        builder.Services.AddSingleton<IActorContext>(
            new StubActorContext(ActorAccountId.From(Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339d0"))));
        builder.Services.AddSingleton<ICommandRequestHasher>(hasher);
        builder.Services.AddSingleton<ICorrectPayableAdjustmentExecutor>(executor);

        var app = builder.Build();
        app.UseYowThiApiInfrastructure();
        app.MapYowThiTechnicalEndpoints();
        return new AppFixture(app, executor, hasher);
    }

    private sealed record AppFixture(
        WebApplication App,
        StubCorrectPayableAdjustmentExecutor Executor,
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
                Enumerable.Repeat((byte)0x7C, CommandRequestHash.Sha256Length).ToArray());
        }
    }

    private sealed class StubCorrectPayableAdjustmentExecutor : ICorrectPayableAdjustmentExecutor
    {
        public ApplicationResult<CorrectPayableAdjustmentResult> Result { get; set; } =
            ApplicationResult<CorrectPayableAdjustmentResult>.Success(
                new CorrectPayableAdjustmentResult(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    -2,
                    -1,
                    null,
                    null,
                    1,
                    2,
                    DateTimeOffset.UnixEpoch));

        public CorrectPayableAdjustmentExecution? LastExecution { get; private set; }
        public int InvocationCount { get; private set; }

        public ValueTask<ApplicationResult<CorrectPayableAdjustmentResult>> ExecuteAsync(
            CorrectPayableAdjustmentExecution execution,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            LastExecution = execution;
            return ValueTask.FromResult(Result);
        }
    }
}
