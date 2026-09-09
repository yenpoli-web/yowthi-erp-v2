using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Hosting;
using YowThi.Erp.Api.Idempotency;
using YowThi.Erp.Api.Sales;
using YowThi.Erp.Application.Security;

namespace YowThi.Erp.Api.ContractTests;

public sealed class SalesTransactionLifecycleEndpointContractTests
{
    [Fact]
    public async Task Sales_transaction_lifecycle_routes_are_explicit_idempotent_and_guarded()
    {
        await using var app = CreateApp();
        app.MapSalesTransactionLifecycleEndpoints();
        var endpoints = GetRouteEndpoints(app);

        var routes = new[]
        {
            ("/api/v1/sales/{salesId:guid}/soft-delete", true, false),
            ("/api/v1/sales/{salesId:guid}/restore", false, false),
            ("/api/v1/sales/{salesId:guid}/hard-delete", true, true),
            ("/api/v1/sales/details/{salesDetailId:guid}/soft-delete", true, false),
            ("/api/v1/sales/details/{salesDetailId:guid}/restore", false, false),
            ("/api/v1/sales/details/{salesDetailId:guid}/hard-delete", true, true),
        };

        foreach (var (route, requiresReauthentication, hardDelete) in routes)
        {
            var endpoint = Assert.Single(endpoints, candidate => candidate.RoutePattern.RawText == route);
            Assert.Contains(HttpMethods.Post, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
            AssertPolicy(endpoint, CapabilityPolicies.SalesTransactionLifecycle);
            Assert.NotNull(endpoint.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());
            Assert.Equal(
                requiresReauthentication,
                endpoint.Metadata.GetMetadata<RequiresDeletionReauthenticationMetadata>() is not null);

            if (hardDelete)
            {
                AssertPolicy(endpoint, CapabilityPolicies.DataProtectionHardDelete);
            }
        }

        Assert.Contains(SecurityCapabilities.SalesTransactionLifecycle, SecurityCapabilities.All);
    }

    private static void AssertPolicy(RouteEndpoint endpoint, string policy) =>
        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            metadata => metadata.Policy == policy);

    private static WebApplication CreateApp()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = "Development",
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
