namespace YowThi.Erp.Application.Procurement;

public sealed record ProcurementBatchListQuery(
    string Locale,
    string? Search,
    int Offset,
    int Limit);

public sealed record ProcurementBatchListItem(
    Guid Id,
    DateOnly ProcurementDate,
    Guid ProcurementProductId,
    string ProcurementProductDisplayName,
    string UnitCode,
    string ProcurementStatus,
    string LifecycleStatus,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt);

public sealed record ProcurementBatchListPage(
    IReadOnlyList<ProcurementBatchListItem> Items,
    int? NextOffset);

public sealed record ProcurementBatchEntryItem(
    Guid Id,
    string SourceType,
    Guid SourceId,
    string? SourceCode,
    string SourceDisplayName,
    decimal NetQuantity,
    string UnitCodeSnapshot,
    decimal UnitPrice,
    long AmountThb,
    bool CompanyPickup,
    Guid? ReceiptStorageLocationId,
    string? ReceiptStorageLocationDisplayName,
    long RowVersion,
    DateTimeOffset RecordedAt,
    DateTimeOffset? DeletedAt);

public sealed record ProcurementBatchWorkspace(
    Guid Id,
    DateOnly ProcurementDate,
    Guid ProcurementProductId,
    string ProcurementProductDisplayName,
    string UnitCode,
    string ProcurementStatus,
    string LifecycleStatus,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? DeletedAt,
    IReadOnlyList<ProcurementBatchEntryItem> Entries);

public interface IProcurementWorkspaceReader
{
    ValueTask<ProcurementBatchListPage> GetBatchesAsync(
        ProcurementBatchListQuery query,
        CancellationToken cancellationToken);

    ValueTask<ProcurementBatchWorkspace?> GetBatchAsync(
        Guid procurementBatchId,
        string locale,
        CancellationToken cancellationToken);
}
