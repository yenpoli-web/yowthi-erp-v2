using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.DataProtection;
using YowThi.Erp.Api.Finance;
using YowThi.Erp.Api.Hosting;
using YowThi.Erp.Api.Infrastructure;
using YowThi.Erp.Api.Inventory;
using YowThi.Erp.Api.Labor;
using YowThi.Erp.Api.Outsourced;
using YowThi.Erp.Api.Party;
using YowThi.Erp.Api.Processing;
using YowThi.Erp.Api.Procurement;
using YowThi.Erp.Api.Product;
using YowThi.Erp.Api.Sales;
using YowThi.Erp.Api.SalesHandling;
using YowThi.Erp.Api.Security;
using YowThi.Erp.Infrastructure.Persistence.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.AddYowThiApi();

var connectionString = builder.Configuration.GetConnectionString("Erp")
    ?? throw new InvalidOperationException("ConnectionStrings:Erp is required.");
builder.Services.AddErpPersistence(connectionString);

var app = builder.Build();

app.UseYowThiApiInfrastructure();
app.MapYowThiTechnicalEndpoints();
app.MapYowThiAuthenticationEndpoints();
app.MapSecurityAccountEndpoints();
app.MapPartyEndpoints();
app.MapSupplierMasterEndpoints();
app.MapCustomerMasterEndpoints();
app.MapFarmerMasterEndpoints();
app.MapOutsourcedVendorMasterEndpoints();
app.MapEmployeeMasterEndpoints();
app.MapPartyLifecycleOptionEndpoints();
app.MapFarmerLifecycleEndpoints();
app.MapEmployeeLifecycleEndpoints();
app.MapProcurementEndpoints();
app.MapProcurementTransactionLifecycleEndpoints();
app.MapProcurementEntryOptionEndpoints();
app.MapProcurementWorkspaceEndpoints();
app.MapProcessingEndpoints();
app.MapProcessingTransactionLifecycleEndpoints();
app.MapProcessingExecutionOptionEndpoints();
app.MapProcessingWorkspaceEndpoints();
app.MapInventoryEndpoints();
app.MapInventoryOperationOptionEndpoints();
app.MapInventoryPositionEndpoints();
app.MapOutsourcedEndpoints();
app.MapOutsourcedTransactionLifecycleEndpoints();
app.MapOutsourcedWorkspaceEndpoints();
app.MapOutsourcedSupplyDetailOptionEndpoints();
app.MapSalesEndpoints();
app.MapSalesTransactionLifecycleEndpoints();
app.MapSalesWorkspaceEndpoints();
app.MapSalesConfirmationOptionEndpoints();
app.MapSalesHandlingEndpoints();
app.MapSalesHandlingWorkOptionEndpoints();
app.MapSalesPackagingItemMasterEndpoints();
app.MapSalesPackagingItemLifecycleEndpoints();
app.MapProcurementProductMasterEndpoints();
app.MapProcurementProductOptionEndpoints();
app.MapSalesProductMasterEndpoints();
app.MapSalesProductOptionEndpoints();
app.MapSalesProductGroupMasterEndpoints();
app.MapSalesProductGroupLifecycleEndpoints();
app.MapSalesProductGroupLifecycleOptionEndpoints();
app.MapContainerLifecycleEndpoints();
app.MapWarehouseMasterEndpoints();
app.MapStorageLocationMasterEndpoints();
app.MapWarehouseLifecycleEndpoints();
app.MapInfrastructureLifecycleOptionEndpoints();
app.MapLaborEndpoints();
app.MapLaborDailyWageOptionEndpoints();
app.MapFinanceEndpoints();
app.MapFinanceSettlementOptionEndpoints();
app.MapDataProtectionEndpoints();
app.MapHardDeleteOptionEndpoints();

app.Run();

public partial class Program;