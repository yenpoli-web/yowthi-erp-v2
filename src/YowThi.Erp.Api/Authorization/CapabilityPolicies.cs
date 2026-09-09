using YowThi.Erp.Application.Security;

namespace YowThi.Erp.Api.Authorization;

public static class CapabilityPolicies
{
    public const string ProcurementConfirm = SecurityCapabilities.ProcurementConfirm;
    public const string ProcurementBatchLifecycle = SecurityCapabilities.ProcurementBatchLifecycle;
    public const string ProcessingConfirm = SecurityCapabilities.ProcessingConfirm;
    public const string OutsourcedConfirm = SecurityCapabilities.OutsourcedConfirm;
    public const string SalesConfirm = SecurityCapabilities.SalesConfirm;
    public const string SalesCorrectAllocation = SecurityCapabilities.SalesCorrectAllocation;
    public const string SalesHandlingWorkRecord = SecurityCapabilities.SalesHandlingWorkRecord;
    public const string SalesPackagingItemLifecycle = SecurityCapabilities.SalesPackagingItemLifecycle;
    public const string ProcurementProductManage = SecurityCapabilities.ProcurementProductManage;
    public const string SalesProductManage = SecurityCapabilities.SalesProductManage;
    public const string SalesProductGroupLifecycle = SecurityCapabilities.SalesProductGroupLifecycle;
    public const string InfrastructureContainerLifecycle = SecurityCapabilities.InfrastructureContainerLifecycle;
    public const string InfrastructureWarehouseManage = SecurityCapabilities.InfrastructureWarehouseManage;
    public const string InfrastructureWarehouseLifecycle = SecurityCapabilities.InfrastructureWarehouseLifecycle;
    public const string InfrastructureStorageLocationManage = SecurityCapabilities.InfrastructureStorageLocationManage;
    public const string LaborDailyWageConfirm = SecurityCapabilities.LaborDailyWageConfirm;
    public const string FinancePay = SecurityCapabilities.FinancePay;
    public const string FinanceCorrect = SecurityCapabilities.FinanceCorrect;
    public const string InventoryView = SecurityCapabilities.InventoryView;
    public const string InventoryAdjust = SecurityCapabilities.InventoryAdjust;
    public const string SupplierLifecycle = SecurityCapabilities.SupplierLifecycle;
    public const string FarmerLifecycle = SecurityCapabilities.FarmerLifecycle;
    public const string EmployeeLifecycle = SecurityCapabilities.EmployeeLifecycle;
    public const string CustomerLifecycle = SecurityCapabilities.CustomerLifecycle;
    public const string OutsourcedVendorLifecycle = SecurityCapabilities.OutsourcedVendorLifecycle;
    public const string DataProtectionHardDelete = SecurityCapabilities.DataProtectionHardDelete;
    public const string SecurityAccountManage = SecurityCapabilities.SecurityAccountManage;
}
