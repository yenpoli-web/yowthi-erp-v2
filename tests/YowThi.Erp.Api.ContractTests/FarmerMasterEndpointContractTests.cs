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
using YowThi.Erp.Api.Party;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Serialization;
using YowThi.Erp.Application.Party;

namespace YowThi.Erp.Api.ContractTests;

public sealed class FarmerMasterEndpointContractTests
{
    private const string ListRoute = "/api/v1/party/farmers";
    private const string UpdateRoute = "/api/v1/party/farmers/{farmerId:guid}/update";

    [Fact]
    public async Task Farmer_master_routes_are_target_specific_authorized_and_writes_are_idempotent()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapFarmerMasterEndpoints();

        var list = GetEndpoint(app, ListRoute, HttpMethods.Get);
        Assert.Equal(FarmerMasterEndpoints.ListOperationId, list.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        AssertPolicy(list, CapabilityPolicies.FarmerLifecycle);
        Assert.Null(list.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());

        var create = GetEndpoint(app, ListRoute, HttpMethods.Post);
        Assert.Equal(FarmerMasterEndpoints.CreateOperationId, create.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        AssertPolicy(create, CapabilityPolicies.FarmerLifecycle);
        Assert.NotNull(create.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());

        var update = GetEndpoint(app, UpdateRoute, HttpMethods.Post);
        Assert.Equal(FarmerMasterEndpoints.UpdateOperationId, update.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        AssertPolicy(update, CapabilityPolicies.FarmerLifecycle);
        Assert.NotNull(update.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());
    }

    [Fact]
    public async Task Farmer_create_canonical_command_normalizes_text_and_uses_server_actor()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapFarmerMasterEndpoints();
        var farmerId = Guid.CreateVersion7();
        fixture.Executor.CreateResult = ApplicationResult<FarmerMasterWriteResult>.Success(new(farmerId, 1));

        var response = await InvokeAsync(
            app,
            GetEndpoint(app, ListRoute, HttpMethods.Post),
            routeFarmerId: null,
            Guid.CreateVersion7(),
            """{"nameZhTw":"  清邁農戶  ","nameThTh":null,"bankName":"  KBANK ","bankAccount":" 123 ","phone":" 081 ","address":"  Chiang Mai ","active":true}""");

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        var execution = Assert.IsType<CreateFarmerExecution>(fixture.Executor.LastCreateExecution);
        Assert.Equal("清邁農戶", execution.Command.NameZhTw);
        Assert.Equal("KBANK", execution.Command.BankName);
        Assert.Equal("123", execution.Command.BankAccount);
        Assert.Equal("081", execution.Command.Phone);
        Assert.Equal("Chiang Mai", execution.Command.Address);
        Assert.Equal(fixture.Actor.ActorAccountId, execution.ActorAccountId);

        using var canonical = JsonDocument.Parse(Assert.IsType<string>(fixture.Hasher.LastPayloadJson));
        Assert.Equal(FarmerMasterEndpoints.CreateCommandType, canonical.RootElement.GetProperty("commandType").GetString());
        Assert.Equal("清邁農戶", canonical.RootElement.GetProperty("command").GetProperty("nameZhTw").GetString());
    }

    [Fact]
    public async Task Farmer_update_canonical_command_contains_route_id_and_expected_row_version()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapFarmerMasterEndpoints();
        var farmerId = Guid.CreateVersion7();
        fixture.Executor.UpdateResult = ApplicationResult<FarmerMasterWriteResult>.Success(new(farmerId, 8));

        var response = await InvokeAsync(
            app,
            GetEndpoint(app, UpdateRoute, HttpMethods.Post),
            farmerId,
            Guid.CreateVersion7(),
            """{"expectedRowVersion":7,"nameZhTw":null,"nameThTh":" เกษตรกร ","bankName":null,"bankAccount":null,"phone":null,"address":null,"active":false}""");

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        var execution = Assert.IsType<UpdateFarmerExecution>(fixture.Executor.LastUpdateExecution);
        Assert.Equal(farmerId, execution.Command.FarmerId);
        Assert.Equal(7, execution.Command.ExpectedRowVersion);
        Assert.Equal("เกษตรกร", execution.Command.NameThTh);
        Assert.False(execution.Command.Active);

        using var canonical = JsonDocument.Parse(Assert.IsType<string>(fixture.Hasher.LastPayloadJson));
        var command = canonical.RootElement.GetProperty("command");
        Assert.Equal(farmerId, command.GetProperty("farmerId").GetGuid());
        Assert.Equal(7, command.GetProperty("expectedRowVersion").GetInt64());
    }

    [Fact]
    public async Task Farmer_master_maps_semantic_and_concurrency_failures()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapFarmerMasterEndpoints();
        var farmerId = Guid.CreateVersion7();

