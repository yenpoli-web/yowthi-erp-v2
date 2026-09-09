using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Application.Product;

public enum ProductMasterStatusFilter
{
    All,
    Active,
    Inactive,
    Deleted,
}

public sealed record ProductMasterQuery(string? Search, ProductMasterStatusFilter Status, int Offset, int Limit, string Locale = "zh-TW");

public sealed record ProcurementProductMasterItem(
    Guid Id,
    string? NameZhTw,
    string? NameThTh,
    string UnitCode,
    Guid? DefaultStorageLocationId,
    string? DefaultStorageLocationDisplayName,
    bool Active,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt);

public sealed record ProcurementProductMasterPage(IReadOnlyList<ProcurementProductMasterItem> Items, int? NextOffset);

public sealed record CreateProcurementProductCommand(
    string? NameZhTw,
    string? NameThTh,
    string UnitCode,
    Guid? DefaultStorageLocationId,
    bool Active);

public sealed record UpdateProcurementProductCommand(
    Guid ProcurementProductId,
    long ExpectedRowVersion,
    string? NameZhTw,
    string? NameThTh,
    string UnitCode,
    Guid? DefaultStorageLocationId,
    bool Active);

public sealed record CreateProcurementProductExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    CreateProcurementProductCommand Command);

public sealed record UpdateProcurementProductExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    UpdateProcurementProductCommand Command);

public sealed record ProcurementProductMasterWriteResult(Guid ProcurementProductId, long RowVersion);

public interface IProcurementProductMasterReader
{
    ValueTask<ProcurementProductMasterPage> GetAsync(ProductMasterQuery query, CancellationToken cancellationToken);
}

public interface IProcurementProductMasterExecutor
{
    ValueTask<ApplicationResult<ProcurementProductMasterWriteResult>> CreateAsync(CreateProcurementProductExecution execution, CancellationToken cancellationToken);
    ValueTask<ApplicationResult<ProcurementProductMasterWriteResult>> UpdateAsync(UpdateProcurementProductExecution execution, CancellationToken cancellationToken);
}

public sealed record SalesProductMasterItem(
    Guid Id,
    Guid SalesProductGroupId,
    string SalesProductGroupDisplayName,
    string? NameZhTw,
    string? NameThTh,
    SalesPricingBasis PricingBasis,
    decimal? PackagingWeight,
    decimal? SalesWeight,
    Guid? DefaultStorageLocationId,
    string? DefaultStorageLocationDisplayName,
    bool Active,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt);

public sealed record SalesProductMasterPage(IReadOnlyList<SalesProductMasterItem> Items, int? NextOffset);

public sealed record CreateSalesProductCommand(
    Guid SalesProductGroupId,
    string? NameZhTw,
    string? NameThTh,
    SalesPricingBasis PricingBasis,
    decimal? PackagingWeight,
    decimal? SalesWeight,
    Guid? DefaultStorageLocationId,
    bool Active);

public sealed record UpdateSalesProductCommand(
    Guid SalesProductId,
    long ExpectedRowVersion,
    Guid SalesProductGroupId,
    string? NameZhTw,
    string? NameThTh,
    SalesPricingBasis PricingBasis,
    decimal? PackagingWeight,
    decimal? SalesWeight,
    Guid? DefaultStorageLocationId,
    bool Active);

public sealed record CreateSalesProductExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    CreateSalesProductCommand Command);

public sealed record UpdateSalesProductExecution(
    CommandId CommandId,
    CommandRequestHash RequestHash,
    ActorAccountId ActorAccountId,
    UpdateSalesProductCommand Command);

public sealed record SalesProductMasterWriteResult(Guid SalesProductId, long RowVersion);

public interface ISalesProductMasterReader
{
    ValueTask<SalesProductMasterPage> GetAsync(ProductMasterQuery query, CancellationToken cancellationToken);
}

public interface ISalesProductMasterExecutor
{
    ValueTask<ApplicationResult<SalesProductMasterWriteResult>> CreateAsync(CreateSalesProductExecution execution, CancellationToken cancellationToken);
    ValueTask<ApplicationResult<SalesProductMasterWriteResult>> UpdateAsync(UpdateSalesProductExecution execution, CancellationToken cancellationToken);
}

