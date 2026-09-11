using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Hosting;
using YowThi.Erp.Api.Infrastructure;
using YowThi.Erp.Api.Party;
using YowThi.Erp.Api.Product;
using YowThi.Erp.Api.SalesHandling;

namespace YowThi.Erp.Api.ContractTests;

public sealed class MasterDeletionReauthenticationEndpointContractTests
{
    [Fact]
    public async Task Master_soft_delete_routes_require_recent_reauthentication_while_restore_does_not()
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

        await using var app = builder.Build();
        app.MapPartyEndpoints();
        app.MapFarmerLifecycleEndpoints();
        app.MapEmployeeLifecycleEndpoints();
        app.MapContainerLifecycleEndpoints();
        app.MapWarehouseLifecycleEndpoints();
        app.MapSalesProductGroupLifecycleEndpoints();
        app.MapSalesPackagingItemLifecycleEndpoints();

        var routePairs = new (string SoftDelete, string Restore)[]
        {
            ("/api/v1/party/suppliers/{supplierId:guid}/soft-delete", "/api/v1/party/suppliers/{supplierId:guid}/restore"),
            ("/api/v1/party/customers/{customerId:guid}/soft-delete", "/api/v1/party/customers/{customerId:guid}/restore"),
            ("/api/v1/party/outsourced-vendors/{outsourcedVendorId:guid}/soft-delete", "/api/v1/party/outsourced-vendors/{outsourcedVendorId:guid}/restore"),
            ("/api/v1/party/farmers/{farmerId:guid}/soft-delete", "/api/v1/party/farmers/{farmerId:guid}/restore"),
            ("/api/v1/party/employees/{employeeId:guid}/soft-delete", "/api/v1/party/employees/{employeeId:guid}/restore"),
            ("/api/v1/infrastructure/containers/{containerId:guid}/soft-delete", "/api/v1/infrastructure/containers/{containerId:guid}/restore"),
            ("/api/v1/infrastructure/warehouses/{warehouseId:guid}/soft-delete", "/api/v1/infrastructure/warehouses/{warehouseId:guid}/restore"),
            ("/api/v1/product/sales-product-groups/{salesProductGroupId:guid}/soft-delete", "/api/v1/product/sales-product-groups/{salesProductGroupId:guid}/restore"),
            ("/api/v1/sales-handling/packaging-items/{salesPackagingItemId:guid}/soft-delete", "/api/v1/sales-handling/packaging-items/{salesPackagingItemId:guid}/restore"),
        };

        foreach (var (softDeleteRoute, restoreRoute) in routePairs)
        {
            Assert.NotNull(GetEndpoint(app, softDeleteRoute).Metadata.GetMetadata<RequiresDeletionReauthenticationMetadata>());
            Assert.Null(GetEndpoint(app, restoreRoute).Metadata.GetMetadata<RequiresDeletionReauthenticationMetadata>());
        }
    }

    private static RouteEndpoint GetEndpoint(WebApplication app, string route) =>
        Assert.Single(
            ((IEndpointRouteBuilder)app).DataSources
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>(),
            endpoint => endpoint.RoutePattern.RawText == route);
}
