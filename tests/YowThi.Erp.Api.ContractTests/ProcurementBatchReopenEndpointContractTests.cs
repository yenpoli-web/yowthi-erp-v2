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
using YowThi.Erp.Api.Hosting;
using YowThi.Erp.Api.Idempotency;
using YowThi.Erp.Api.Procurement;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Serialization;
using YowThi.Erp.Application.Procurement;

namespace YowThi.Erp.Api.ContractTests;

public sealed class ProcurementBatchReopenEndpointContractTests
{
    private const string Route = "/api/v1/procurement/batches/{procurementBatchId:guid}/reopen";

    [Fact]
    public async Task Reopen_route_is_target_specific_lifecycle_authorized_idempotent_v1_post()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapProcurementEndpoints();

        var endpoint = GetEndpoint(app);
        Assert.Contains(HttpMethods.Post, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        Assert.Equal(
            ProcurementEndpoints.ReopenBatchOperationId,
            endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            metadata => metadata.Policy == CapabilityPolicies.ProcurementBatchLifecycle);
        Assert.DoesNotContain(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            metadata => metadata.Policy is CapabilityPolicies.ProcurementConfirm or CapabilityPolicies.DataProtectionHardDelete);
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
    public async Task Reopen_canonical_command_contains_route_batch_id_expected_version_and_reason()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapProcurementEndpoints();

        var batchId = Guid.NewGuid();
        fixture.Executor.Result = ApplicationResult<ReopenProcurementBatchResult>.Success(
            new ReopenProcurementBatchResult(batchId, 8, DateTimeOffset.UtcNow));

        var response = await InvokeAsync(
            app,
            batchId,
            Guid.NewGuid(),
            """{"expectedRowVersion":7,"reasonText":"operator reopen"}""");

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.NotNull(fixture.Executor.LastExecution);
        Assert.Equal(batchId, fixture.Executor.LastExecution.Command.ProcurementBatchId);
        Assert.Equal(7, fixture.Executor.LastExecution.Command.ExpectedRowVersion);
        Assert.Equal("operator reopen", fixture.Executor.LastExecution.Command.ReasonText);

        Assert.NotNull(fixture.Hasher.LastPayloadJson);
        using (var canonical = JsonDocument.Parse(fixture.Hasher.LastPayloadJson))
        {
            Assert.Equal(
                ProcurementEndpoints.ReopenBatchCommandType,
                canonical.RootElement.GetProperty("commandType").GetString());
            var command = canonical.RootElement.GetProperty("command");
            Assert.Equal(batchId, command.GetProperty("procurementBatchId").GetGuid());
            Assert.Equal(7, command.GetProperty("expectedRowVersion").GetInt64());
            Assert.Equal("operator reopen", command.GetProperty("reasonText").GetString());
        }

        using var body = JsonDocument.Parse(response.Body);
        Assert.Equal(batchId, body.RootElement.GetProperty("procurementBatchId").GetGuid());
        Assert.Equal(8, body.RootElement.GetProperty("reopenedRowVersion").GetInt64());
    }

    [Fact]
    public async Task Reopen_maps_semantic_and_current_state_failures()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapProcurementEndpoints();

        fixture.Executor.Result = ApplicationResult<ReopenProcurementBatchResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Validation,
                ProcurementBatchReopenErrorCodes.InvalidInput));
        var invalid = await InvokeAsync(
            app,
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"expectedRowVersion":0,"reasonText":null}""");
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, invalid.StatusCode);
        AssertProblemCode(invalid.Body, ProcurementBatchReopenErrorCodes.InvalidInput);

        fixture.Executor.Result = ApplicationResult<ReopenProcurementBatchResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Conflict,
                ProcurementBatchReopenErrorCodes.BatchNotClosed));
        var stateConflict = await InvokeAsync(
            app,
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"expectedRowVersion":2,"reasonText":null}""");
        Assert.Equal(StatusCodes.Status409Conflict, stateConflict.StatusCode);
        AssertProblemCode(stateConflict.Body, ProcurementBatchReopenErrorCodes.BatchNotClosed);

        fixture.Executor.Result = ApplicationResult<ReopenProcurementBatchResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Conflict,
                ProcurementBatchReopenErrorCodes.ConcurrentChange));
        var stale = await InvokeAsync(
            app,
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"expectedRowVersion":2,"reasonText":null}""");
        Assert.Equal(StatusCodes.Status409Conflict, stale.StatusCode);
        AssertProblemCode(stale.Body, ProcurementBatchReopenErrorCodes.ConcurrentChange);
    }

    private static void AssertProblemCode(string body, string expectedCode)
    {
        using var document = JsonDocument.Parse(body);
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
        Assert.Equal($"urn:yowthi:error:{expectedCode}", document.RootElement.GetProperty("type").GetString());
    }

    private static async Task<HttpInvocationResult> InvokeAsync(
        WebApplication app,
        Guid batchId,
        Guid commandId,
        string body)
    {
        var endpoint = GetEndpoint(app);
        var bytes = Encoding.UTF8.GetBytes(body);
        await using var requestBody = new MemoryStream(bytes);
        await using var responseBody = new MemoryStream();

        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new RequestBodyDetectionFeature());
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = endpoint.RoutePattern.RawText!
            .Replace("{procurementBatchId:guid}", batchId.ToString(), StringComparison.Ordinal);
        context.Request.RouteValues["procurementBatchId"] = batchId.ToString();
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = bytes.Length;
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

        var executor = new StubReopenExecutor();
        var hasher = new CapturingRequestHasher();
        builder.Services.AddSingleton<IActorContext>(
            new StubActorContext(ActorAccountId.From(Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339d0"))));
        builder.Services.AddSingleton<ICommandRequestHasher>(hasher);
        builder.Services.AddSingleton<IReopenProcurementBatchExecutor>(executor);

        var app = builder.Build();
        app.UseYowThiApiInfrastructure();
        app.MapYowThiTechnicalEndpoints();
        return new AppFixture(app, executor, hasher);
    }

    private sealed record AppFixture(
        WebApplication App,
        StubReopenExecutor Executor,
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
                Enumerable.Repeat((byte)0x5D, CommandRequestHash.Sha256Length).ToArray());
        }
    }

    private sealed class StubReopenExecutor : IReopenProcurementBatchExecutor
    {
        public ApplicationResult<ReopenProcurementBatchResult> Result { get; set; } =
            ApplicationResult<ReopenProcurementBatchResult>.Success(
                new ReopenProcurementBatchResult(Guid.NewGuid(), 2, DateTimeOffset.UnixEpoch));

        public ReopenProcurementBatchExecution? LastExecution { get; private set; }

        public ValueTask<ApplicationResult<ReopenProcurementBatchResult>> ExecuteAsync(
            ReopenProcurementBatchExecution execution,
            CancellationToken cancellationToken)
        {
            LastExecution = execution;
            return ValueTask.FromResult(Result);
        }
    }
}
