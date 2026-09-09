using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Localization;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.Product;

namespace YowThi.Erp.Api.Product;

public static class ProcurementProductOptionEndpoints
{
    public static IEndpointRouteBuilder MapProcurementProductOptionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapApiV1().MapGroup("/product")
            .MapGet("/procurement-products/storage-location-options", GetStorageLocationsAsync)
            .WithName("Product_ListProcurementProductStorageLocationOptions")
            .RequireAuthorization(CapabilityPolicies.ProcurementProductManage)
            .Produces<ProductMasterStorageLocationOptions>(StatusCodes.Status200OK)
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
}
