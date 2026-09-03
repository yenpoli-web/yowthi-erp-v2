namespace YowThi.Erp.Application.Inventory;

public static class InventoryApplicationErrorCodes
{
    public const string InvalidInput = "inventory.invalid-input";
    public const string PositionNotFound = "inventory.position-not-found";
    public const string StorageLocationNotFound = "inventory.storage-location-not-found";
    public const string InsufficientStock = "inventory.insufficient-stock";
    public const string BatchClosed = "inventory.batch-closed";
    public const string ConcurrentChange = "inventory.concurrent-change";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}
