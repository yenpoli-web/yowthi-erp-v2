using YowThi.Erp.Api.Hosting;
using YowThi.Erp.Api.Outsourced;
using YowThi.Erp.Api.Processing;
using YowThi.Erp.Api.Procurement;

var builder = WebApplication.CreateBuilder(args);

builder.AddYowThiApi();

var app = builder.Build();

app.UseYowThiApiInfrastructure();
app.MapYowThiTechnicalEndpoints();
app.MapProcurementEndpoints();
app.MapProcurementEntryOptionEndpoints();
app.MapProcessingEndpoints();
app.MapOutsourcedEndpoints();
app.MapOutsourcedSupplyDetailOptionEndpoints();

app.Run();

public partial class Program;
