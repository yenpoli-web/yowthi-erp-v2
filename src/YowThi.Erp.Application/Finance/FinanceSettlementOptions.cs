namespace YowThi.Erp.Application.Finance;

public sealed record FinanceSettlementOptionsQuery(
    string Locale,
    string? Search,
    int Offset,
    int Limit);

public sealed record FinanceSettlementOptionPage<T>(
    IReadOnlyList<T> Items,
    int? NextOffset);

public sealed record FinancePayableSettlementOption(
    Guid PayableId,
    string PayableKind,
    string SourceDisplayName,
    long OutstandingThb,
    long OutstandingVersion,
    DateTimeOffset UpdatedAt);

public sealed record FinanceReceivableSettlementOption(
    Guid ReceivableId,
    Guid SalesId,
    DateOnly SalesDate,
    string CustomerDisplayName,
    long OutstandingThb,
    long OutstandingVersion,
    DateTimeOffset UpdatedAt);

public interface IFinanceSettlementOptionsReader
{
    ValueTask<FinanceSettlementOptionPage<FinancePayableSettlementOption>> GetPayablesAsync(
        FinanceSettlementOptionsQuery query,
        CancellationToken cancellationToken);

    ValueTask<FinanceSettlementOptionPage<FinanceReceivableSettlementOption>> GetReceivablesAsync(
        FinanceSettlementOptionsQuery query,
        CancellationToken cancellationToken);
}
