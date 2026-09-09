using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Hosting;
using YowThi.Erp.Api.Idempotency;
using YowThi.Erp.Api.Outsourced;

namespace YowThi.Erp.Api.ContractTests;

public sealed class OutsourcedTransactionLifecycleEndpointContractTests
{
    [Fact]
    public async Task Outsourced_transaction_lifecycle_routes_are_explicit_idempotent_and_guarded()
    {
        await using var app = CreateApp();
        app.MapOutsourcedTransactionLifecycleEndpoints();
        var endpoints = GetRouteEndpoints(app);

        var batchSoftDelete = AssertRoute(endpoints, "/api/v1/outsourced/batches/{outsourcedSupplyBatchId:guid}/soft-delete");
        var batchRestore = AssertRoute(endpoints, "/api/v1/outsourced/batches/{outsourcedSupplyBatchId:guid}/restore");
        var batchHardDelete = AssertRoute(endpoints, "/api/v1/outsourced/batches/{outsourcedSupplyBatchId:guid}/hard-delete");
        var detailSoftDelete = AssertRoute(endpoints, "/api/v1/outsourced/supply-details/{outsourcedSupplyDetailId:guid}/soft-delete");
        var detailRestore = AssertRoute(endpoints, "/api/v1/outsourced/supply-details/{outsourcedSupplyDetailId:guid}/restore");
        var detailHardDelete = AssertRoute(endpoints, "/api/v1/outsourced/supply-details/{outsourcedSupplyDetailId:guid}/hard-delete");

        AssertLifecycleWrite(batchSoftDelete, requiresReauthentication: true);
        AssertLifecycleWrite(batchRestore, requiresReauthentication: false);
        AssertLifecycleWrite(batchHardDelete, requiresReauthentication: true);
        AssertPolicy(batchHardDelete, CapabilityPolicies.DataProtectionHardDelete);

        AssertLifecycleWrite(detailSoftDelete, requiresReauthentication: true);
        AssertLifecycleWrite(detailRestore, requiresReauthentication: false);
        AssertLifecycleWrite(detailHardDelete, requiresReauthentication: true);
        AssertPolicy(detailHardDelete, CapabilityPolicies.DataProtectionHardDelete);
    }

    [Fact]
    public async Task Outsourced_workspace_routes_are_read_only_and_use_existing_module_access_capability()
    {
        await using var app = CreateApp();
        app.MapOutsourcedWorkspaceEndpoints();
        var endpoints = GetRouteEndpoints(app);

        var list = AssertRoute(endpoints, "/api/v1/outsourced/workspace/");
        var detail = AssertRoute(endpoints, "/api/v1/outsourced/workspace/{outsourcedSupplyBatchId:guid}");

        AssertRead(list);
        AssertRead(detail);
        Assert.Equal(OutsourcedWorkspaceEndpoints.ListBatchesOperationId, list.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Equal(OutsourcedWorkspaceEndpoints.GetBatchOperationId, detail.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
    }

    private static void AssertLifecycleWrite(RouteEndpoint endpoint, bool requiresReauthentication)
    {
        Assert.Contains(HttpMethods.Post, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        AssertPolicy(endpoint, CapabilityPolicies.OutsourcedTransactionLifecycle);
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

    private static void AssertRead(RouteEndpoint endpoint)
    {
        Assert.Contains(HttpMethods.Get, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        AssertPolicy(endpoint, CapabilityPolicies.OutsourcedConfirm);
        Assert.Null(endpoint.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());
        Assert.Null(endpoint.Metadata.GetMetadata<RequiresDeletionReauthenticationMetadata>());
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
