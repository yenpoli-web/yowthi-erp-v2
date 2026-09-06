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
using YowThi.Erp.Api.Party;

namespace YowThi.Erp.Api.ContractTests;

public sealed class PartyLifecycleOptionEndpointContractTests
{
    [Fact]
    public async Task Party_lifecycle_option_queries_use_target_specific_capabilities_without_idempotency()
    {
        await using var app = CreateApp();
        app.MapPartyLifecycleOptionEndpoints();

        var expected = new Dictionary<string, (string OperationId, string Policy)>(StringComparer.Ordinal)
        {
            ["/api/v1/party/lifecycle-options/suppliers"] =
                (PartyLifecycleOptionEndpoints.SuppliersOperationId, CapabilityPolicies.SupplierLifecycle),
            ["/api/v1/party/lifecycle-options/customers"] =
                (PartyLifecycleOptionEndpoints.CustomersOperationId, CapabilityPolicies.CustomerLifecycle),
            ["/api/v1/party/lifecycle-options/outsourced-vendors"] =
                (PartyLifecycleOptionEndpoints.OutsourcedVendorsOperationId, CapabilityPolicies.OutsourcedVendorLifecycle),
            ["/api/v1/party/lifecycle-options/farmers"] =
                (PartyLifecycleOptionEndpoints.FarmersOperationId, CapabilityPolicies.FarmerLifecycle),
            ["/api/v1/party/lifecycle-options/employees"] =
                (PartyLifecycleOptionEndpoints.EmployeesOperationId, CapabilityPolicies.EmployeeLifecycle),
        };

        foreach (var pair in expected)
        {
            var endpoint = Assert.Single(
                GetRouteEndpoints(app),
                item => item.RoutePattern.RawText == pair.Key);

            Assert.Contains(HttpMethods.Get, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
            Assert.Equal(pair.Value.OperationId, endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                item => item.Policy == pair.Value.Policy);
            Assert.Null(endpoint.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>(),
                item => item.StatusCode == StatusCodes.Status200OK);
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>(),
                item => item.StatusCode == StatusCodes.Status400BadRequest);
        }
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
