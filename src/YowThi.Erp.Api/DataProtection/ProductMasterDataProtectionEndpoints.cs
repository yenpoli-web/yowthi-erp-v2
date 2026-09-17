using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Api.Idempotency;
using YowThi.Erp.Api.Localization;
using YowThi.Erp.Api.Routing;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Serialization;
using YowThi.Erp.Application.DataProtection;

namespace YowThi.Erp.Api.DataProtection;

public static class ProductMasterDataProtectionEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapProductMasterDataProtectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var dataProtection = endpoints.MapApiV1().MapGroup("/data-protection");
        MapHardDelete(dataProtection, "/procurement-products/{id:guid}/hard-delete", "DataProtection_HardDeleteProcurementProduct", HardDeleteProcurementProductAsync);
        MapHardDelete(dataProtection, "/sales-products/{id:guid}/hard-delete", "DataProtection_HardDeleteSalesProduct", HardDeleteSalesProductAsync);
        MapHardDelete(dataProtection, "/sales-product-groups/{id:guid}/hard-delete", "DataProtection_HardDeleteSalesProductGroup", HardDeleteSalesProductGroupAsync);

        var options = dataProtection.MapGroup("/hard-delete-options");
        MapOptions(options, "/procurement-products", "DataProtection_ListHardDeleteProcurementProducts", (reader, query, ct) => reader.GetProcurementProductsAsync(query, ct));
        MapOptions(options, "/sales-products", "DataProtection_ListHardDeleteSalesProducts", (reader, query, ct) => reader.GetSalesProductsAsync(query, ct));
        MapOptions(options, "/sales-product-groups", "DataProtection_ListHardDeleteSalesProductGroups", (reader, query, ct) => reader.GetSalesProductGroupsAsync(query, ct));
        return endpoints;
    }

    private static void MapHardDelete(RouteGroupBuilder group, string path, string operationId, Delegate handler) =>
        group.MapPost(path, handler)
            .WithName(operationId)
            .RequireAuthorization(CapabilityPolicies.DataProtectionHardDelete)
            .RequireIdempotencyKey()
            .RequireRecentDeletionReauthentication()
            .Produces<HardDeleteProductMasterResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

    private static async Task<IResult> HardDeleteProcurementProductAsync(Guid id, ProductHardDeleteRequest request, HttpContext context,
        [FromServices] IActorContext actor, [FromServices] ICommandRequestHasher hasher, [FromServices] IHardDeleteProcurementProductExecutor executor, CancellationToken ct) =>
        ToResult(context, await executor.ExecuteAsync(CreateExecution(id, request, context, actor, hasher, "HardDeleteProcurementProduct"), ct), "Procurement Product hard delete failed.");

    private static async Task<IResult> HardDeleteSalesProductAsync(Guid id, ProductHardDeleteRequest request, HttpContext context,
        [FromServices] IActorContext actor, [FromServices] ICommandRequestHasher hasher, [FromServices] IHardDeleteSalesProductExecutor executor, CancellationToken ct) =>
        ToResult(context, await executor.ExecuteAsync(CreateExecution(id, request, context, actor, hasher, "HardDeleteSalesProduct"), ct), "Sales Product hard delete failed.");

    private static async Task<IResult> HardDeleteSalesProductGroupAsync(Guid id, ProductHardDeleteRequest request, HttpContext context,
        [FromServices] IActorContext actor, [FromServices] ICommandRequestHasher hasher, [FromServices] IHardDeleteSalesProductGroupExecutor executor, CancellationToken ct) =>
        ToResult(context, await executor.ExecuteAsync(CreateExecution(id, request, context, actor, hasher, "HardDeleteSalesProductGroup"), ct), "Sales Product Group hard delete failed.");

    private static HardDeleteProductMasterExecution CreateExecution(Guid id, ProductHardDeleteRequest request, HttpContext context, IActorContext actor, ICommandRequestHasher hasher, string commandType)
    {
        var command = new HardDeleteProductMasterCommand(id, request.ExpectedRowVersion);
        var payload = JsonPayload.FromUtf8Json(JsonSerializer.SerializeToUtf8Bytes(new CanonicalRequest(commandType, command), JsonOptions));
        return new HardDeleteProductMasterExecution(context.GetRequiredCommandId(), hasher.Compute(payload), actor.ActorAccountId, command);
    }

    private static IResult ToResult(HttpContext context, Application.Common.Results.ApplicationResult<HardDeleteProductMasterResult> result, string title)
    {
        if (result.IsSuccess) return TypedResults.Ok(result.Value);
        var status = result.Error.Kind switch
        {
            ApplicationErrorKind.Validation => StatusCodes.Status422UnprocessableEntity,
            ApplicationErrorKind.NotFound => StatusCodes.Status404NotFound,
            ApplicationErrorKind.Conflict => StatusCodes.Status409Conflict,
            ApplicationErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            _ => throw new ArgumentOutOfRangeException(nameof(result.Error.Kind)),
        };
        return ApiProblemResults.Create(context, status, result.Error.Code, title);
    }

    private static void MapOptions(RouteGroupBuilder group, string path, string operationId,
        Func<IProductHardDeleteOptionsReader, HardDeleteOptionsQuery, CancellationToken, ValueTask<HardDeleteOptionPage<HardDeleteOption>>> read)
    {
        group.MapGet(path, async (HttpContext context, [FromServices] IApiLocaleResolver localeResolver, [FromServices] IProductHardDeleteOptionsReader reader, CancellationToken ct) =>
        {
            if (!TryCreateQuery(context, localeResolver, out var query)) return ApiProblemResults.Create(context, 400, ApiErrorCodes.RequestValidationFailed, "Request validation failed.");
            var page = await read(reader, query!, ct);
            return TypedResults.Ok(new HardDeleteOptionsResponse(page.Items.Select(item => new HardDeleteOptionResponse(item.Id, item.DisplayName, item.Active, item.RowVersion, item.Deleted, item.DeletedAt)).ToArray(), EncodeCursor(page.NextOffset)));
        }).WithName(operationId).RequireAuthorization(CapabilityPolicies.DataProtectionHardDelete)
          .Produces<HardDeleteOptionsResponse>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status400BadRequest)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static bool TryCreateQuery(HttpContext context, IApiLocaleResolver localeResolver, out HardDeleteOptionsQuery? query)
    {
        var search = context.Request.Query["search"];
        var limits = context.Request.Query["limit"];
        var cursors = context.Request.Query["cursor"];
        if (search.Count > 1 || limits.Count > 1 || cursors.Count > 1) { query = null; return false; }
        var limit = 50;
        if (limits.Count == 1 && (!int.TryParse(limits[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 100)) { query = null; return false; }
        if (!TryDecodeCursor(cursors.Count == 1 ? cursors[0] : null, out var offset)) { query = null; return false; }
        query = new HardDeleteOptionsQuery(localeResolver.Resolve(context.Request), search.Count == 1 && !string.IsNullOrWhiteSpace(search[0]) ? search[0]!.Trim() : null, offset, limit);
        return true;
    }

    private static bool TryDecodeCursor(string? cursor, out int offset)
    {
        offset = 0;
        if (string.IsNullOrWhiteSpace(cursor)) return true;
        try
        {
            var value = cursor.Replace('-', '+').Replace('_', '/');
            value = value.PadRight(value.Length + ((4 - value.Length % 4) % 4), '=');
            return int.TryParse(Encoding.UTF8.GetString(Convert.FromBase64String(value)), NumberStyles.None, CultureInfo.InvariantCulture, out offset) && offset >= 0;
        }
        catch (FormatException) { return false; }
    }

    private static string? EncodeCursor(int? offset) => offset is null ? null : Convert.ToBase64String(Encoding.UTF8.GetBytes(offset.Value.ToString(CultureInfo.InvariantCulture))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record CanonicalRequest(string CommandType, HardDeleteProductMasterCommand Command);
}

public sealed record ProductHardDeleteRequest(long ExpectedRowVersion);