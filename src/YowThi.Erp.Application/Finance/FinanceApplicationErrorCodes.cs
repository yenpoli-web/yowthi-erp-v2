namespace YowThi.Erp.Application.Finance;

public static class FinanceApplicationErrorCodes
{
    public const string InvalidInput = "finance.invalid-input";
    public const string PayableNotFound = "finance.payable-not-found";
    public const string ReceivableNotFound = "finance.receivable-not-found";
    public const string OutstandingChanged = "finance.outstanding-changed";
    public const string PaymentExceedsOutstanding = "finance.payment-exceeds-outstanding";
    public const string ReceiptExceedsOutstanding = "finance.receipt-exceeds-outstanding";
    public const string AdjustmentCausesNegativeOutstanding = "finance.adjustment-causes-negative-outstanding";
    public const string AdjustmentNotAllowed = "finance.adjustment-not-allowed";
    public const string TransportSettlementUnavailable = "finance.transport-settlement-unavailable";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
}
