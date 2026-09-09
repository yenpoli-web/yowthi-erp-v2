namespace YowThi.Erp.Application.Security;

public static class SecurityCapabilities
{
    public const string ProcurementConfirm = "procurement.confirm";
    public const string ProcurementBatchLifecycle = "procurement.batch.lifecycle";
    public const string ProcurementTransactionLifecycle = "procurement.transaction.lifecycle";
    public const string ProcessingConfirm = "processing.confirm";
    public const string ProcessingTransactionLifecycle = "processing.transaction.lifecycle";
    public const string OutsourcedConfirm = "outsourced.confirm";
    public const string SalesConfirm = "sales.confirm";
    public const string SalesCorrectAllocation = "sales.correct-allocation";
    public const string SalesTransactionLifecycle = "sales.transaction.lifecycle";
    public const string SalesHandlingWorkRecord = "sales-handling.work-record.record";
    public const string SalesPackagingItemLifecycle = "sales-handling.packaging-item.lifecycle";
    public const string ProcurementProductManage = "product.procurement-product.manage";
    public const string SalesProductManage = "product.sales-product.manage";
    public const string SalesProductGroupLifecycle = "product.sales-product-group.lifecycle";
    public const string InfrastructureContainerLifecycle = "infrastructure.container.lifecycle";
    public const string InfrastructureWarehouseManage = "infrastructure.warehouse.manage";
    public const string InfrastructureWarehouseLifecycle = "infrastructure.warehouse.lifecycle";
    public const string InfrastructureStorageLocationManage = "infrastructure.storage-location.manage";
    public const string LaborDailyWageConfirm = "labor.daily-wage.confirm";
    public const string FinancePay = "finance.pay";
    public const string FinanceCorrect = "finance.correct";
    public const string InventoryView = "inventory.view";
    public const string InventoryAdjust = "inventory.adjust";
    public const string SupplierLifecycle = "party.supplier.lifecycle";
    public const string FarmerLifecycle = "party.farmer.lifecycle";
    public const string EmployeeLifecycle = "party.employee.lifecycle";
    public const string CustomerLifecycle = "party.customer.lifecycle";
    public const string OutsourcedVendorLifecycle = "party.outsourced-vendor.lifecycle";
    public const string DataProtectionHardDelete = "data-protection.hard-delete";
    public const string SecurityAccountManage = "security.account.manage";

    public static IReadOnlyList<string> All { get; } =
    [
        ProcurementConfirm,
        ProcurementBatchLifecycle,
        ProcurementTransactionLifecycle,
        ProcessingConfirm,
        ProcessingTransactionLifecycle,
        OutsourcedConfirm,
        SalesConfirm,
        SalesCorrectAllocation,
        SalesTransactionLifecycle,
        SalesHandlingWorkRecord,
        SalesPackagingItemLifecycle,
        ProcurementProductManage,
        SalesProductManage,
        SalesProductGroupLifecycle,
        InfrastructureContainerLifecycle,
        InfrastructureWarehouseManage,
        InfrastructureWarehouseLifecycle,
        InfrastructureStorageLocationManage,
        LaborDailyWageConfirm,
        FinancePay,
        FinanceCorrect,
        InventoryView,
        InventoryAdjust,
        SupplierLifecycle,
        FarmerLifecycle,
        EmployeeLifecycle,
        CustomerLifecycle,
        OutsourcedVendorLifecycle,
        DataProtectionHardDelete,
        SecurityAccountManage,
    ];

    public static bool IsKnown(string capability) =>
        All.Contains(capability, StringComparer.Ordinal);
}
