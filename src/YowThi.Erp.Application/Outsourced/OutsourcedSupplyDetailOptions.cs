using YowThi.Erp.Domain.Product;

namespace YowThi.Erp.Application.Outsourced;

public sealed record OutsourcedSupplyDetailOptionsQuery(
    string Locale,
    string? Search,
    int Offset,
    int Limit);

public sealed record OutsourcedSupplyDetailOptionPage<T>(
    IReadOnlyList<T> Items,
    int? NextOffset);

public sealed record OutsourcedVendorOption(
    Guid Id,
    string DisplayName);

public sealed record OutsourcedSalesProductOption(
    Guid Id,
    string DisplayName,
    SalesPricingBasis PricingBasis);

public sealed record OutsourcedReceiptStorageLocationOption(
    Guid Id,
    string DisplayName,
    string? Code,
    Guid WarehouseId,
    bool IsProductDefault);

public sealed record OutsourcedReceiptStorageLocationOptions(
    Guid SalesProductId,
    Guid? DefaultStorageLocationId,
    OutsourcedSupplyDetailOptionPage<OutsourcedReceiptStorageLocationOption> Locations);

public interface IOutsourcedSupplyDetailOptionsReader
{
    ValueTask<OutsourcedSupplyDetailOptionPage<OutsourcedVendorOption>> GetVendorsAsync(
        OutsourcedSupplyDetailOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<OutsourcedSupplyDetailOptionPage<OutsourcedSalesProductOption>> GetProductsAsync(
        OutsourcedSupplyDetailOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<OutsourcedReceiptStorageLocationOptions?> GetReceiptStorageLocationsAsync(
        Guid salesProductId,
        OutsourcedSupplyDetailOptionsQuery query,
        CancellationToken cancellationToken);
}