public sealed record ProductMasterStorageLocationOption(Guid Id, string DisplayName, string? Code, Guid WarehouseId, string WarehouseDisplayName);
public sealed record ProductMasterStorageLocationOptions(IReadOnlyList<ProductMasterStorageLocationOption> Items);

public interface IProductMasterOptionsReader
{
    ValueTask<ProductMasterStorageLocationOptions> GetStorageLocationsAsync(string locale, string? search, int limit, CancellationToken cancellationToken);
}

public static class ProcurementProductMasterErrorCodes
{
    public const string InvalidInput = "product.procurement-product.invalid-input";
    public const string ItemNotFound = "product.procurement-product.not-found";
    public const string ItemDeleted = "product.procurement-product.deleted";
    public const string StorageLocationNotFound = "product.procurement-product.storage-location-not-found";
    public const string StaleRowVersion = "product.procurement-product.stale-row-version";
    public const string IdempotencyKeyReused = "product.procurement-product.idempotency-key-reused";
}

public static class SalesProductMasterErrorCodes
{
    public const string InvalidInput = "product.sales-product.invalid-input";
    public const string ItemNotFound = "product.sales-product.not-found";
    public const string ItemDeleted = "product.sales-product.deleted";
    public const string ProductGroupNotFound = "product.sales-product.group-not-found";
    public const string StorageLocationNotFound = "product.sales-product.storage-location-not-found";
    public const string StaleRowVersion = "product.sales-product.stale-row-version";
    public const string IdempotencyKeyReused = "product.sales-product.idempotency-key-reused";
}

public static class ProcurementProductMasterValidation
{
    public static ApplicationError? Validate(CreateProcurementProductCommand command) =>
        HasVisibleName(command.NameZhTw, command.NameThTh)
        && !string.IsNullOrWhiteSpace(command.UnitCode)
        && command.DefaultStorageLocationId != Guid.Empty
            ? null
            : Invalid();

    public static ApplicationError? Validate(UpdateProcurementProductCommand command) =>
        command.ProcurementProductId != Guid.Empty
        && command.ExpectedRowVersion >= 1
        && HasVisibleName(command.NameZhTw, command.NameThTh)
        && !string.IsNullOrWhiteSpace(command.UnitCode)
        && command.DefaultStorageLocationId != Guid.Empty
            ? null
            : Invalid();

    private static ApplicationError Invalid() => ApplicationError.Create(ApplicationErrorKind.Validation, ProcurementProductMasterErrorCodes.InvalidInput);
    private static bool HasVisibleName(string? zh, string? th) => !string.IsNullOrWhiteSpace(zh) || !string.IsNullOrWhiteSpace(th);
}

public static class SalesProductMasterValidation
{
    public static ApplicationError? Validate(CreateSalesProductCommand command) => IsValid(command.SalesProductGroupId, command.NameZhTw, command.NameThTh, command.PricingBasis, command.PackagingWeight, command.SalesWeight, command.DefaultStorageLocationId) ? null : Invalid();

    public static ApplicationError? Validate(UpdateSalesProductCommand command) =>
        command.SalesProductId != Guid.Empty
        && command.ExpectedRowVersion >= 1
        && IsValid(command.SalesProductGroupId, command.NameZhTw, command.NameThTh, command.PricingBasis, command.PackagingWeight, command.SalesWeight, command.DefaultStorageLocationId)
            ? null
            : Invalid();

    private static bool IsValid(Guid groupId, string? zh, string? th, SalesPricingBasis basis, decimal? packagingWeight, decimal? salesWeight, Guid? storageLocationId)
    {
        if (groupId == Guid.Empty || (!Enum.IsDefined(basis)) || (string.IsNullOrWhiteSpace(zh) && string.IsNullOrWhiteSpace(th)) || storageLocationId == Guid.Empty)
        {
            return false;
        }

        if (packagingWeight is <= 0)
        {
            return false;
        }

        return basis switch
        {
            SalesPricingBasis.WEIGHT_BASED_UNIT => salesWeight is >= 0,
            SalesPricingBasis.UNIT_BASED => salesWeight is null,
            _ => false,
        };
    }

    private static ApplicationError Invalid() => ApplicationError.Create(ApplicationErrorKind.Validation, SalesProductMasterErrorCodes.InvalidInput);
}
