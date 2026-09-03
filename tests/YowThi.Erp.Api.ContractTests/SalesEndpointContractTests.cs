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

public sealed class SalesEndpointContractTests
{
    [Fact]
    public async Task Confirm_sales_endpoint_matches_v1_contract()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapSalesEndpoints();

        var endpoint = GetConfirmSalesEndpoint(app);

        Assert.Contains(HttpMethods.Post, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        Assert.Equal(
            SalesEndpoints.ConfirmSalesOperationId,
            endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            metadata => metadata.Policy == CapabilityPolicies.SalesConfirm);
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
    public async Task Confirm_sales_uses_route_id_expected_version_and_string_origin_in_canonical_command()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapSalesEndpoints();

        var salesId = Guid.NewGuid();
        var detailId = Guid.NewGuid();
        var procurementBatchId = Guid.NewGuid();
        var receivableId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var operationId = Guid.NewGuid();

        fixture.Executor.Result = ApplicationResult<ConfirmSalesResult>.Success(
            new ConfirmSalesResult(salesId, 9, receivableId, revisionId, operationId));

        var body = $$"""
        {
          "expectedRowVersion": 8,
          "manualAllocationOverrides": [
            {
              "salesDetailId": "{{detailId}}",
              "origin": "IN_HOUSE",
              "procurementBatchId": "{{procurementBatchId}}",
              "outsourcedSupplyBatchId": null,
              "allocatedQuantity": 2.5
            }
          ]
        }
        """;

        var response = await InvokeAsync(
            app,
            GetConfirmSalesEndpoint(app),
            salesId,
            Guid.NewGuid(),
            body);

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.NotNull(fixture.Executor.LastExecution);
        Assert.Equal(salesId, fixture.Executor.LastExecution.Command.SalesId);
        Assert.Equal(8, fixture.Executor.LastExecution.Command.ExpectedRowVersion);

        var allocation = Assert.Single(fixture.Executor.LastExecution.Command.ManualAllocationOverrides);
        Assert.Equal(detailId, allocation.SalesDetailId);
        Assert.Equal(InventoryOrigin.IN_HOUSE, allocation.Origin);
        Assert.Equal(procurementBatchId, allocation.ProcurementBatchId);
        Assert.Null(allocation.OutsourcedSupplyBatchId);
        Assert.Equal(2.5m, allocation.AllocatedQuantity);

        Assert.NotNull(fixture.Hasher.LastPayloadJson);
        using (var canonical = JsonDocument.Parse(fixture.Hasher.LastPayloadJson))
        {
            var root = canonical.RootElement;
            Assert.Equal(SalesEndpoints.ConfirmSalesCommandType, root.GetProperty("commandType").GetString());
            var command = root.GetProperty("command");
            Assert.Equal(salesId, command.GetProperty("salesId").GetGuid());
            Assert.Equal(8, command.GetProperty("expectedRowVersion").GetInt64());
            Assert.Equal(
                "IN_HOUSE",
                command.GetProperty("manualAllocationOverrides")[0].GetProperty("origin").GetString());
        }

        using var responseJson = JsonDocument.Parse(response.Body);
        Assert.Equal(salesId, responseJson.RootElement.GetProperty("salesId").GetGuid());
        Assert.Equal(9, responseJson.RootElement.GetProperty("rowVersion").GetInt64());
        Assert.False(responseJson.RootElement.TryGetProperty("salesRowVersion", out _));
        Assert.Equal(receivableId, responseJson.RootElement.GetProperty("receivableId").GetGuid());
        Assert.Equal(revisionId, responseJson.RootElement.GetProperty("allocationRevisionId").GetGuid());
        Assert.Equal(operationId, responseJson.RootElement.GetProperty("inventoryOperationId").GetGuid());
    }

    [Fact]
    public async Task Confirm_sales_rejects_invalid_expected_version_as_transport_validation()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapSalesEndpoints();