        fixture.Executor.CreateResult = ApplicationResult<FarmerMasterWriteResult>.Failure(
            ApplicationError.Create(ApplicationErrorKind.Validation, FarmerMasterErrorCodes.InvalidInput));
        var invalidCreate = await InvokeAsync(
            app,
            GetEndpoint(app, ListRoute, HttpMethods.Post),
            null,
            Guid.CreateVersion7(),
            """{"nameZhTw":null,"nameThTh":null,"bankName":null,"bankAccount":null,"phone":"081","address":null,"active":true}""");
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, invalidCreate.StatusCode);
        AssertProblemCode(invalidCreate.Body, FarmerMasterErrorCodes.InvalidInput);

        fixture.Executor.UpdateResult = ApplicationResult<FarmerMasterWriteResult>.Failure(
            ApplicationError.Create(ApplicationErrorKind.Conflict, FarmerMasterErrorCodes.StaleRowVersion));
        var stale = await InvokeAsync(
            app,
            GetEndpoint(app, UpdateRoute, HttpMethods.Post),
            farmerId,
            Guid.CreateVersion7(),
            """{"expectedRowVersion":2,"nameZhTw":"農戶","nameThTh":null,"bankName":null,"bankAccount":null,"phone":null,"address":null,"active":true}""");
        Assert.Equal(StatusCodes.Status409Conflict, stale.StatusCode);
        AssertProblemCode(stale.Body, FarmerMasterErrorCodes.StaleRowVersion);

        var transportInvalid = await InvokeAsync(
            app,
            GetEndpoint(app, UpdateRoute, HttpMethods.Post),
            farmerId,
            Guid.CreateVersion7(),
            """{"expectedRowVersion":0,"nameZhTw":"農戶","nameThTh":null,"bankName":null,"bankAccount":null,"phone":null,"address":null,"active":true}""");
        Assert.Equal(StatusCodes.Status400BadRequest, transportInvalid.StatusCode);
        AssertProblemCode(transportInvalid.Body, "request.validation-failed");
    }

    private static void AssertPolicy(RouteEndpoint endpoint, string policy) =>
        Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(), metadata => metadata.Policy == policy);

    private static void AssertProblemCode(string body, string expectedCode)
    {
        using var document = JsonDocument.Parse(body);
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
    }

    private static async Task<HttpInvocationResult> InvokeAsync(
        WebApplication app,
        RouteEndpoint endpoint,
        Guid? routeFarmerId,
        Guid commandId,
        string body)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        await using var requestBody = new MemoryStream(bodyBytes);
        await using var responseBody = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new RequestBodyDetectionFeature());
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = endpoint.RoutePattern.RawText!;
        if (routeFarmerId.HasValue)
        {
            context.Request.Path = context.Request.Path.Value!.Replace("{farmerId:guid}", routeFarmerId.Value.ToString(), StringComparison.Ordinal);
            context.Request.RouteValues["farmerId"] = routeFarmerId.Value.ToString();
        }
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
        return new(context.Response.StatusCode, await reader.ReadToEndAsync());
    }

    private static RouteEndpoint GetEndpoint(WebApplication app, string route, string method) =>
        Assert.Single(
            ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>(),
            endpoint => endpoint.RoutePattern.RawText == route
                && (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(method) ?? false));

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

        var executor = new StubFarmerMasterExecutor();
        var reader = new StubFarmerMasterReader();
        var hasher = new CapturingRequestHasher();
        var actor = new StubActorContext(ActorAccountId.From(Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339b0")));
        builder.Services.AddSingleton<IActorContext>(actor);
        builder.Services.AddSingleton<ICommandRequestHasher>(hasher);
        builder.Services.AddSingleton<IFarmerMasterExecutor>(executor);
        builder.Services.AddSingleton<IFarmerMasterReader>(reader);

        var app = builder.Build();
        app.UseYowThiApiInfrastructure();
        app.MapYowThiTechnicalEndpoints();
        return new(app, executor, reader, hasher, actor);
    }

    private sealed record AppFixture(
        WebApplication App,
        StubFarmerMasterExecutor Executor,
        StubFarmerMasterReader Reader,
        CapturingRequestHasher Hasher,
        StubActorContext Actor);
    private sealed record HttpInvocationResult(int StatusCode, string Body);
    private sealed class RequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature { public bool CanHaveBody => true; }
    private sealed class StubActorContext(ActorAccountId actorAccountId) : IActorContext { public ActorAccountId ActorAccountId { get; } = actorAccountId; }

    private sealed class CapturingRequestHasher : ICommandRequestHasher
    {
        public string? LastPayloadJson { get; private set; }
        public CommandRequestHash Compute(JsonPayload canonicalCommandPayload)
        {
            LastPayloadJson = Encoding.UTF8.GetString(canonicalCommandPayload.Utf8Json.Span);
            return CommandRequestHash.FromSha256(Enumerable.Repeat((byte)0x73, CommandRequestHash.Sha256Length).ToArray());
        }
    }

    private sealed class StubFarmerMasterExecutor : IFarmerMasterExecutor
    {
        public ApplicationResult<FarmerMasterWriteResult> CreateResult { get; set; } = ApplicationResult<FarmerMasterWriteResult>.Success(new(Guid.CreateVersion7(), 1));
        public ApplicationResult<FarmerMasterWriteResult> UpdateResult { get; set; } = ApplicationResult<FarmerMasterWriteResult>.Success(new(Guid.CreateVersion7(), 2));
        public CreateFarmerExecution? LastCreateExecution { get; private set; }
        public UpdateFarmerExecution? LastUpdateExecution { get; private set; }
        public ValueTask<ApplicationResult<FarmerMasterWriteResult>> CreateAsync(CreateFarmerExecution execution, CancellationToken cancellationToken) { LastCreateExecution = execution; return ValueTask.FromResult(CreateResult); }
        public ValueTask<ApplicationResult<FarmerMasterWriteResult>> UpdateAsync(UpdateFarmerExecution execution, CancellationToken cancellationToken) { LastUpdateExecution = execution; return ValueTask.FromResult(UpdateResult); }
    }

    private sealed class StubFarmerMasterReader : IFarmerMasterReader
    {
        public ValueTask<FarmerMasterPage> GetAsync(FarmerMasterQuery query, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new FarmerMasterPage([], null));
    }
}
