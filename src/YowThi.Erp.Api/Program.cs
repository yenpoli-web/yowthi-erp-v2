using YowThi.Erp.Api.Finance;
using YowThi.Erp.Api.Hosting;
using YowThi.Erp.Api.Inventory;
using YowThi.Erp.Api.Labor;
using YowThi.Erp.Api.Outsourced;
using YowThi.Erp.Api.Processing;
using YowThi.Erp.Api.Procurement;
using YowThi.Erp.Api.Sales;
using YowThi.Erp.Api.SalesHandling;

var builder = WebApplication.CreateBuilder(args);

builder.AddYowThiApi();

var app = builder.Build();

app.UseYowThiApiInfrastructure();
app.MapYowThiTechnicalEndpoints();
app.MapProcurementEndpoints();
app.MapProcurementEntryOptionEndpoints();
app.MapProcessingEndpoints();
app.MapProcessingExecutionOptionEndpoints();
app.MapInventoryEndpoints();
app.MapOutsourcedEndpoints();
app.MapOutsourcedSupplyDetailOptionEndpoints();
app.MapSalesEndpoints();
app.MapSalesHandlingEndpoints();
app.MapLaborEndpoints();
app.MapFinanceEndpoints();

app.Run();

public partial class Program;