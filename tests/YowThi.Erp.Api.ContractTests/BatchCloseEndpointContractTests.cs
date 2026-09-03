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
using YowThi.Erp.Api.Outsourced;
using YowThi.Erp.Api.Procurement;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Serialization;
using YowThi.Erp.Application.Outsourced;
using YowThi.Erp.Application.Procurement;

namespace YowThi.Erp.Api.ContractTests;

public sealed class BatchCloseEndpointContractTests
{
    [Fact]
    public async Task Batch_close_endpoints_are_purpose_authorized_idempotent_v1_posts()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapProcurementEndpoints();
        app.MapOutsourcedEndpoints();

        AssertEndpointContract(
            GetEndpoint(app, "/api/v1/procurement/batches/{procurementBatchId:guid}/close"),
            ProcurementEndpoints.CloseBatchOperationId,
            CapabilityPolicies.ProcurementConfirm);
        AssertEndpointContract(
            GetEndpoint(app, "/api/v1/outsourced/batches/{outsourcedSupplyBatchId:guid}/close"),
            OutsourcedEndpoints.CloseBatchOperationId,
            CapabilityPolicies.OutsourcedConfirm);
    }

    [Fact]
    public async Task Procurement_close_canonical_command_includes_route_batch_id_and_expected_row_version()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapProcurementEndpoints();

        var batchId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        fixture.ProcurementExecutor.Result = ApplicationResult<CloseProcurementBatchResult>.Success(
            new CloseProcurementBatchResult(batchId, operationId, 2, 8));

        var response = await InvokeAsync(
            app,
            GetEndpoint(app, "/api/v1/procurement/batches/{procurementBatchId:guid}/close"),
            "procurementBatchId",
            batchId,
            Guid.NewGuid(),
            """{"expectedRowVersion":7}""");

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        var execution = Assert.IsType<CloseProcurementBatchExecution>(fixture.ProcurementExecutor.LastExecution);
        Assert.Equal(batchId, execution.Command.ProcurementBatchId);
        Assert.Equal(7, execution.Command.ExpectedRowVersion);

        AssertCanonical(
            fixture.Hasher.LastPayloadJson,
            ProcurementEndpoints.CloseBatchCommandType,
            command =>
            {
                Assert.Equal(batchId, command.GetProperty("procurementBatchId").GetGuid());
                Assert.Equal(7, command.GetProperty("expectedRowVersion").GetInt64());
            });

        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(batchId, json.RootElement.GetProperty("procurementBatchId").GetGuid());
        Assert.Equal(operationId, json.RootElement.GetProperty("inventoryOperationId").GetGuid());
        Assert.Equal(2, json.RootElement.GetProperty("reconciledPositionCount").GetInt32());
        Assert.Equal(8, json.RootElement.GetProperty("closedRowVersion").GetInt64());
    }

    [Fact]
    public async Task Outsourced_close_canonical_command_includes_route_batch_id_and_expected_row_version()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapOutsourcedEndpoints();

        var batchId = Guid.NewGuid();
        fixture.OutsourcedExecutor.Result = ApplicationResult<CloseOutsourcedSupplyBatchResult>.Success(
            new CloseOutsourcedSupplyBatchResult(batchId, 5));

        var response = await InvokeAsync(
            app,
            GetEndpoint(app, "/api/v1/outsourced/batches/{outsourcedSupplyBatchId:guid}/close"),
            "outsourcedSupplyBatchId",
            batchId,
            Guid.NewGuid(),
            """{"expectedRowVersion":4}""");

        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        var execution = Assert.IsType<CloseOutsourcedSupplyBatchExecution>(fixture.OutsourcedExecutor.LastExecution);
        Assert.Equal(batchId, execution.Command.OutsourcedSupplyBatchId);
        Assert.Equal(4, execution.Command.ExpectedRowVersion);

        AssertCanonical(
            fixture.Hasher.LastPayloadJson,
            OutsourcedEndpoints.CloseBatchCommandType,
            command =>
            {
                Assert.Equal(batchId, command.GetProperty("outsourcedSupplyBatchId").GetGuid());
                Assert.Equal(4, command.GetProperty("expectedRowVersion").GetInt64());
            });
    }

    [Fact]
    public async Task Batch_close_maps_semantic_validation_and_sellable_inventory_conflict_to_stable_statuses()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapProcurementEndpoints();
        app.MapOutsourcedEndpoints();

        fixture.ProcurementExecutor.Result = ApplicationResult<CloseProcurementBatchResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Conflict,
                ProcurementBatchCloseErrorCodes.SellableInventoryRemaining));
        var procurement = await InvokeAsync(
            app,
            GetEndpoint(app, "/api/v1/procurement/batches/{procurementBatchId:guid}/close"),
            "procurementBatchId",
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"expectedRowVersion":1}""");
        Assert.Equal(StatusCodes.Status409Conflict, procurement.StatusCode);
        AssertProblemCode(procurement.Body, ProcurementBatchCloseErrorCodes.SellableInventoryRemaining);

        fixture.OutsourcedExecutor.Result = ApplicationResult<CloseOutsourcedSupplyBatchResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Validation,
                OutsourcedBatchCloseErrorCodes.InvalidInput));
        var outsourced = await InvokeAsync(
            app,
            GetEndpoint(app, "/api/v1/outsourced/batches/{outsourcedSupplyBatchId:guid}/close"),
            "outsourcedSupplyBatchId",
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"expectedRowVersion":0}""");
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, outsourced.StatusCode);
        AssertProblemCode(outsourced.Body, OutsourcedBatchCloseErrorCodes.InvalidInput);
    }

    private static void AssertEndpointContract(
        RouteEndpoint endpoint,
        string operationId,
        string capabilityPolicy)
    {
        Assert.Contains(HttpMethods.Post, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        Assert.Equal(operationId, endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            metadata => metadata.Policy == capabilityPolicy);
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
        string routeParameterName,
        Guid routeValue,
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
            .Replace($"{{{routeParameterName}:guid}}", routeValue.ToString(), StringComparison.Ordinal);
        context.Request.RouteValues[routeParameterName] = routeValue.ToString();
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
            await reader.ReadToEndAsync());
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

        var procurementExecutor = new StubCloseProcurementBatchExecutor();
        var outsourcedExecutor = new StubCloseOutsourcedSupplyBatchExecutor();
        var hasher = new CapturingRequestHasher();
        builder.Services.AddSingleton<IActorContext>(
            new StubActorContext(ActorAccountId.From(Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339d0"))));
        builder.Services.AddSingleton<ICommandRequestHasher>(hasher);
        builder.Services.AddSingleton<ICloseProcurementBatchExecutor>(procurementExecutor);
        builder.Services.AddSingleton<ICloseOutsourcedSupplyBatchExecutor>(outsourcedExecutor);

        var app = builder.Build();
        app.UseYowThiApiInfrastructure();
        app.MapYowThiTechnicalEndpoints();
        return new AppFixture(app, procurementExecutor, outsourcedExecutor, hasher);
    }

    private static RouteEndpoint[] GetRouteEndpoints(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

    private sealed record AppFixture(
        WebApplication App,
        StubCloseProcurementBatchExecutor ProcurementExecutor,
        StubCloseOutsourcedSupplyBatchExecutor OutsourcedExecutor,
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
                Enumerable.Repeat((byte)0x6A, CommandRequestHash.Sha256Length).ToArray());
        }
    }

    private sealed class StubCloseProcurementBatchExecutor : ICloseProcurementBatchExecutor
    {
        public ApplicationResult<CloseProcurementBatchResult> Result { get; set; } =
            ApplicationResult<CloseProcurementBatchResult>.Success(
                new CloseProcurementBatchResult(Guid.NewGuid(), null, 0, 2));

        public CloseProcurementBatchExecution? LastExecution { get; private set; }

        public ValueTask<ApplicationResult<CloseProcurementBatchResult>> ExecuteAsync(
            CloseProcurementBatchExecution execution,
            CancellationToken cancellationToken)
        {
            LastExecution = execution;
            return ValueTask.FromResult(Result);
        }
    }

    private sealed class StubCloseOutsourcedSupplyBatchExecutor : ICloseOutsourcedSupplyBatchExecutor
    {
        public ApplicationResult<CloseOutsourcedSupplyBatchResult> Result { get; set; } =
            ApplicationResult<CloseOutsourcedSupplyBatchResult>.Success(
                new CloseOutsourcedSupplyBatchResult(Guid.NewGuid(), 2));

        public CloseOutsourcedSupplyBatchExecution? LastExecution { get; private set; }

        public ValueTask<ApplicationResult<CloseOutsourcedSupplyBatchResult>> ExecuteAsync(
            CloseOutsourcedSupplyBatchExecution execution,
            CancellationToken cancellationToken)
        {
            LastExecution = execution;
            return ValueTask.FromResult(Result);
        }
    }
}
