namespace YowThi.Erp.Application.Sales;

public sealed record SalesWorkspaceListQuery(
    string Locale,
    string? Search,
    int Offset,
    int Limit);

public sealed record SalesWorkspaceListItem(
    Guid Id,
    DateOnly SalesDate,
    Guid CustomerId,
    string CustomerDisplayName,
    string Status,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? DeletedAt);

public sealed record SalesWorkspaceListPage(
    IReadOnlyList<SalesWorkspaceListItem> Items,
    int? NextOffset);

public sealed record SalesWorkspaceDetailItem(
    Guid Id,
    int LineNumber,
    Guid SalesProductId,
    string ProductDisplayName,
    decimal Quantity,
    string PricingBasis,
    decimal? SalesWeight,
    decimal UnitPrice,
    long AmountThb,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt);

public sealed record SalesWorkspace(
    Guid Id,
    DateOnly SalesDate,
    Guid CustomerId,
    string CustomerDisplayName,
    string Status,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? DeletedAt,
    IReadOnlyList<SalesWorkspaceDetailItem> Details);

public interface ISalesWorkspaceReader
{
    ValueTask<SalesWorkspaceListPage> GetSalesAsync(
        SalesWorkspaceListQuery query,
        CancellationToken cancellationToken);

    ValueTask<SalesWorkspace?> GetSaleAsync(
        Guid salesId,
        string locale,
        CancellationToken cancellationToken);
}
