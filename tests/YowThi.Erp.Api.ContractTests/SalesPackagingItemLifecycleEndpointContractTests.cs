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
using YowThi.Erp.Api.SalesHandling;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Serialization;
using YowThi.Erp.Application.SalesHandling;

namespace YowThi.Erp.Api.ContractTests;

public sealed class SalesPackagingItemLifecycleEndpointContractTests
{
    private const string SoftDeleteRoute = "/api/v1/sales-handling/packaging-items/{salesPackagingItemId:guid}/soft-delete";
    private const string RestoreRoute = "/api/v1/sales-handling/packaging-items/{salesPackagingItemId:guid}/restore";

    [Fact]
    public async Task Packaging_item_lifecycle_routes_are_target_specific_authorized_idempotent_v1_posts()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapSalesPackagingItemLifecycleEndpoints();

        AssertLifecycleEndpoint(GetEndpoint(app, SoftDeleteRoute), SalesPackagingItemLifecycleEndpoints.SoftDeleteOperationId);
        AssertLifecycleEndpoint(GetEndpoint(app, RestoreRoute), SalesPackagingItemLifecycleEndpoints.RestoreOperationId);
    }

    [Fact]
    public async Task Packaging_item_lifecycle_canonical_commands_include_route_id_and_expected_row_version()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapSalesPackagingItemLifecycleEndpoints();
        var itemId = Guid.NewGuid();

        fixture.Executor.SoftDeleteResult = ApplicationResult<SalesPackagingItemLifecycleResult>.Success(
            new SalesPackagingItemLifecycleResult(itemId, 8, true));
        var softDelete = await InvokeAsync(
            app,
            GetEndpoint(app, SoftDeleteRoute),
            itemId,
            Guid.NewGuid(),
            """{"expectedRowVersion":7}""");
        Assert.Equal(StatusCodes.Status200OK, softDelete.StatusCode);
        Assert.Equal(itemId, fixture.Executor.LastSoftDeleteExecution!.Command.SalesPackagingItemId);
        Assert.Equal(7, fixture.Executor.LastSoftDeleteExecution.Command.ExpectedRowVersion);
        AssertCanonical(fixture.Hasher.LastPayloadJson, SalesPackagingItemLifecycleEndpoints.SoftDeleteCommandType, itemId, 7);

        fixture.Executor.RestoreResult = ApplicationResult<SalesPackagingItemLifecycleResult>.Success(
            new SalesPackagingItemLifecycleResult(itemId, 9, false));
        var restore = await InvokeAsync(
            app,
            GetEndpoint(app, RestoreRoute),
            itemId,
            Guid.NewGuid(),
            """{"expectedRowVersion":8}""");
        Assert.Equal(StatusCodes.Status200OK, restore.StatusCode);
        Assert.Equal(itemId, fixture.Executor.LastRestoreExecution!.Command.SalesPackagingItemId);
        Assert.Equal(8, fixture.Executor.LastRestoreExecution.Command.ExpectedRowVersion);
        AssertCanonical(fixture.Hasher.LastPayloadJson, SalesPackagingItemLifecycleEndpoints.RestoreCommandType, itemId, 8);
    }

    [Fact]
    public async Task Packaging_item_lifecycle_maps_conflicts_and_does_not_reuse_work_record_or_party_policies()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapSalesPackagingItemLifecycleEndpoints();

        fixture.Executor.SoftDeleteResult = ApplicationResult<SalesPackagingItemLifecycleResult>.Failure(
            ApplicationError.Create(ApplicationErrorKind.Conflict, SalesPackagingItemLifecycleErrorCodes.AlreadyDeleted));
        var conflict = await InvokeAsync(
            app,
            GetEndpoint(app, SoftDeleteRoute),
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"expectedRowVersion":1}""");
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        AssertProblemCode(conflict.Body, SalesPackagingItemLifecycleErrorCodes.AlreadyDeleted);

        var invalid = await InvokeAsync(
            app,
            GetEndpoint(app, RestoreRoute),
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"expectedRowVersion":0}""");
        Assert.Equal(StatusCodes.Status400BadRequest, invalid.StatusCode);
        AssertProblemCode(invalid.Body, "request.validation-failed");

        var policies = GetEndpoint(app, SoftDeleteRoute).Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(x => x.Policy)
            .ToArray();
        Assert.Contains(CapabilityPolicies.SalesPackagingItemLifecycle, policies);
        Assert.DoesNotContain(CapabilityPolicies.SalesHandlingWorkRecord, policies);
        Assert.DoesNotContain(CapabilityPolicies.EmployeeLifecycle, policies);
        Assert.DoesNotContain(CapabilityPolicies.DataProtectionHardDelete, policies);
    }

    private static void AssertLifecycleEndpoint(RouteEndpoint endpoint, string operationId)
    {
        Assert.Contains(HttpMethods.Post, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        Assert.Equal(operationId, endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            metadata => metadata.Policy == CapabilityPolicies.SalesPackagingItemLifecycle);
        Assert.NotNull(endpoint.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());
    }

    private static void AssertCanonical(string? payload, string commandType, Guid itemId, long expectedRowVersion)
    {
        Assert.NotNull(payload);
        using var document = JsonDocument.Parse(payload);
        Assert.Equal(commandType, document.RootElement.GetProperty("commandType").GetString());
        var command = document.RootElement.GetProperty("command");
        Assert.Equal(itemId, command.GetProperty("salesPackagingItemId").GetGuid());
        Assert.Equal(expectedRowVersion, command.GetProperty("expectedRowVersion").GetInt64());
    }

    private static void AssertProblemCode(string body, string expectedCode)
    {
        using var document = JsonDocument.Parse(body);
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
    }

    private static async Task<HttpInvocationResult> InvokeAsync(
        WebApplication app,
        RouteEndpoint endpoint,
        Guid itemId,
        Guid commandId,
        string body)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        await using var requestBody = new MemoryStream(bodyBytes);
        await using var responseBody = new MemoryStream();

        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new RequestBodyDetectionFeature());
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = endpoint.RoutePattern.RawText!
            .Replace("{salesPackagingItemId:guid}", itemId.ToString(), StringComparison.Ordinal);
        context.Request.RouteValues["salesPackagingItemId"] = itemId.ToString();
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

    private static RouteEndpoint GetEndpoint(WebApplication app, string route) =>
        Assert.Single(
            ((IEndpointRouteBuilder)app).DataSources
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>(),
            endpoint => endpoint.RoutePattern.RawText == route);

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
        builder.Services.AddSingleton<ISalesPackagingItemLifecycleExecutor>(executor);

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
                Enumerable.Repeat((byte)0x6A, CommandRequestHash.Sha256Length).ToArray());
        }
    }

    private sealed class StubExecutor : ISalesPackagingItemLifecycleExecutor
    {
        public ApplicationResult<SalesPackagingItemLifecycleResult> SoftDeleteResult { get; set; } =
            ApplicationResult<SalesPackagingItemLifecycleResult>.Success(
                new SalesPackagingItemLifecycleResult(Guid.NewGuid(), 2, true));
        public ApplicationResult<SalesPackagingItemLifecycleResult> RestoreResult { get; set; } =
            ApplicationResult<SalesPackagingItemLifecycleResult>.Success(
                new SalesPackagingItemLifecycleResult(Guid.NewGuid(), 3, false));
        public SoftDeleteSalesPackagingItemExecution? LastSoftDeleteExecution { get; private set; }
        public RestoreSalesPackagingItemExecution? LastRestoreExecution { get; private set; }

        public ValueTask<ApplicationResult<SalesPackagingItemLifecycleResult>> SoftDeleteAsync(
            SoftDeleteSalesPackagingItemExecution execution,
            CancellationToken cancellationToken)
        {
            LastSoftDeleteExecution = execution;
            return ValueTask.FromResult(SoftDeleteResult);
        }

        public ValueTask<ApplicationResult<SalesPackagingItemLifecycleResult>> RestoreAsync(
            RestoreSalesPackagingItemExecution execution,
            CancellationToken cancellationToken)
        {
            LastRestoreExecution = execution;
            return ValueTask.FromResult(RestoreResult);
        }
    }
}
