using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.DataProtection;
using YowThi.Erp.Api.Hosting;
using YowThi.Erp.Api.Idempotency;

namespace YowThi.Erp.Api.ContractTests;

public sealed class HardDeleteOptionEndpointContractTests
{
    [Fact]
    public async Task Hard_delete_option_queries_use_highest_authority_capability_without_idempotency()
    {
        await using var app = CreateApp();
        app.MapHardDeleteOptionEndpoints();

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v1/data-protection/hard-delete-options/suppliers"] = HardDeleteOptionEndpoints.SuppliersOperationId,
            ["/api/v1/data-protection/hard-delete-options/customers"] = HardDeleteOptionEndpoints.CustomersOperationId,
            ["/api/v1/data-protection/hard-delete-options/outsourced-vendors"] = HardDeleteOptionEndpoints.OutsourcedVendorsOperationId,
            ["/api/v1/data-protection/hard-delete-options/farmers"] = HardDeleteOptionEndpoints.FarmersOperationId,
            ["/api/v1/data-protection/hard-delete-options/processing-executions"] = HardDeleteOptionEndpoints.ProcessingExecutionsOperationId,
            ["/api/v1/data-protection/hard-delete-options/processing-execution-inputs"] = HardDeleteOptionEndpoints.ProcessingExecutionInputsOperationId,
            ["/api/v1/data-protection/hard-delete-options/processing-execution-outputs"] = HardDeleteOptionEndpoints.ProcessingExecutionOutputsOperationId,
        };

        foreach (var pair in expected)
        {
            var endpoint = Assert.Single(
                GetRouteEndpoints(app),
                item => item.RoutePattern.RawText == pair.Key);

            Assert.Contains(HttpMethods.Get, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
            Assert.Equal(pair.Value, endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                item => item.Policy == CapabilityPolicies.DataProtectionHardDelete);
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
