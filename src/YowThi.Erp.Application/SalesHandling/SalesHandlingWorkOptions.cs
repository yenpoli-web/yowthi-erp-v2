namespace YowThi.Erp.Application.SalesHandling;

public sealed record SalesHandlingWorkOptionsQuery(
    string Locale,
    string? Search,
    int Offset,
    int Limit);

public sealed record SalesHandlingWorkOptionPage<T>(
    IReadOnlyList<T> Items,
    int? NextOffset);

public sealed record SalesHandlingSaleOption(
    Guid Id,
    DateOnly SalesDate,
    string CustomerDisplayName,
    string Status);

public sealed record SalesHandlingEmployeeOption(
    Guid Id,
    string DisplayName);

public sealed record SalesHandlingPackagingItemOption(
    Guid Id,
    string DisplayName);

public interface ISalesHandlingWorkOptionsReader
{
    ValueTask<SalesHandlingWorkOptionPage<SalesHandlingSaleOption>> GetSalesAsync(
        SalesHandlingWorkOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<SalesHandlingWorkOptionPage<SalesHandlingEmployeeOption>> GetEmployeesAsync(
        SalesHandlingWorkOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<SalesHandlingWorkOptionPage<SalesHandlingPackagingItemOption>> GetPackagingItemsAsync(
        SalesHandlingWorkOptionsQuery query,
        CancellationToken cancellationToken);
}
