using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.Application.Procurement;

public sealed record ProcurementReceiptDestination(
    Guid StorageLocationId,
    Guid WarehouseId,
    string? WarehouseNameZhTw,
    string? WarehouseNameThTh,
    string ResolutionSource);

public interface IProcurementReceiptDestinationResolver
{
    ValueTask<ApplicationResult<ProcurementReceiptDestination>> ResolveAsync(
        DateOnly procurementDate,
        Guid procurementProductId,
        CancellationToken cancellationToken);
}

public static class ProcurementReceiptDestinationErrorCodes
{
    public const string Ambiguous = "procurement.receipt-destination-ambiguous";
}

public static class ProcurementReceiptDestinationFailures
{
    public static ApplicationResult<ProcurementReceiptDestination> Failure(
        ApplicationErrorKind kind,
        string code) =>
        ApplicationResult<ProcurementReceiptDestination>.Failure(
            ApplicationError.Create(kind, code));
}
