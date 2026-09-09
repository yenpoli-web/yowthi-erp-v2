using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Api.Idempotency;
using YowThi.Erp.Api.Localization;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Serialization;
using YowThi.Erp.Application.Product;
using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Api.Product;

public static class SalesProductMasterEndpoints
{
    public const string ListOperationId = "Product_ListSalesProducts";
    public const string CreateOperationId = "Product_CreateSalesProduct";
    public const string UpdateOperationId = "Product_UpdateSalesProduct";
    public const string CreateCommandType = "CreateSalesProduct";
    public const string UpdateCommandType = "UpdateSalesProduct";
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapSalesProductMasterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var product = endpoints.MapApiV1().MapGroup("/product");

        product.MapGet("/sales-products", ListAsync)
            .WithName(ListOperationId)
            .RequireAuthorization(CapabilityPolicies.SalesProductManage)
            .Produces<SalesProductMasterResponsePage>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        product.MapPost("/sales-products", CreateAsync)
            .WithName(CreateOperationId)
            .RequireAuthorization(CapabilityPolicies.SalesProductManage)
            .RequireIdempotencyKey()
            .Produces<SalesProductMasterWriteResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        product.MapPost("/sales-products/{salesProductId:guid}/update", UpdateAsync)
            .WithName(UpdateOperationId)
            .RequireAuthorization(CapabilityPolicies.SalesProductManage)
            .RequireIdempotencyKey()
            .Produces<SalesProductMasterWriteResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        HttpContext httpContext,
        [FromServices] IApiLocaleResolver localeResolver,
        [FromServices] ISalesProductMasterReader reader,
        string? search,
        string? status,
        int offset = 0,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0 || limit is < 1 or > 200 || !ProductMasterEndpointSupport.TryParseStatus(status, out var parsedStatus))
        {
            return TypedResults.BadRequest();
        }

        var page = await reader.GetAsync(
            new ProductMasterQuery(search, parsedStatus, offset, limit, localeResolver.Resolve(httpContext.Request)),
            cancellationToken);
        return TypedResults.Ok(new SalesProductMasterResponsePage(
            page.Items.Select(ToResponse).ToArray(),
            page.NextOffset));
    }

    private static async Task<IResult> CreateAsync(
        CreateSalesProductRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ISalesProductMasterExecutor executor,
        CancellationToken cancellationToken)
    {
        if (!TryParsePricingBasis(request.PricingBasis, out var pricingBasis))
        {
            return ApiProblemResults.Create(httpContext, StatusCodes.Status422UnprocessableEntity,
                SalesProductMasterErrorCodes.InvalidInput, "Sales Product request is invalid.");
        }

        var command = new CreateSalesProductCommand(
            request.SalesProductGroupId,
            ProductMasterEndpointSupport.Normalize(request.NameZhTw),
            ProductMasterEndpointSupport.Normalize(request.NameThTh),
            pricingBasis,
            request.PackagingWeight,
            request.SalesWeight,
            request.DefaultStorageLocationId,
            request.Active);
        var payload = JsonPayload.FromUtf8Json(JsonSerializer.SerializeToUtf8Bytes(
            new CanonicalCreateRequest(CreateCommandType, command), CanonicalJsonOptions));
        var execution = new CreateSalesProductExecution(
            httpContext.GetRequiredCommandId(), requestHasher.Compute(payload), actorContext.ActorAccountId, command);
        var result = await executor.CreateAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : ProductMasterEndpointSupport.Failure(httpContext, result.Error, "Sales Product create failed.");
    }

    private static async Task<IResult> UpdateAsync(
        Guid salesProductId,
        UpdateSalesProductRequest request,
        HttpContext httpContext,
        [FromServices] IActorContext actorContext,
        [FromServices] ICommandRequestHasher requestHasher,
        [FromServices] ISalesProductMasterExecutor executor,
        CancellationToken cancellationToken)
    {
        if (!TryParsePricingBasis(request.PricingBasis, out var pricingBasis))
        {
            return ApiProblemResults.Create(httpContext, StatusCodes.Status422UnprocessableEntity,
                SalesProductMasterErrorCodes.InvalidInput, "Sales Product request is invalid.");
        }

        var command = new UpdateSalesProductCommand(
            salesProductId,
            request.ExpectedRowVersion,
            request.SalesProductGroupId,
            ProductMasterEndpointSupport.Normalize(request.NameZhTw),
            ProductMasterEndpointSupport.Normalize(request.NameThTh),
            pricingBasis,
            request.PackagingWeight,
            request.SalesWeight,
            request.DefaultStorageLocationId,
            request.Active);
        var payload = JsonPayload.FromUtf8Json(JsonSerializer.SerializeToUtf8Bytes(
            new CanonicalUpdateRequest(UpdateCommandType, command), CanonicalJsonOptions));
        var execution = new UpdateSalesProductExecution(
            httpContext.GetRequiredCommandId(), requestHasher.Compute(payload), actorContext.ActorAccountId, command);
        var result = await executor.UpdateAsync(execution, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : ProductMasterEndpointSupport.Failure(httpContext, result.Error, "Sales Product update failed.");
    }

    private static bool TryParsePricingBasis(string? value, out SalesPricingBasis pricingBasis) =>
        Enum.TryParse(value?.Trim(), ignoreCase: true, out pricingBasis) && Enum.IsDefined(pricingBasis);

    private static SalesProductMasterResponse ToResponse(SalesProductMasterItem item) => new(
        item.Id,
        item.SalesProductGroupId,
        item.SalesProductGroupDisplayName,
        item.NameZhTw,
        item.NameThTh,
        item.PricingBasis.ToString(),
        item.PackagingWeight,
        item.SalesWeight,
        item.DefaultStorageLocationId,
        item.DefaultStorageLocationDisplayName,
        item.Active,
        item.RowVersion,
        item.CreatedAt,
        item.DeletedAt);

    private sealed record CanonicalCreateRequest(string CommandType, CreateSalesProductCommand Command);
    private sealed record CanonicalUpdateRequest(string CommandType, UpdateSalesProductCommand Command);
}

public sealed record CreateSalesProductRequest(
    Guid SalesProductGroupId,
    string? NameZhTw,
    string? NameThTh,
    string? PricingBasis,
    decimal? PackagingWeight,
    decimal? SalesWeight,
    Guid? DefaultStorageLocationId,
    bool Active);

public sealed record UpdateSalesProductRequest(
    long ExpectedRowVersion,
    Guid SalesProductGroupId,
    string? NameZhTw,
    string? NameThTh,
    string? PricingBasis,
    decimal? PackagingWeight,
    decimal? SalesWeight,
    Guid? DefaultStorageLocationId,
    bool Active);

public sealed record SalesProductMasterResponse(
    Guid Id,
    Guid SalesProductGroupId,
    string SalesProductGroupDisplayName,
    string? NameZhTw,
    string? NameThTh,
    string PricingBasis,
    decimal? PackagingWeight,
    decimal? SalesWeight,
    Guid? DefaultStorageLocationId,
    string? DefaultStorageLocationDisplayName,
    bool Active,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt);

public sealed record SalesProductMasterResponsePage(
    IReadOnlyList<SalesProductMasterResponse> Items,
    int? NextOffset);
