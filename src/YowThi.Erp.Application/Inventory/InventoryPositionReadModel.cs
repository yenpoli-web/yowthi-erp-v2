using YowThi.Erp.Domain.Inventory;

namespace YowThi.Erp.Application.Inventory;

public sealed record InventoryPositionQuery(
    string? Search,
    InventoryOrigin? Origin,
    InventoryObjectKind? ObjectKind,
    Guid? WarehouseId,
    Guid? StorageLocationId,
    bool IncludeZeroBalance,
    int Offset,
    int Limit,
    string Locale = "zh-TW");

public sealed record InventoryPositionItem(
    Guid Id,
    InventoryOrigin Origin,
    Guid SourceBatchId,
    InventoryObjectKind ObjectKind,
    Guid ObjectId,
    string ObjectDisplayName,
    Guid WarehouseId,
    string WarehouseDisplayName,
    Guid StorageLocationId,
    string StorageLocationDisplayName,
    InventoryRawSourceKind? RawSourceKind,
    Guid? SupplierId,
    string? SupplierDisplayName,
    decimal BalanceQuantity,
    long RowVersion);

public sealed record InventoryPositionPage(
    IReadOnlyList<InventoryPositionItem> Items,
    int? NextOffset);

public interface IInventoryPositionReader
{
    ValueTask<InventoryPositionPage> GetAsync(
        InventoryPositionQuery query,
        CancellationToken cancellationToken);
}
