using YowThi.Erp.Domain.Procurement;

namespace YowThi.Erp.Application.Procurement;

public sealed record ProcurementEntryOptionsQuery(
    string Locale,
    string? Search,
    int Offset,
    int Limit);

public sealed record ProcurementEntryOptionPage<T>(
    IReadOnlyList<T> Items,
    int? NextOffset);

public sealed record ProcurementProductOption(
    Guid Id,
    string DisplayName,
    string UnitCode);

public sealed record ProcurementSourceOption(
    Guid Id,
    string? Code,
    string DisplayName);

public sealed record ProcurementReceiptStorageLocationOption(
    Guid Id,
    string DisplayName,
    string? Code,
    Guid WarehouseId,
    bool IsProductDefault);

public sealed record ProcurementReceiptStorageLocationOptions(
    Guid ProcurementProductId,
    Guid? DefaultStorageLocationId,
    ProcurementEntryOptionPage<ProcurementReceiptStorageLocationOption> Locations);

public interface IProcurementEntryOptionsReader
{
    ValueTask<ProcurementEntryOptionPage<ProcurementProductOption>> GetProductsAsync(
        ProcurementEntryOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<ProcurementEntryOptionPage<ProcurementSourceOption>> GetSourcesAsync(
        ProcurementSourceType sourceType,
        ProcurementEntryOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<ProcurementReceiptStorageLocationOptions?> GetReceiptStorageLocationsAsync(
        Guid procurementProductId,
        ProcurementEntryOptionsQuery query,
        CancellationToken cancellationToken);
}
