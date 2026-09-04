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
using YowThi.Erp.Api.Hosting;
using YowThi.Erp.Api.Idempotency;
using YowThi.Erp.Api.Sales;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Serialization;
using YowThi.Erp.Application.Sales;
using YowThi.Erp.Domain.Inventory;

namespace YowThi.Erp.Api.ContractTests;

public sealed class SalesAllocationCorrectionEndpointContractTests
{
    private const string Route = "/api/v1/sales/{salesId:guid}/allocation-revisions";

    [Fact]
    public async Task Allocation_revision_is_target_specific_authorized_idempotent_v1_post()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapSalesEndpoints();

        var endpoint = GetEndpoint(app);
        Assert.Contains(HttpMethods.Post, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        Assert.Equal(
            SalesEndpoints.CorrectAllocationOperationId,
            endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            metadata => metadata.Policy == CapabilityPolicies.SalesCorrectAllocation);
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

    [Fact]
    public async Task Complete_replacement_canonical_command_contains_route_version_mode_and_full_allocation()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapSalesEndpoints();

        var salesId = Guid.NewGuid();
        var detailId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        fixture.Executor.Result = ApplicationResult<CorrectSalesAllocationResult>.Success(
            new CorrectSalesAllocationResult(salesId, 9, revisionId, 1, Guid.NewGuid()));

        var response = await InvokeAsync(
            app,
            GetEndpoint(app),
            salesId,
            Guid.NewGuid(),
            $$"""
            {
              "expectedRowVersion": 8,
              "mode": "COMPLETE_REPLACEMENT",
              "allocations": [
                {
                  "salesDetailId": "{{detailId}}",
                  "origin": "IN_HOUSE",
                  "procurementBatchId": "{{batchId}}",
                  "outsourcedSupplyBatchId": null,
                  "allocatedQuantity": 8
                }
              ]
            }
            """);

        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        var execution = Assert.IsType<CorrectSalesAllocationExecution>(fixture.Executor.LastExecution);
        Assert.Equal(salesId, execution.Command.SalesId);
        Assert.Equal(8, execution.Command.ExpectedRowVersion);
        Assert.Equal(SalesAllocationCorrectionMode.COMPLETE_REPLACEMENT, execution.Command.Mode);
        var allocation = Assert.Single(execution.Command.Allocations);
        Assert.Equal(detailId, allocation.SalesDetailId);
        Assert.Equal(InventoryOrigin.IN_HOUSE, allocation.Origin);
        Assert.Equal(batchId, allocation.ProcurementBatchId);
        Assert.Equal(8m, allocation.AllocatedQuantity);

        Assert.NotNull(fixture.Hasher.LastPayloadJson);
        using var canonical = JsonDocument.Parse(fixture.Hasher.LastPayloadJson);
        Assert.Equal(
            SalesEndpoints.CorrectAllocationCommandType,
            canonical.RootElement.GetProperty("commandType").GetString());
        var command = canonical.RootElement.GetProperty("command");
        Assert.Equal(salesId, command.GetProperty("salesId").GetGuid());
        Assert.Equal(8, command.GetProperty("expectedRowVersion").GetInt64());
        Assert.Equal("COMPLETE_REPLACEMENT", command.GetProperty("mode").GetString());
        Assert.Equal("IN_HOUSE", command.GetProperty("allocations")[0].GetProperty("origin").GetString());

        Assert.Contains(revisionId.ToString(), response.Location, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Override_and_reallocate_mode_is_explicit_and_preserves_override_input()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapSalesEndpoints();

        var salesId = Guid.NewGuid();
        var detailId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        fixture.Executor.Result = ApplicationResult<CorrectSalesAllocationResult>.Success(
            new CorrectSalesAllocationResult(salesId, 4, Guid.NewGuid(), 2, Guid.NewGuid()));

        var response = await InvokeAsync(
            app,
            GetEndpoint(app),
            salesId,
            Guid.NewGuid(),
            $$"""
            {
              "expectedRowVersion": 3,
              "mode": "OVERRIDE_AND_REALLOCATE",
              "allocations": [
                {
                  "salesDetailId": "{{detailId}}",
                  "origin": "OUTSOURCED",
                  "procurementBatchId": null,
                  "outsourcedSupplyBatchId": "{{batchId}}",
                  "allocatedQuantity": 2
                }
              ]
            }
            """);

        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        var execution = Assert.IsType<CorrectSalesAllocationExecution>(fixture.Executor.LastExecution);
        Assert.Equal(SalesAllocationCorrectionMode.OVERRIDE_AND_REALLOCATE, execution.Command.Mode);
        var allocation = Assert.Single(execution.Command.Allocations);
        Assert.Equal(InventoryOrigin.OUTSOURCED, allocation.Origin);
        Assert.Equal(batchId, allocation.OutsourcedSupplyBatchId);
        Assert.Equal(2m, allocation.AllocatedQuantity);
    }

    [Fact]
    public async Task Allocation_revision_maps_transport_and_lifecycle_failures_without_guessing_mode()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapSalesEndpoints();
        var endpoint = GetEndpoint(app);

        var invalid = await InvokeAsync(
            app,
            endpoint,
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"expectedRowVersion":1,"mode":"AUTO","allocations":[]}""");
        Assert.Equal(StatusCodes.Status400BadRequest, invalid.StatusCode);
        AssertProblemCode(invalid.Body, ApiErrorCodes.RequestValidationFailed);

        fixture.Executor.Result = ApplicationResult<CorrectSalesAllocationResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Conflict,
                SalesAllocationCorrectionErrorCodes.LifecycleBlocked));
        var blocked = await InvokeAsync(
            app,
            endpoint,
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"expectedRowVersion":2,"mode":"OVERRIDE_AND_REALLOCATE","allocations":[]}""");
        Assert.Equal(StatusCodes.Status409Conflict, blocked.StatusCode);
        AssertProblemCode(blocked.Body, SalesAllocationCorrectionErrorCodes.LifecycleBlocked);
    }

    private static void AssertProblemCode(string body, string expectedCode)
    {
        using var document = JsonDocument.Parse(body);
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
    }

    private static async Task<HttpInvocationResult> InvokeAsync(
        WebApplication app,
        RouteEndpoint endpoint,
        Guid salesId,
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
            .Replace("{salesId:guid}", salesId.ToString(), StringComparison.Ordinal);
        context.Request.RouteValues["salesId"] = salesId.ToString();
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
        return new HttpInvocationResult(
            context.Response.StatusCode,
            await reader.ReadToEndAsync(),
            context.Response.Headers.Location.ToString());
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

        var executor = new StubCorrectionExecutor();
        var hasher = new CapturingRequestHasher();
        builder.Services.AddSingleton<IActorContext>(
            new StubActorContext(ActorAccountId.From(Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339b0"))));
        builder.Services.AddSingleton<ICommandRequestHasher>(hasher);
        builder.Services.AddSingleton<ICorrectSalesAllocationExecutor>(executor);
        builder.Services.AddSingleton<IConfirmSalesExecutor>(new StubConfirmSalesExecutor());

        var app = builder.Build();
        app.UseYowThiApiInfrastructure();
        app.MapYowThiTechnicalEndpoints();
        return new AppFixture(app, executor, hasher);
    }

    private sealed record AppFixture(
        WebApplication App,
        StubCorrectionExecutor Executor,
        CapturingRequestHasher Hasher);

    private sealed record HttpInvocationResult(int StatusCode, string Body, string Location);

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

    private sealed class StubCorrectionExecutor : ICorrectSalesAllocationExecutor
    {
        public ApplicationResult<CorrectSalesAllocationResult> Result { get; set; } =
            ApplicationResult<CorrectSalesAllocationResult>.Success(
                new CorrectSalesAllocationResult(Guid.NewGuid(), 2, Guid.NewGuid(), 1, Guid.NewGuid()));

        public CorrectSalesAllocationExecution? LastExecution { get; private set; }

        public ValueTask<ApplicationResult<CorrectSalesAllocationResult>> ExecuteAsync(
            CorrectSalesAllocationExecution execution,
            CancellationToken cancellationToken)
        {
            LastExecution = execution;
            return ValueTask.FromResult(Result);
        }
    }

    private sealed class StubConfirmSalesExecutor : IConfirmSalesExecutor
    {
        public ValueTask<ApplicationResult<ConfirmSalesResult>> ExecuteAsync(
            ConfirmSalesExecution execution,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                ApplicationResult<ConfirmSalesResult>.Success(
                    new ConfirmSalesResult(Guid.NewGuid(), 2, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())));
    }
}