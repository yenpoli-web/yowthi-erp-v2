namespace YowThi.Erp.Api.Authorization;

public static class CapabilityPolicies
{
    public const string ProcurementConfirm = "procurement.confirm";
    public const string ProcurementBatchLifecycle = "procurement.batch.lifecycle";
    public const string ProcessingConfirm = "processing.confirm";
    public const string OutsourcedConfirm = "outsourced.confirm";
    public const string SalesConfirm = "sales.confirm";
    public const string SalesCorrectAllocation = "sales.correct-allocation";
    public const string SalesHandlingWorkRecord = "sales-handling.work-record.record";
    public const string LaborDailyWageConfirm = "labor.daily-wage.confirm";
    public const string FinancePay = "finance.pay";
    public const string FinanceCorrect = "finance.correct";
    public const string InventoryAdjust = "inventory.adjust";
    public const string SupplierLifecycle = "party.supplier.lifecycle";
    public const string CustomerLifecycle = "party.customer.lifecycle";
    public const string OutsourcedVendorLifecycle = "party.outsourced-vendor.lifecycle";
    public const string DataProtectionHardDelete = "data-protection.hard-delete";
}
