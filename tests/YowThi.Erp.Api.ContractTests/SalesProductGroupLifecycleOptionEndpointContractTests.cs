using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Hosting;
using YowThi.Erp.Api.Idempotency;
using YowThi.Erp.Api.Product;

namespace YowThi.Erp.Api.ContractTests;

public sealed class SalesProductGroupLifecycleOptionEndpointContractTests
{
    [Fact]
    public async Task Sales_product_group_lifecycle_options_use_target_specific_capability_without_idempotency()
    {
        await using var app = CreateApp();
        app.MapSalesProductGroupLifecycleOptionEndpoints();

        var endpoint = Assert.Single(
            GetRouteEndpoints(app),
            item => item.RoutePattern.RawText == "/api/v1/product/lifecycle-options/sales-product-groups");

        Assert.Contains(HttpMethods.Get, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        Assert.Equal(
            SalesProductGroupLifecycleOptionEndpoints.OperationId,
            endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            item => item.Policy == CapabilityPolicies.SalesProductGroupLifecycle);
        Assert.Null(endpoint.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());
        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>(),
            item => item.StatusCode == StatusCodes.Status200OK);
        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>(),
            item => item.StatusCode == StatusCodes.Status400BadRequest);
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

    private static RouteEndpoint[] GetRouteEndpoints(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
}