        var response = await InvokeAsync(
            app,
            GetConfirmSalesEndpoint(app),
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"expectedRowVersion":0,"manualAllocationOverrides":[]}""");

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Null(fixture.Executor.LastExecution);
        AssertProblemCode(response.Body, ApiErrorCodes.RequestValidationFailed);
    }

    [Fact]
    public async Task Confirm_sales_maps_stale_row_version_to_conflict_problem_details()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapSalesEndpoints();
        fixture.Executor.Result = ApplicationResult<ConfirmSalesResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Conflict,
                SalesApplicationErrorCodes.StaleRowVersion));

        var response = await InvokeAsync(
            app,
            GetConfirmSalesEndpoint(app),
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"expectedRowVersion":8,"manualAllocationOverrides":[]}""");

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        AssertProblemCode(response.Body, SalesApplicationErrorCodes.StaleRowVersion);
    }

    [Fact]
    public async Task Confirm_sales_keeps_sales_001_issue_location_as_unprocessable_entity()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapSalesEndpoints();
        fixture.Executor.Result = ApplicationResult<ConfirmSalesResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Validation,
                SalesApplicationErrorCodes.IssueLocationRequired));

        var response = await InvokeAsync(
            app,
            GetConfirmSalesEndpoint(app),
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"expectedRowVersion":8,"manualAllocationOverrides":[]}""");

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, response.StatusCode);
        AssertProblemCode(response.Body, SalesApplicationErrorCodes.IssueLocationRequired);
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
        Guid salesId,
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
        context.Request.Path = $"/api/v1/sales/{salesId}/confirm";
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = bodyBytes.Length;
        context.Request.Body = requestBody;
        context.Request.Headers[IdempotencyKeyEndpointFilter.HeaderName] = commandId.ToString();
        context.Request.RouteValues["salesId"] = salesId.ToString();
        context.Response.Body = responseBody;
        context.SetEndpoint(endpoint);

        Assert.NotNull(endpoint.RequestDelegate);
        await endpoint.RequestDelegate(context);

        responseBody.Position = 0;
        using var reader = new StreamReader(responseBody, Encoding.UTF8, leaveOpen: true);
        var responseText = await reader.ReadToEndAsync();
        return new HttpInvocationResult(context.Response.StatusCode, responseText);
    }

    private static RouteEndpoint GetConfirmSalesEndpoint(WebApplication app)
    {
        return Assert.Single(
            GetRouteEndpoints(app),
            endpoint => endpoint.RoutePattern.RawText == "/api/v1/sales/{salesId:guid}/confirm");
    }

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

        var executor = new StubConfirmSalesExecutor();
        var hasher = new CapturingRequestHasher();
        builder.Services.AddSingleton<IActorContext>(
            new StubActorContext(ActorAccountId.From(Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339a0"))));
        builder.Services.AddSingleton<ICommandRequestHasher>(hasher);
        builder.Services.AddSingleton<IConfirmSalesExecutor>(executor);

        var app = builder.Build();
        app.UseYowThiApiInfrastructure();
        app.MapYowThiTechnicalEndpoints();
        return new AppFixture(app, executor, hasher);
    }

    private static RouteEndpoint[] GetRouteEndpoints(WebApplication app)
    {
        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
    }

    private sealed record AppFixture(
        WebApplication App,
        StubConfirmSalesExecutor Executor,
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
            return CommandRequestHash.FromSha256(Enumerable.Repeat((byte)0x2A, CommandRequestHash.Sha256Length).ToArray());
        }
    }

    private sealed class StubConfirmSalesExecutor : IConfirmSalesExecutor
    {
        public ApplicationResult<ConfirmSalesResult> Result { get; set; } =
            ApplicationResult<ConfirmSalesResult>.Success(
                new ConfirmSalesResult(
                    Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339a1"),
                    2,
                    Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339a2"),
                    Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339a3"),
                    Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339a4")));

        public ConfirmSalesExecution? LastExecution { get; private set; }

        public ValueTask<ApplicationResult<ConfirmSalesResult>> ExecuteAsync(
            ConfirmSalesExecution execution,
            CancellationToken cancellationToken)
        {
            LastExecution = execution;
            return ValueTask.FromResult(Result);
        }
    }
}
