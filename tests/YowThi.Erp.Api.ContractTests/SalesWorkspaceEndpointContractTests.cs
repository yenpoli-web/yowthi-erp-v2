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
using YowThi.Erp.Api.Sales;

namespace YowThi.Erp.Api.ContractTests;

public sealed class SalesWorkspaceEndpointContractTests
{
    [Fact]
    public async Task Sales_workspace_queries_are_read_only_and_require_transaction_lifecycle_capability()
    {
        await using var app = CreateApp();
        app.MapSalesWorkspaceEndpoints();

        var endpoints = GetRouteEndpoints(app);
        var list = Assert.Single(endpoints, endpoint => endpoint.RoutePattern.RawText == "/api/v1/sales/workspace");
        var detail = Assert.Single(endpoints, endpoint => endpoint.RoutePattern.RawText == "/api/v1/sales/workspace/{salesId:guid}");

        foreach (var endpoint in new[] { list, detail })
        {
            Assert.Contains(HttpMethods.Get, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
            Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(), item => item.Policy == CapabilityPolicies.SalesTransactionLifecycle);
            Assert.Null(endpoint.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());
        }

        Assert.Equal(SalesWorkspaceEndpoints.ListSalesOperationId, list.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Equal(SalesWorkspaceEndpoints.GetSalesWorkspaceOperationId, detail.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
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
            ["Api:Localization:DefaultLocale"] = "zh-TW",
        });
        builder.AddYowThiApi();
        return builder.Build();
    }

    private static RouteEndpoint[] GetRouteEndpoints(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
}
