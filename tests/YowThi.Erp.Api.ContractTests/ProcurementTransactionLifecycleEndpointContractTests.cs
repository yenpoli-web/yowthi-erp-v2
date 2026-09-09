using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Hosting;
using YowThi.Erp.Api.Idempotency;
using YowThi.Erp.Api.Procurement;

namespace YowThi.Erp.Api.ContractTests;

public sealed class ProcurementTransactionLifecycleEndpointContractTests
{
    [Fact]
    public async Task Procurement_transaction_lifecycle_routes_are_explicit_idempotent_and_guarded()
    {
        await using var app = CreateApp();
        app.MapProcurementTransactionLifecycleEndpoints();
        var endpoints = GetRouteEndpoints(app);

        var batchSoftDelete = AssertRoute(endpoints, "/api/v1/procurement/batches/{procurementBatchId:guid}/soft-delete");
        var batchRestore = AssertRoute(endpoints, "/api/v1/procurement/batches/{procurementBatchId:guid}/restore");
        var entrySoftDelete = AssertRoute(endpoints, "/api/v1/procurement/entries/{procurementEntryId:guid}/soft-delete");
        var entryRestore = AssertRoute(endpoints, "/api/v1/procurement/entries/{procurementEntryId:guid}/restore");
        var entryHardDelete = AssertRoute(endpoints, "/api/v1/procurement/entries/{procurementEntryId:guid}/hard-delete");

        AssertLifecycleWrite(batchSoftDelete, requiresReauthentication: true);
        AssertLifecycleWrite(batchRestore, requiresReauthentication: false);
        AssertLifecycleWrite(entrySoftDelete, requiresReauthentication: true);
        AssertLifecycleWrite(entryRestore, requiresReauthentication: false);
        AssertLifecycleWrite(entryHardDelete, requiresReauthentication: true);

        AssertPolicy(entryHardDelete, CapabilityPolicies.DataProtectionHardDelete);
    }

    private static void AssertLifecycleWrite(RouteEndpoint endpoint, bool requiresReauthentication)
    {
        Assert.Contains(HttpMethods.Post, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        AssertPolicy(endpoint, CapabilityPolicies.ProcurementTransactionLifecycle);
        Assert.NotNull(endpoint.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());

        if (requiresReauthentication)
        {
            Assert.NotNull(endpoint.Metadata.GetMetadata<RequiresDeletionReauthenticationMetadata>());
        }
        else
        {
            Assert.Null(endpoint.Metadata.GetMetadata<RequiresDeletionReauthenticationMetadata>());
        }
    }

    private static RouteEndpoint AssertRoute(IEnumerable<RouteEndpoint> endpoints, string route) =>
        Assert.Single(endpoints, endpoint => endpoint.RoutePattern.RawText == route);

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
