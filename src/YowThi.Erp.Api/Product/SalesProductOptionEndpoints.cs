using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Localization;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.Product;

namespace YowThi.Erp.Api.Product;

public static class SalesProductOptionEndpoints
{
    public static IEndpointRouteBuilder MapSalesProductOptionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var product = endpoints.MapApiV1().MapGroup("/product");

        product.MapGet("/sales-products/storage-location-options", GetStorageLocationsAsync)
            .WithName("Product_ListSalesProductStorageLocationOptions")
            .RequireAuthorization(CapabilityPolicies.SalesProductManage)
            .Produces<ProductMasterStorageLocationOptions>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        product.MapGet("/sales-products/group-options", GetGroupsAsync)
            .WithName("Product_ListSalesProductGroupOptions")
            .RequireAuthorization(CapabilityPolicies.SalesProductManage)
            .Produces<SalesProductMasterGroupOptions>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return endpoints;
    }

    private static async Task<IResult> GetStorageLocationsAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] IProductMasterOptionsReader reader,
        string? search,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 200)
        {
            return TypedResults.BadRequest();
        }

        return TypedResults.Ok(await reader.GetStorageLocationsAsync(
            localeResolver.Resolve(httpContext.Request), search, limit, cancellationToken));
    }

    private static async Task<IResult> GetGroupsAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] ISalesProductMasterOptionsReader reader,
        string? search,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 200)
        {
            return TypedResults.BadRequest();
        }

        return TypedResults.Ok(await reader.GetGroupsAsync(
            localeResolver.Resolve(httpContext.Request), search, limit, cancellationToken));
    }
}
