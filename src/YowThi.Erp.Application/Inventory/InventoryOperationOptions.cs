using YowThi.Erp.Domain.Inventory;

namespace YowThi.Erp.Application.Inventory;

public sealed record InventoryOperationOptionsQuery(
    string Locale,
    string? Search,
    int Offset,
    int Limit);

public sealed record InventoryOperationOptionPage<T>(
    IReadOnlyList<T> Items,
    int? NextOffset);

public sealed record InventoryOperationIdentityOption(
    InventoryOrigin Origin,
    Guid? ProcurementBatchId,
    Guid? OutsourcedSupplyBatchId,
    DateOnly BatchDate,
    InventoryObjectKind InventoryObjectKind,
    Guid? ProcurementProductId,
    Guid? ProcessMaterialId,
    Guid? SalesProductId,
    string ObjectDisplayName,
    InventoryRawSourceKind? RawSourceKind,
    Guid? SupplierId,
    string? RawSourceDisplayName);

public sealed record InventoryTransferSourceOption(
    Guid InventoryPositionId,
    InventoryOperationIdentityOption InventoryIdentity,
    Guid SourceStorageLocationId,
    string SourceStorageLocationDisplayName,
    bool SourceStorageLocationActive,
    decimal BalanceQuantity);

public sealed record InventoryStorageLocationOption(
    Guid Id,
    string DisplayName,
    bool Active);

public interface IInventoryOperationOptionsReader
{
    ValueTask<InventoryOperationOptionPage<InventoryTransferSourceOption>> GetTransferSourcesAsync(
        InventoryOperationOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<InventoryOperationOptionPage<InventoryOperationIdentityOption>> GetAdjustmentIdentitiesAsync(
        InventoryOperationOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<InventoryOperationOptionPage<InventoryStorageLocationOption>> GetTransferDestinationsAsync(
        InventoryOperationOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<InventoryOperationOptionPage<InventoryStorageLocationOption>> GetAdjustmentLocationsAsync(
        InventoryOperationOptionsQuery query,
        CancellationToken cancellationToken);
}
