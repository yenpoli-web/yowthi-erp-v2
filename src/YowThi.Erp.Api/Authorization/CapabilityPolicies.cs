namespace YowThi.Erp.Api.Authorization;

public static class CapabilityPolicies
{
    public const string ProcurementConfirm = "procurement.confirm";
    public const string ProcessingConfirm = "processing.confirm";
    public const string OutsourcedConfirm = "outsourced.confirm";
    public const string SalesConfirm = "sales.confirm";
    public const string FinancePay = "finance.pay";
    public const string InventoryAdjust = "inventory.adjust";
    public const string DataProtectionHardDelete = "data-protection.hard-delete";
}
