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
using YowThi.Erp.Api.DataProtection;
using YowThi.Erp.Api.Hosting;
using YowThi.Erp.Api.Idempotency;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Serialization;
using YowThi.Erp.Application.DataProtection;

namespace YowThi.Erp.Api.ContractTests;

public sealed class OutsourcedVendorHardDeleteEndpointContractTests
{
    private const string Route = "/api/v1/data-protection/outsourced-vendors/{outsourcedVendorId:guid}/hard-delete";

    [Fact]
    public async Task Outsourced_vendor_hard_delete_is_target_specific_authorized_idempotent_v1_post()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapDataProtectionEndpoints();

        var endpoint = GetEndpoint(app);
        Assert.Contains(HttpMethods.Post, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        Assert.Equal(
            DataProtectionEndpoints.HardDeleteOutsourcedVendorOperationId,
            endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            metadata => metadata.Policy == CapabilityPolicies.DataProtectionHardDelete);
        Assert.NotNull(endpoint.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());
        Assert.NotNull(endpoint.Metadata.GetMetadata<RequiresDeletionReauthenticationMetadata>());

        var statuses = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
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
    public async Task Outsourced_vendor_hard_delete_canonical_command_includes_route_id_and_expected_row_version()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapDataProtectionEndpoints();
        var vendorId = Guid.NewGuid();
        fixture.Executor.Result = ApplicationResult<HardDeleteOutsourcedVendorResult>.Success(
            new HardDeleteOutsourcedVendorResult(vendorId));

        var response = await InvokeAsync(app, GetEndpoint(app), vendorId, Guid.NewGuid(), """{"expectedRowVersion":7}""");
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(vendorId, fixture.Executor.LastExecution!.Command.OutsourcedVendorId);
        Assert.Equal(7, fixture.Executor.LastExecution.Command.ExpectedRowVersion);

        Assert.NotNull(fixture.Hasher.LastPayloadJson);
        using var canonical = JsonDocument.Parse(fixture.Hasher.LastPayloadJson);
        Assert.Equal(
            DataProtectionEndpoints.HardDeleteOutsourcedVendorCommandType,
            canonical.RootElement.GetProperty("commandType").GetString());
        var command = canonical.RootElement.GetProperty("command");
        Assert.Equal(vendorId, command.GetProperty("outsourcedVendorId").GetGuid());
        Assert.Equal(7, command.GetProperty("expectedRowVersion").GetInt64());
    }

    [Fact]
    public async Task Outsourced_vendor_hard_delete_maps_dependency_block_and_transport_validation()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapDataProtectionEndpoints();
        var endpoint = GetEndpoint(app);

        fixture.Executor.Result = ApplicationResult<HardDeleteOutsourcedVendorResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Conflict,
                OutsourcedVendorHardDeleteErrorCodes.DependencyBlocked));
        var blocked = await InvokeAsync(app, endpoint, Guid.NewGuid(), Guid.NewGuid(), """{"expectedRowVersion":1}""");
        Assert.Equal(StatusCodes.Status409Conflict, blocked.StatusCode);
        AssertProblemCode(blocked.Body, OutsourcedVendorHardDeleteErrorCodes.DependencyBlocked);

        var invalid = await InvokeAsync(app, endpoint, Guid.NewGuid(), Guid.NewGuid(), """{"expectedRowVersion":0}""");
        Assert.Equal(StatusCodes.Status400BadRequest, invalid.StatusCode);
        AssertProblemCode(invalid.Body, "request.validation-failed");
    }

    private static void AssertProblemCode(string body, string expectedCode)
    {
        using var document = JsonDocument.Parse(body);
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
    }

    private static async Task<HttpInvocationResult> InvokeAsync(
        WebApplication app,
        RouteEndpoint endpoint,
        Guid outsourcedVendorId,
        Guid commandId,
        string body)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        await using var requestBody = new MemoryStream(bodyBytes);
        await using var responseBody = new MemoryStream();

        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [DeletionReauthenticationClaims.CreateClaim(DateTimeOffset.UtcNow)],
                "contract-test"));
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new RequestBodyDetectionFeature());
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = endpoint.RoutePattern.RawText!
            .Replace("{outsourcedVendorId:guid}", outsourcedVendorId.ToString(), StringComparison.Ordinal);
        context.Request.RouteValues["outsourcedVendorId"] = outsourcedVendorId.ToString();
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
            ((IEndpointRouteBuilder)app).DataSources.SelectMany(x => x.Endpoints).OfType<RouteEndpoint>(),
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

        var executor = new StubExecutor();
        var hasher = new CapturingRequestHasher();
        builder.Services.AddSingleton<IActorContext>(
            new StubActorContext(ActorAccountId.From(Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339c0"))));
        builder.Services.AddSingleton<ICommandRequestHasher>(hasher);
        builder.Services.AddSingleton<IHardDeleteOutsourcedVendorExecutor>(executor);

        var app = builder.Build();
        app.UseYowThiApiInfrastructure();
        app.MapYowThiTechnicalEndpoints();
        return new AppFixture(app, executor, hasher);
    }

    private sealed record AppFixture(WebApplication App, StubExecutor Executor, CapturingRequestHasher Hasher);
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
                Enumerable.Repeat((byte)0x6D, CommandRequestHash.Sha256Length).ToArray());
        }
    }

    private sealed class StubExecutor : IHardDeleteOutsourcedVendorExecutor
    {
        public ApplicationResult<HardDeleteOutsourcedVendorResult> Result { get; set; } =
            ApplicationResult<HardDeleteOutsourcedVendorResult>.Success(
                new HardDeleteOutsourcedVendorResult(Guid.NewGuid()));
        public HardDeleteOutsourcedVendorExecution? LastExecution { get; private set; }

        public ValueTask<ApplicationResult<HardDeleteOutsourcedVendorResult>> ExecuteAsync(
            HardDeleteOutsourcedVendorExecution execution,
            CancellationToken cancellationToken)
        {
            LastExecution = execution;
            return ValueTask.FromResult(Result);
        }
    }
}
