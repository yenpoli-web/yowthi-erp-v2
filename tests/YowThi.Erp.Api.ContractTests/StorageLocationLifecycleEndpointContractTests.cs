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
using YowThi.Erp.Api.Infrastructure;

namespace YowThi.Erp.Api.ContractTests;

public sealed class StorageLocationLifecycleEndpointContractTests
{
    private const string SoftDeleteRoute = "/api/v1/infrastructure/storage-locations/{storageLocationId:guid}/soft-delete";
    private const string RestoreRoute = "/api/v1/infrastructure/storage-locations/{storageLocationId:guid}/restore";

    [Fact]
    public async Task Storage_location_lifecycle_routes_are_authorized_idempotent_and_apply_reauth_only_to_soft_delete()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = Environments.Development,
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Localization:DefaultLocale"] = "zh-TW",
        });
        builder.AddYowThiApi();
        await using var app = builder.Build();
        app.MapStorageLocationMasterEndpoints();

        var softDelete = GetEndpoint(app, SoftDeleteRoute);
        var restore = GetEndpoint(app, RestoreRoute);

        AssertLifecycleEndpoint(softDelete, "Infrastructure_SoftDeleteStorageLocation");
        AssertLifecycleEndpoint(restore, "Infrastructure_RestoreStorageLocation");
        Assert.NotNull(softDelete.Metadata.GetMetadata<RequiresDeletionReauthenticationMetadata>());
        Assert.Null(restore.Metadata.GetMetadata<RequiresDeletionReauthenticationMetadata>());
    }

    private static void AssertLifecycleEndpoint(RouteEndpoint endpoint, string operationId)
    {
        Assert.Contains(HttpMethods.Post, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        Assert.Equal(operationId, endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            metadata => metadata.Policy == CapabilityPolicies.InfrastructureStorageLocationManage);
        Assert.NotNull(endpoint.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());
    }

    private static RouteEndpoint GetEndpoint(WebApplication app, string route) =>
        Assert.Single(
            ((IEndpointRouteBuilder)app).DataSources
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>(),
            endpoint => endpoint.RoutePattern.RawText == route);
}
