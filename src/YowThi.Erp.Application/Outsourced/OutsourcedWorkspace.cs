namespace YowThi.Erp.Application.Outsourced;

public sealed record OutsourcedWorkspaceListQuery(
    string Locale,
    string? Search,
    int Offset,
    int Limit);

public sealed record OutsourcedWorkspaceListItem(
    Guid Id,
    DateOnly SupplyDate,
    Guid OutsourcedVendorId,
    string OutsourcedVendorDisplayName,
    string LifecycleStatus,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? DeletedAt);

public sealed record OutsourcedWorkspaceListPage(
    IReadOnlyList<OutsourcedWorkspaceListItem> Items,
    int? NextOffset);

public sealed record OutsourcedWorkspaceDetailItem(
    Guid Id,
    Guid SalesProductId,
    string SalesProductDisplayName,
    decimal Quantity,
    string PricingBasis,
    decimal UnitPrice,
    long AmountThb,
    Guid ReceiptStorageLocationId,
    string ReceiptStorageLocationDisplayName,
    long RowVersion,
    DateTimeOffset RecordedAt,
    DateTimeOffset? DeletedAt);

public sealed record OutsourcedWorkspace(
    Guid Id,
    DateOnly SupplyDate,
    Guid OutsourcedVendorId,
    string OutsourcedVendorDisplayName,
    string LifecycleStatus,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? DeletedAt,
    IReadOnlyList<OutsourcedWorkspaceDetailItem> Details);

public interface IOutsourcedWorkspaceReader
{
    ValueTask<OutsourcedWorkspaceListPage> GetBatchesAsync(
        OutsourcedWorkspaceListQuery query,
        CancellationToken cancellationToken);

    ValueTask<OutsourcedWorkspace?> GetBatchAsync(
        Guid outsourcedSupplyBatchId,
        string locale,
        CancellationToken cancellationToken);
}
