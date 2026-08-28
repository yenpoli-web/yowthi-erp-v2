using YowThi.Erp.Api.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddYowThiApi();

var app = builder.Build();

app.UseYowThiApiInfrastructure();
app.MapYowThiTechnicalEndpoints();

app.Run();

public partial class Program;
