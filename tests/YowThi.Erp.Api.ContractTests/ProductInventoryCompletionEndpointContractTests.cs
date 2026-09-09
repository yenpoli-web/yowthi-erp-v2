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
using YowThi.Erp.Api.Inventory;
using YowThi.Erp.Api.Product;

namespace YowThi.Erp.Api.ContractTests;

public sealed class ProductInventoryCompletionEndpointContractTests
{
    [Fact]
    public async Task Product_and_inventory_completion_routes_have_target_specific_capabilities_and_idempotent_writes()
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

        app.MapProcurementProductMasterEndpoints();
        app.MapProcurementProductOptionEndpoints();
        app.MapSalesProductMasterEndpoints();
        app.MapSalesProductOptionEndpoints();
        app.MapInventoryPositionEndpoints();
        app.MapWarehouseMasterEndpoints();
        app.MapStorageLocationMasterEndpoints();

        AssertRead(app, "/api/v1/product/procurement-products", CapabilityPolicies.ProcurementProductManage, ProcurementProductMasterEndpoints.ListOperationId);
        AssertWrite(app, "/api/v1/product/procurement-products", CapabilityPolicies.ProcurementProductManage, ProcurementProductMasterEndpoints.CreateOperationId);
        AssertWrite(app, "/api/v1/product/procurement-products/{procurementProductId:guid}/update", CapabilityPolicies.ProcurementProductManage, ProcurementProductMasterEndpoints.UpdateOperationId);
        AssertRead(app, "/api/v1/product/procurement-products/storage-location-options", CapabilityPolicies.ProcurementProductManage, "Product_ListProcurementProductStorageLocationOptions");

        AssertRead(app, "/api/v1/product/sales-products", CapabilityPolicies.SalesProductManage, SalesProductMasterEndpoints.ListOperationId);
        AssertWrite(app, "/api/v1/product/sales-products", CapabilityPolicies.SalesProductManage, SalesProductMasterEndpoints.CreateOperationId);
        AssertWrite(app, "/api/v1/product/sales-products/{salesProductId:guid}/update", CapabilityPolicies.SalesProductManage, SalesProductMasterEndpoints.UpdateOperationId);
        AssertRead(app, "/api/v1/product/sales-products/storage-location-options", CapabilityPolicies.SalesProductManage, "Product_ListSalesProductStorageLocationOptions");
        AssertRead(app, "/api/v1/product/sales-products/group-options", CapabilityPolicies.SalesProductManage, "Product_ListSalesProductGroupOptions");

        AssertRead(app, "/api/v1/inventory/positions", CapabilityPolicies.InventoryView, "Inventory_ListPositions");

        AssertRead(app, "/api/v1/infrastructure/warehouses", CapabilityPolicies.InfrastructureWarehouseManage, "Infrastructure_ListWarehouses");
        AssertWrite(app, "/api/v1/infrastructure/warehouses", CapabilityPolicies.InfrastructureWarehouseManage, "Infrastructure_CreateWarehouse");
        AssertWrite(app, "/api/v1/infrastructure/warehouses/{warehouseId:guid}/update", CapabilityPolicies.InfrastructureWarehouseManage, "Infrastructure_UpdateWarehouse");

        AssertRead(app, "/api/v1/infrastructure/storage-locations", CapabilityPolicies.InfrastructureStorageLocationManage, "Infrastructure_ListStorageLocations");
        AssertRead(app, "/api/v1/infrastructure/storage-locations/warehouse-options", CapabilityPolicies.InfrastructureStorageLocationManage, "Infrastructure_ListStorageLocationWarehouseOptions");
        AssertWrite(app, "/api/v1/infrastructure/storage-locations", CapabilityPolicies.InfrastructureStorageLocationManage, "Infrastructure_CreateStorageLocation");
        AssertWrite(app, "/api/v1/infrastructure/storage-locations/{storageLocationId:guid}/update", CapabilityPolicies.InfrastructureStorageLocationManage, "Infrastructure_UpdateStorageLocation");
    }

    [Fact]
    public void New_capabilities_are_known_security_capabilities()
    {
        Assert.True(YowThi.Erp.Application.Security.SecurityCapabilities.IsKnown(CapabilityPolicies.ProcurementProductManage));
        Assert.True(YowThi.Erp.Application.Security.SecurityCapabilities.IsKnown(CapabilityPolicies.SalesProductManage));
        Assert.True(YowThi.Erp.Application.Security.SecurityCapabilities.IsKnown(CapabilityPolicies.InventoryView));
        Assert.True(YowThi.Erp.Application.Security.SecurityCapabilities.IsKnown(CapabilityPolicies.InfrastructureWarehouseManage));
        Assert.True(YowThi.Erp.Application.Security.SecurityCapabilities.IsKnown(CapabilityPolicies.InfrastructureStorageLocationManage));
    }

    private static void AssertRead(WebApplication app, string route, string policy, string operationId)
    {
        var endpoint = GetEndpoint(app, route, HttpMethods.Get);
        Assert.Equal(operationId, endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        AssertPolicy(endpoint, policy);
        Assert.Null(endpoint.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());
    }

    private static void AssertWrite(WebApplication app, string route, string policy, string operationId)
    {
        var endpoint = GetEndpoint(app, route, HttpMethods.Post);
        Assert.Equal(operationId, endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        AssertPolicy(endpoint, policy);
        Assert.NotNull(endpoint.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());
    }

    private static void AssertPolicy(RouteEndpoint endpoint, string policy) =>
        Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(), metadata => metadata.Policy == policy);

    private static RouteEndpoint GetEndpoint(WebApplication app, string route, string method) =>
        Assert.Single(
            ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>(),
            endpoint => endpoint.RoutePattern.RawText == route
                && (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(method) ?? false));
}
