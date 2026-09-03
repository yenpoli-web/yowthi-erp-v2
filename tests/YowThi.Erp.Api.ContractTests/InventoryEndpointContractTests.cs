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
using YowThi.Erp.Api.Inventory;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Serialization;
using YowThi.Erp.Application.Inventory;
using YowThi.Erp.Domain.Inventory;

namespace YowThi.Erp.Api.ContractTests;

public sealed class InventoryEndpointContractTests
{
    [Fact]
    public async Task Inventory_write_endpoints_match_v1_contract()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapInventoryEndpoints();

        AssertEndpointContract(
            GetEndpoint(app, "/api/v1/inventory/transfers"),
            InventoryEndpoints.TransferInventoryOperationId);
        AssertEndpointContract(
            GetEndpoint(app, "/api/v1/inventory/adjustments"),
            InventoryEndpoints.AdjustInventoryOperationId);
    }

    [Fact]
    public async Task Transfer_inventory_uses_full_position_identity_string_enums_and_canonical_command()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapInventoryEndpoints();

        var batchId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var sourceLocationId = Guid.NewGuid();
        var destinationLocationId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var transferOutId = Guid.NewGuid();
        var transferInId = Guid.NewGuid();
        fixture.TransferExecutor.Result = ApplicationResult<TransferInventoryResult>.Success(
            new TransferInventoryResult(operationId, transferOutId, transferInId, 4.25m));

        var body = $$"""
            {
              "inventoryIdentity": {
                "origin": "IN_HOUSE",
                "procurementBatchId": "{{batchId}}",
                "outsourcedSupplyBatchId": null,
                "inventoryObjectKind": "PROCUREMENT_PRODUCT",
                "procurementProductId": "{{productId}}",
                "processMaterialId": null,
                "salesProductId": null,
                "rawSourceKind": "SUPPLIER",
                "supplierId": "{{supplierId}}"
              },
              "sourceStorageLocationId": "{{sourceLocationId}}",
              "destinationStorageLocationId": "{{destinationLocationId}}",
              "quantity": 4.25
            }
            """;

        var response = await InvokeAsync(
            app,
            GetEndpoint(app, "/api/v1/inventory/transfers"),
            Guid.NewGuid(),
            body);

        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        var execution = Assert.IsType<TransferInventoryExecution>(fixture.TransferExecutor.LastExecution);
        Assert.Equal(InventoryOrigin.IN_HOUSE, execution.Command.InventoryIdentity.Origin);
        Assert.Equal(batchId, execution.Command.InventoryIdentity.ProcurementBatchId);
        Assert.Null(execution.Command.InventoryIdentity.OutsourcedSupplyBatchId);
        Assert.Equal(InventoryObjectKind.PROCUREMENT_PRODUCT, execution.Command.InventoryIdentity.InventoryObjectKind);
        Assert.Equal(productId, execution.Command.InventoryIdentity.ProcurementProductId);
        Assert.Equal(InventoryRawSourceKind.SUPPLIER, execution.Command.InventoryIdentity.RawSourceKind);
        Assert.Equal(supplierId, execution.Command.InventoryIdentity.SupplierId);
        Assert.Equal(sourceLocationId, execution.Command.SourceStorageLocationId);
        Assert.Equal(destinationLocationId, execution.Command.DestinationStorageLocationId);
        Assert.Equal(4.25m, execution.Command.Quantity);

        AssertCanonical(
            fixture.Hasher.LastPayloadJson,
            InventoryEndpoints.TransferInventoryCommandType,
            command =>
            {
                var identity = command.GetProperty("inventoryIdentity");
                Assert.Equal("IN_HOUSE", identity.GetProperty("origin").GetString());
                Assert.Equal(batchId, identity.GetProperty("procurementBatchId").GetGuid());
                Assert.Equal("PROCUREMENT_PRODUCT", identity.GetProperty("inventoryObjectKind").GetString());
                Assert.Equal(productId, identity.GetProperty("procurementProductId").GetGuid());
                Assert.Equal("SUPPLIER", identity.GetProperty("rawSourceKind").GetString());
                Assert.Equal(supplierId, identity.GetProperty("supplierId").GetGuid());
                Assert.Equal(sourceLocationId, command.GetProperty("sourceStorageLocationId").GetGuid());
                Assert.Equal(destinationLocationId, command.GetProperty("destinationStorageLocationId").GetGuid());
                Assert.Equal(4.25m, command.GetProperty("quantity").GetDecimal());
            });

        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(operationId, json.RootElement.GetProperty("inventoryOperationId").GetGuid());
        Assert.Equal(transferOutId, json.RootElement.GetProperty("transferOutMovementId").GetGuid());
        Assert.Equal(transferInId, json.RootElement.GetProperty("transferInMovementId").GetGuid());
        Assert.Equal(4.25m, json.RootElement.GetProperty("quantity").GetDecimal());
    }

    [Fact]
    public async Task Adjust_inventory_preserves_signed_delta_reason_and_string_enum_identity()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapInventoryEndpoints();

        var outsourcedBatchId = Guid.NewGuid();
        var salesProductId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var movementId = Guid.NewGuid();
        fixture.AdjustExecutor.Result = ApplicationResult<AdjustInventoryResult>.Success(
            new AdjustInventoryResult(operationId, movementId, -3.5m));

        var body = $$"""
            {
              "inventoryIdentity": {
                "origin": "OUTSOURCED",
                "procurementBatchId": null,
                "outsourcedSupplyBatchId": "{{outsourcedBatchId}}",
                "inventoryObjectKind": "SALES_PRODUCT",
                "procurementProductId": null,
                "processMaterialId": null,
                "salesProductId": "{{salesProductId}}",
                "rawSourceKind": null,
                "supplierId": null
              },
              "storageLocationId": "{{locationId}}",
              "quantityDelta": -3.5,
              "reasonText": "count correction"
            }
            """;

        var response = await InvokeAsync(
            app,
            GetEndpoint(app, "/api/v1/inventory/adjustments"),
            Guid.NewGuid(),
            body);

        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        var execution = Assert.IsType<AdjustInventoryExecution>(fixture.AdjustExecutor.LastExecution);
        Assert.Equal(InventoryOrigin.OUTSOURCED, execution.Command.InventoryIdentity.Origin);
        Assert.Equal(outsourcedBatchId, execution.Command.InventoryIdentity.OutsourcedSupplyBatchId);
        Assert.Equal(InventoryObjectKind.SALES_PRODUCT, execution.Command.InventoryIdentity.InventoryObjectKind);
        Assert.Equal(salesProductId, execution.Command.InventoryIdentity.SalesProductId);
        Assert.Null(execution.Command.InventoryIdentity.RawSourceKind);
        Assert.Equal(locationId, execution.Command.StorageLocationId);
        Assert.Equal(-3.5m, execution.Command.QuantityDelta);
        Assert.Equal("count correction", execution.Command.ReasonText);

        AssertCanonical(
            fixture.Hasher.LastPayloadJson,
            InventoryEndpoints.AdjustInventoryCommandType,
            command =>
            {
                var identity = command.GetProperty("inventoryIdentity");
                Assert.Equal("OUTSOURCED", identity.GetProperty("origin").GetString());
                Assert.Equal(outsourcedBatchId, identity.GetProperty("outsourcedSupplyBatchId").GetGuid());
                Assert.Equal("SALES_PRODUCT", identity.GetProperty("inventoryObjectKind").GetString());
                Assert.Equal(salesProductId, identity.GetProperty("salesProductId").GetGuid());
                Assert.Equal(JsonValueKind.Null, identity.GetProperty("rawSourceKind").ValueKind);
                Assert.Equal(locationId, command.GetProperty("storageLocationId").GetGuid());
                Assert.Equal(-3.5m, command.GetProperty("quantityDelta").GetDecimal());
                Assert.Equal("count correction", command.GetProperty("reasonText").GetString());
            });

        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(operationId, json.RootElement.GetProperty("inventoryOperationId").GetGuid());
        Assert.Equal(movementId, json.RootElement.GetProperty("adjustmentMovementId").GetGuid());
        Assert.Equal(-3.5m, json.RootElement.GetProperty("quantityDelta").GetDecimal());
    }

    [Fact]
    public async Task Inventory_write_rejects_missing_identity_or_empty_required_location_as_transport_validation()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapInventoryEndpoints();

        var transfer = await InvokeAsync(
            app,
            GetEndpoint(app, "/api/v1/inventory/transfers"),
            Guid.NewGuid(),
            $$"""
              {
                "inventoryIdentity": null,
                "sourceStorageLocationId": "{{Guid.NewGuid()}}",
                "destinationStorageLocationId": "{{Guid.NewGuid()}}",
                "quantity": 1
              }
              """);
        Assert.Equal(StatusCodes.Status400BadRequest, transfer.StatusCode);
        Assert.Null(fixture.TransferExecutor.LastExecution);
        AssertProblemCode(transfer.Body, ApiErrorCodes.RequestValidationFailed);

        var adjustment = await InvokeAsync(
            app,
            GetEndpoint(app, "/api/v1/inventory/adjustments"),
            Guid.NewGuid(),
            """
              {
                "inventoryIdentity": null,
                "storageLocationId": "00000000-0000-0000-0000-000000000000",
                "quantityDelta": 1,
                "reasonText": "count"
              }
              """);
        Assert.Equal(StatusCodes.Status400BadRequest, adjustment.StatusCode);
        Assert.Null(fixture.AdjustExecutor.LastExecution);
        AssertProblemCode(adjustment.Body, ApiErrorCodes.RequestValidationFailed);
    }

    [Fact]
    public async Task Inventory_semantic_validation_maps_to_422_with_stable_code()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapInventoryEndpoints();

        fixture.AdjustExecutor.Result = ApplicationResult<AdjustInventoryResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Validation,
                InventoryApplicationErrorCodes.InvalidInput));

        var response = await InvokeAsync(
            app,
            GetEndpoint(app, "/api/v1/inventory/adjustments"),
            Guid.NewGuid(),
            CreateValidAdjustmentBody(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0m, ""));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, response.StatusCode);
        AssertProblemCode(response.Body, InventoryApplicationErrorCodes.InvalidInput);
    }

    [Fact]
    public async Task Transfer_inventory_maps_position_not_found_and_stock_conflicts_to_stable_statuses()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapInventoryEndpoints();

        var batchId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var sourceLocationId = Guid.NewGuid();
        var destinationLocationId = Guid.NewGuid();
        var body = CreateValidTransferBody(batchId, productId, supplierId, sourceLocationId, destinationLocationId, 5m);

        fixture.TransferExecutor.Result = ApplicationResult<TransferInventoryResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.NotFound,
                InventoryApplicationErrorCodes.PositionNotFound));
        var notFound = await InvokeAsync(
            app,
            GetEndpoint(app, "/api/v1/inventory/transfers"),
            Guid.NewGuid(),
            body);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
        AssertProblemCode(notFound.Body, InventoryApplicationErrorCodes.PositionNotFound);

        fixture.TransferExecutor.Result = ApplicationResult<TransferInventoryResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Conflict,
                InventoryApplicationErrorCodes.InsufficientStock));
        var insufficient = await InvokeAsync(
            app,
            GetEndpoint(app, "/api/v1/inventory/transfers"),
            Guid.NewGuid(),
            body);
        Assert.Equal(StatusCodes.Status409Conflict, insufficient.StatusCode);
        AssertProblemCode(insufficient.Body, InventoryApplicationErrorCodes.InsufficientStock);

        fixture.TransferExecutor.Result = ApplicationResult<TransferInventoryResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Conflict,
                InventoryApplicationErrorCodes.ConcurrentChange));
        var concurrent = await InvokeAsync(
            app,
            GetEndpoint(app, "/api/v1/inventory/transfers"),
            Guid.NewGuid(),
            body);
        Assert.Equal(StatusCodes.Status409Conflict, concurrent.StatusCode);
        AssertProblemCode(concurrent.Body, InventoryApplicationErrorCodes.ConcurrentChange);
    }

    [Fact]
    public async Task Inventory_idempotency_reuse_maps_to_conflict()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapInventoryEndpoints();

        fixture.AdjustExecutor.Result = ApplicationResult<AdjustInventoryResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Conflict,
                InventoryApplicationErrorCodes.IdempotencyKeyReused));

        var response = await InvokeAsync(
            app,
            GetEndpoint(app, "/api/v1/inventory/adjustments"),
            Guid.NewGuid(),
            CreateValidAdjustmentBody(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2m, "count"));

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        AssertProblemCode(response.Body, InventoryApplicationErrorCodes.IdempotencyKeyReused);
    }

    private static string CreateValidTransferBody(
        Guid batchId,
        Guid productId,
        Guid supplierId,
        Guid sourceLocationId,
        Guid destinationLocationId,
        decimal quantity) =>
        $$"""
          {
            "inventoryIdentity": {
              "origin": "IN_HOUSE",
              "procurementBatchId": "{{batchId}}",
              "outsourcedSupplyBatchId": null,
              "inventoryObjectKind": "PROCUREMENT_PRODUCT",
              "procurementProductId": "{{productId}}",
              "processMaterialId": null,
              "salesProductId": null,
              "rawSourceKind": "SUPPLIER",
              "supplierId": "{{supplierId}}"
            },
            "sourceStorageLocationId": "{{sourceLocationId}}",
            "destinationStorageLocationId": "{{destinationLocationId}}",
            "quantity": {{quantity}}
          }
          """;

    private static string CreateValidAdjustmentBody(
        Guid batchId,
        Guid productId,
        Guid supplierId,
        Guid locationId,
        Guid unused,
        decimal quantityDelta,
        string reasonText) =>
        $$"""
          {
            "inventoryIdentity": {
              "origin": "IN_HOUSE",
              "procurementBatchId": "{{batchId}}",
              "outsourcedSupplyBatchId": null,
              "inventoryObjectKind": "PROCUREMENT_PRODUCT",
              "procurementProductId": "{{productId}}",
              "processMaterialId": null,
              "salesProductId": null,
              "rawSourceKind": "SUPPLIER",
              "supplierId": "{{supplierId}}"
            },
            "storageLocationId": "{{locationId}}",
            "quantityDelta": {{quantityDelta}},
            "reasonText": "{{reasonText}}"
          }
          """;

    private static void AssertEndpointContract(RouteEndpoint endpoint, string operationId)
    {
        Assert.Contains(HttpMethods.Post, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        Assert.Equal(operationId, endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            metadata => metadata.Policy == CapabilityPolicies.InventoryAdjust);
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
        context.Request.Path = endpoint.RoutePattern.RawText!;
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

        var transferExecutor = new StubTransferInventoryExecutor();
        var adjustExecutor = new StubAdjustInventoryExecutor();
        var hasher = new CapturingRequestHasher();
        builder.Services.AddSingleton<IActorContext>(
            new StubActorContext(ActorAccountId.From(Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339d0"))));
        builder.Services.AddSingleton<ICommandRequestHasher>(hasher);
        builder.Services.AddSingleton<ITransferInventoryExecutor>(transferExecutor);
        builder.Services.AddSingleton<IAdjustInventoryExecutor>(adjustExecutor);

        var app = builder.Build();
        app.UseYowThiApiInfrastructure();
        app.MapYowThiTechnicalEndpoints();
        return new AppFixture(app, transferExecutor, adjustExecutor, hasher);
    }

    private static RouteEndpoint[] GetRouteEndpoints(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

    private sealed record AppFixture(
        WebApplication App,
        StubTransferInventoryExecutor TransferExecutor,
        StubAdjustInventoryExecutor AdjustExecutor,
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

    private sealed class StubTransferInventoryExecutor : ITransferInventoryExecutor
    {
        public ApplicationResult<TransferInventoryResult> Result { get; set; } =
            ApplicationResult<TransferInventoryResult>.Success(
                new TransferInventoryResult(
                    Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339d1"),
                    Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339d2"),
                    Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339d3"),
                    1m));

        public TransferInventoryExecution? LastExecution { get; private set; }

        public ValueTask<ApplicationResult<TransferInventoryResult>> ExecuteAsync(
            TransferInventoryExecution execution,
            CancellationToken cancellationToken)
        {
            LastExecution = execution;
            return ValueTask.FromResult(Result);
        }
    }

    private sealed class StubAdjustInventoryExecutor : IAdjustInventoryExecutor
    {
        public ApplicationResult<AdjustInventoryResult> Result { get; set; } =
            ApplicationResult<AdjustInventoryResult>.Success(
                new AdjustInventoryResult(
                    Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339d4"),
                    Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339d5"),
                    1m));

        public AdjustInventoryExecution? LastExecution { get; private set; }

        public ValueTask<ApplicationResult<AdjustInventoryResult>> ExecuteAsync(
            AdjustInventoryExecution execution,
            CancellationToken cancellationToken)
        {
            LastExecution = execution;
            return ValueTask.FromResult(Result);
        }
    }
}