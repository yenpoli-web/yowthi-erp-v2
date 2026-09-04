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

public sealed class ReceiptCorrectionEndpointContractTests
{
    private const string Route =
        "/api/v1/finance/receivables/{receivableId:guid}/receipts/{receiptId:guid}/correct-amount";

    [Fact]
    public async Task Receipt_correction_route_is_target_specific_authorized_idempotent_v1_post()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapFinanceEndpoints();

        var endpoint = GetEndpoint(app);
        Assert.Contains(HttpMethods.Post, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        Assert.Equal(
            FinanceEndpoints.CorrectReceiptAmountOperationId,
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
    public async Task Receipt_correction_canonical_command_includes_both_route_ids_amount_version_and_reason()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapFinanceEndpoints();

        var receivableId = Guid.NewGuid();
        var receiptId = Guid.NewGuid();
        fixture.Executor.Result = ApplicationResult<CorrectReceiptAmountResult>.Success(
            new CorrectReceiptAmountResult(
                receiptId,
                receivableId,
                8000,
                5000,
                7000,
                13,
                DateTimeOffset.UtcNow));

        var response = await InvokeAsync(
            app,
            receivableId,
            receiptId,
            Guid.NewGuid(),
            """{"correctedAmountThb":5000,"expectedOutstandingVersion":12,"reasonText":"entry correction"}""");

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.NotNull(fixture.Executor.LastExecution);
        Assert.Equal(receivableId, fixture.Executor.LastExecution.Command.ReceivableId);
        Assert.Equal(receiptId, fixture.Executor.LastExecution.Command.ReceiptId);
        Assert.Equal(5000, fixture.Executor.LastExecution.Command.CorrectedAmountThb);
        Assert.Equal(12, fixture.Executor.LastExecution.Command.ExpectedOutstandingVersion);
        Assert.Equal("entry correction", fixture.Executor.LastExecution.Command.ReasonText);

        Assert.NotNull(fixture.Hasher.LastPayloadJson);
        using (var canonical = JsonDocument.Parse(fixture.Hasher.LastPayloadJson))
        {
            Assert.Equal(
                FinanceEndpoints.CorrectReceiptAmountCommandType,
                canonical.RootElement.GetProperty("commandType").GetString());
            var command = canonical.RootElement.GetProperty("command");
            Assert.Equal(receivableId, command.GetProperty("receivableId").GetGuid());
            Assert.Equal(receiptId, command.GetProperty("receiptId").GetGuid());
            Assert.Equal(5000, command.GetProperty("correctedAmountThb").GetInt64());
            Assert.Equal(12, command.GetProperty("expectedOutstandingVersion").GetInt64());
            Assert.Equal("entry correction", command.GetProperty("reasonText").GetString());
        }

        using var body = JsonDocument.Parse(response.Body);
        Assert.Equal(receiptId, body.RootElement.GetProperty("receiptId").GetGuid());
        Assert.Equal(8000, body.RootElement.GetProperty("previousAmountThb").GetInt64());
        Assert.Equal(5000, body.RootElement.GetProperty("correctedAmountThb").GetInt64());
        Assert.Equal(7000, body.RootElement.GetProperty("outstandingThb").GetInt64());
        Assert.Equal(13, body.RootElement.GetProperty("outstandingVersion").GetInt64());
    }

    [Fact]
    public async Task Receipt_correction_maps_transport_semantic_and_current_state_failures()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapFinanceEndpoints();

        var invalid = await InvokeAsync(
            app,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"correctedAmountThb":5000,"expectedOutstandingVersion":0,"reasonText":null}""");
        Assert.Equal(StatusCodes.Status400BadRequest, invalid.StatusCode);
        Assert.Equal(0, fixture.Executor.InvocationCount);
        AssertProblemCode(invalid.Body, ApiErrorCodes.RequestValidationFailed);

        fixture.Executor.Result = ApplicationResult<CorrectReceiptAmountResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Validation,
                FinanceApplicationErrorCodes.ReceiptCorrectionNoChange));
        var noChange = await InvokeAsync(
            app,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"correctedAmountThb":5000,"expectedOutstandingVersion":2,"reasonText":null}""");
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, noChange.StatusCode);
        AssertProblemCode(noChange.Body, FinanceApplicationErrorCodes.ReceiptCorrectionNoChange);

        fixture.Executor.Result = ApplicationResult<CorrectReceiptAmountResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Conflict,
                FinanceApplicationErrorCodes.OutstandingChanged));
        var conflict = await InvokeAsync(
            app,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"correctedAmountThb":5000,"expectedOutstandingVersion":2,"reasonText":null}""");
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
        Guid receivableId,
        Guid receiptId,
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
            .Replace("{receivableId:guid}", receivableId.ToString(), StringComparison.Ordinal)
            .Replace("{receiptId:guid}", receiptId.ToString(), StringComparison.Ordinal);
        context.Request.RouteValues["receivableId"] = receivableId.ToString();
        context.Request.RouteValues["receiptId"] = receiptId.ToString();
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

        var executor = new StubCorrectReceiptAmountExecutor();
        var hasher = new CapturingRequestHasher();
        builder.Services.AddSingleton<IActorContext>(
            new StubActorContext(ActorAccountId.From(Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339d0"))));
        builder.Services.AddSingleton<ICommandRequestHasher>(hasher);
        builder.Services.AddSingleton<ICorrectReceiptAmountExecutor>(executor);

        var app = builder.Build();
        app.UseYowThiApiInfrastructure();
        app.MapYowThiTechnicalEndpoints();
        return new AppFixture(app, executor, hasher);
    }

    private sealed record AppFixture(
        WebApplication App,
        StubCorrectReceiptAmountExecutor Executor,
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
                Enumerable.Repeat((byte)0x6C, CommandRequestHash.Sha256Length).ToArray());
        }
    }

    private sealed class StubCorrectReceiptAmountExecutor : ICorrectReceiptAmountExecutor
    {
        public ApplicationResult<CorrectReceiptAmountResult> Result { get; set; } =
            ApplicationResult<CorrectReceiptAmountResult>.Success(
                new CorrectReceiptAmountResult(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    2,
                    1,
                    1,
                    2,
                    DateTimeOffset.UnixEpoch));

        public CorrectReceiptAmountExecution? LastExecution { get; private set; }
        public int InvocationCount { get; private set; }

        public ValueTask<ApplicationResult<CorrectReceiptAmountResult>> ExecuteAsync(
            CorrectReceiptAmountExecution execution,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            LastExecution = execution;
            return ValueTask.FromResult(Result);
        }
    }
}
