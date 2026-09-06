namespace YowThi.Erp.Application.Sales;

public sealed record SalesConfirmationOptionsQuery(string Locale, string? Search, int Offset, int Limit);
public sealed record SalesConfirmationOptionPage<T>(IReadOnlyList<T> Items, int? NextOffset);
public sealed record SalesConfirmationSaleOption(Guid Id, DateOnly SalesDate, string CustomerDisplayName, long RowVersion);
public sealed record SalesConfirmationDetailOption(Guid Id, int LineNumber, Guid SalesProductId, string ProductDisplayName, decimal Quantity, string PricingBasis, decimal? SalesWeight, decimal UnitPrice, long AmountThb);
public sealed record SalesConfirmationWorkspace(Guid SalesId, DateOnly SalesDate, Guid CustomerId, string CustomerDisplayName, long RowVersion, IReadOnlyList<SalesConfirmationDetailOption> Details);

public interface ISalesConfirmationOptionsReader
{
    ValueTask<SalesConfirmationOptionPage<SalesConfirmationSaleOption>> GetSalesAsync(SalesConfirmationOptionsQuery query, CancellationToken cancellationToken);
    ValueTask<SalesConfirmationWorkspace?> GetWorkspaceAsync(Guid salesId, string locale, CancellationToken cancellationToken);
}
