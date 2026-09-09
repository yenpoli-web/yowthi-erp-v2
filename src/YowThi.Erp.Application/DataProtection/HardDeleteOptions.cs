namespace YowThi.Erp.Application.DataProtection;

public sealed record HardDeleteOptionsQuery(
    string Locale,
    string? Search,
    int Offset,
    int Limit);

public sealed record HardDeleteOptionPage<T>(
    IReadOnlyList<T> Items,
    int? NextOffset);

public sealed record HardDeleteOption(
    Guid Id,
    string DisplayName,
    bool Active,
    long RowVersion,
    bool Deleted,
    DateTimeOffset? DeletedAt);

public interface IHardDeleteOptionsReader
{
    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetSuppliersAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetCustomersAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetOutsourcedVendorsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetFarmersAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetOutsourcedSupplyBatchesAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetOutsourcedSupplyDetailsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetProcurementBatchesAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetProcurementEntriesAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetSalesAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetSalesDetailsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetProcessingExecutionsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetProcessingExecutionInputsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<HardDeleteOptionPage<HardDeleteOption>> GetProcessingExecutionOutputsAsync(
        HardDeleteOptionsQuery query,
        CancellationToken cancellationToken);
}
