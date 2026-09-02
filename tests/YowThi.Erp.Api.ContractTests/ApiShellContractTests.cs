using System.Text.Json;
using System.Text.Json.Serialization;
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
using YowThi.Erp.Api.Outsourced;
using YowThi.Erp.Api.Procurement;
using YowThi.Erp.Api.Routing;

namespace YowThi.Erp.Api.ContractTests;

public sealed class ApiShellContractTests
{
    [Fact]
    public async Task Api_v1_group_is_authenticated_by_default()
    {
        await using var app = CreateApp(Environments.Development);
        var api = app.MapApiV1();
        api.MapGet("/contract-probe", static () => TypedResults.NoContent())
            .WithName("Contract_Probe");

        var endpoint = Assert.Single(GetRouteEndpoints(app), endpoint => endpoint.RoutePattern.RawText == "/api/v1/contract-probe");

        Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
    }

    [Fact]
    public async Task Confirm_procurement_entry_endpoint_matches_v1_contract()
    {
        await using var app = CreateApp(Environments.Development);
        app.MapProcurementEndpoints();

        var endpoint = Assert.Single(
            GetRouteEndpoints(app),
            endpoint => endpoint.RoutePattern.RawText == "/api/v1/procurement/entries");

        Assert.Contains(HttpMethods.Post, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        Assert.Equal(
            ProcurementEndpoints.ConfirmEntryOperationId,
            endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            metadata => metadata.Policy == CapabilityPolicies.ProcurementConfirm);
        Assert.NotNull(endpoint.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());
        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>(),
            metadata => metadata.StatusCode == StatusCodes.Status201Created);
    }

    [Fact]
    public async Task Confirm_outsourced_supply_detail_endpoint_matches_v1_contract()
    {
        await using var app = CreateApp(Environments.Development);
        app.MapOutsourcedEndpoints();

        var endpoint = Assert.Single(
            GetRouteEndpoints(app),
            endpoint => endpoint.RoutePattern.RawText == "/api/v1/outsourced/supply-details");

        Assert.Contains(HttpMethods.Post, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        Assert.Equal(
            OutsourcedEndpoints.ConfirmSupplyDetailOperationId,
            endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            metadata => metadata.Policy == CapabilityPolicies.OutsourcedConfirm);
        Assert.NotNull(endpoint.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());
        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>(),
            metadata => metadata.StatusCode == StatusCodes.Status201Created);
    }

    [Fact]
    public async Task Outsourced_supply_detail_option_queries_are_authenticated_purpose_specific_gets_without_idempotency()
    {
        await using var app = CreateApp(Environments.Development);
        app.MapOutsourcedSupplyDetailOptionEndpoints();

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v1/outsourced/supply-detail-options/vendors"] = OutsourcedSupplyDetailOptionEndpoints.VendorsOperationId,
            ["/api/v1/outsourced/supply-detail-options/products"] = OutsourcedSupplyDetailOptionEndpoints.ProductsOperationId,
            ["/api/v1/outsourced/supply-detail-options/storage-locations"] = OutsourcedSupplyDetailOptionEndpoints.StorageLocationsOperationId,
        };

        foreach (var pair in expected)
        {
            var endpoint = Assert.Single(
                GetRouteEndpoints(app),
                endpoint => endpoint.RoutePattern.RawText == pair.Key);

            Assert.Contains(HttpMethods.Get, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
            Assert.Equal(pair.Value, endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                metadata => metadata.Policy == CapabilityPolicies.OutsourcedConfirm);
            Assert.Null(endpoint.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>(),
                metadata => metadata.StatusCode == StatusCodes.Status200OK);
        }
    }

    [Fact]
    public async Task Procurement_entry_option_queries_are_authenticated_purpose_specific_gets_without_idempotency()
    {
        await using var app = CreateApp(Environments.Development);
        app.MapProcurementEntryOptionEndpoints();

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v1/procurement/entry-options/products"] = ProcurementEntryOptionEndpoints.ProductsOperationId,
            ["/api/v1/procurement/entry-options/sources"] = ProcurementEntryOptionEndpoints.SourcesOperationId,
            ["/api/v1/procurement/entry-options/storage-locations"] = ProcurementEntryOptionEndpoints.StorageLocationsOperationId,
        };

        foreach (var pair in expected)
        {
            var endpoint = Assert.Single(
                GetRouteEndpoints(app),
                endpoint => endpoint.RoutePattern.RawText == pair.Key);

            Assert.Contains(HttpMethods.Get, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
            Assert.Equal(pair.Value, endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                metadata => metadata.Policy == CapabilityPolicies.ProcurementConfirm);
            Assert.Null(endpoint.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>(),
                metadata => metadata.StatusCode == StatusCodes.Status200OK);
        }
    }

    [Fact]
    public async Task Health_probe_is_minimal_anonymous_and_excluded_from_OpenApi()
    {
        await using var app = CreateApp(Environments.Development);
        var endpoint = Assert.Single(GetRouteEndpoints(app), endpoint => endpoint.RoutePattern.RawText == "/health/live");

        Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        Assert.NotNull(endpoint.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>());
        Assert.Equal("Health_Live", endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
    }

    [Fact]
    public async Task Development_maps_first_party_OpenApi_and_Production_does_not()
    {
        await using var development = CreateApp(Environments.Development);
        Assert.Contains(
            GetRouteEndpoints(development),
            endpoint => endpoint.RoutePattern.RawText?.Contains("/openapi/", StringComparison.Ordinal) == true);

        await using var production = CreateApp(Environments.Production);
        Assert.DoesNotContain(
            GetRouteEndpoints(production),
            endpoint => endpoint.RoutePattern.RawText?.Contains("/openapi/", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Json_write_contract_rejects_unknown_properties()
    {
        await using var app = CreateApp(Environments.Development);
        var options = app.Services.GetRequiredService<IOptions<JsonOptions>>().Value;

        Assert.Equal(JsonNamingPolicy.CamelCase, options.SerializerOptions.PropertyNamingPolicy);
        Assert.Equal(JsonUnmappedMemberHandling.Disallow, options.SerializerOptions.UnmappedMemberHandling);
    }

    [Fact]
    public void Module_routes_are_the_confirmed_v1_modules()
    {
        Assert.Equal(
            new[]
            {
                "party",
                "product",
                "processing-config",
                "procurement",
                "processing",
                "inventory",
                "outsourced",
                "sales",
                "sales-handling",
                "labor",
                "finance",
                "audit",
                "data-protection",
            },
            ApiRouteGroups.Modules);
    }

    [Fact]
    public void Capability_policy_names_are_operation_oriented_not_roles()
    {
        Assert.Equal("procurement.confirm", CapabilityPolicies.ProcurementConfirm);
        Assert.Equal("outsourced.confirm", CapabilityPolicies.OutsourcedConfirm);
        Assert.Equal("sales.confirm", CapabilityPolicies.SalesConfirm);
        Assert.Equal("finance.pay", CapabilityPolicies.FinancePay);
        Assert.Equal("inventory.adjust", CapabilityPolicies.InventoryAdjust);
        Assert.Equal("data-protection.hard-delete", CapabilityPolicies.DataProtectionHardDelete);
    }

    private static WebApplication CreateApp(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = environment,
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
