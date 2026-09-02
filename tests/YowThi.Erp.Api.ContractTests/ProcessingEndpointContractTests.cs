using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Hosting;
using YowThi.Erp.Api.Idempotency;
using YowThi.Erp.Api.Processing;
using YowThi.Erp.Domain.Processing;

namespace YowThi.Erp.Api.ContractTests;

public sealed class ProcessingEndpointContractTests
{
    [Fact]
    public async Task Confirm_processing_execution_endpoint_matches_v1_contract()
    {
        await using var app = CreateApp();
        app.MapProcessingEndpoints();

        var endpoint = Assert.Single(
            GetRouteEndpoints(app),
            endpoint => endpoint.RoutePattern.RawText == "/api/v1/processing/executions");

        Assert.Contains(HttpMethods.Post, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        Assert.Equal(
            ProcessingEndpoints.ConfirmExecutionOperationId,
            endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            metadata => metadata.Policy == CapabilityPolicies.ProcessingConfirm);
        Assert.NotNull(endpoint.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());
        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>(),
            metadata => metadata.StatusCode == StatusCodes.Status201Created);
        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>(),
            metadata => metadata.StatusCode == StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task Processing_source_kind_uses_string_enum_transport_shape()
    {
        await using var app = CreateApp();
        var options = app.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;

        var source = JsonSerializer.Deserialize<ProcessingSourceSelectionRequest>(
            """{"sourceKind":"SUPPLIER","supplierId":null}""",
            options);

        Assert.NotNull(source);
        Assert.Equal(ProcessingSourceKind.SUPPLIER, source.SourceKind);
        Assert.Null(source.SupplierId);
    }

    [Fact]
    public void Processing_capability_policy_is_operation_oriented()
    {
        Assert.Equal("processing.confirm", CapabilityPolicies.ProcessingConfirm);
    }

    private static WebApplication CreateApp()
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

        var app = builder.Build();
        app.UseYowThiApiInfrastructure();
        app.MapYowThiTechnicalEndpoints();
        return app;
    }

    private static RouteEndpoint[] GetRouteEndpoints(WebApplication app)
    {
        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
    }
}
