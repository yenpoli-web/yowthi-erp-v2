using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Finance;
using YowThi.Erp.Api.Hosting;
using YowThi.Erp.Api.Idempotency;

namespace YowThi.Erp.Api.ContractTests;

public sealed class FinanceSettlementOptionEndpointContractTests
{
    [Fact]
    public async Task Finance_settlement_option_queries_are_authenticated_purpose_specific_gets_without_idempotency()
    {
        await using var app = CreateApp();
        app.MapFinanceSettlementOptionEndpoints();

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v1/finance/settlement-options/payables"] = FinanceSettlementOptionEndpoints.PayablesOperationId,
            ["/api/v1/finance/settlement-options/receivables"] = FinanceSettlementOptionEndpoints.ReceivablesOperationId,
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
                metadata => metadata.Policy == CapabilityPolicies.FinancePay);
            Assert.Null(endpoint.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>(),
                metadata => metadata.StatusCode == StatusCodes.Status200OK);
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>(),
                metadata => metadata.StatusCode == StatusCodes.Status400BadRequest);
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
