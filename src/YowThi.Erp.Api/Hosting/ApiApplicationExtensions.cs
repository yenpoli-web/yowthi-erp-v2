namespace YowThi.Erp.Api.Hosting;

public static class ApiApplicationExtensions
{
    public static WebApplication UseYowThiApiInfrastructure(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseRateLimiter();

        return app;
    }

    public static WebApplication MapYowThiTechnicalEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/health/live", static () => TypedResults.Ok(new HealthProbeResponse("live")))
            .AllowAnonymous()
            .ExcludeFromDescription()
            .WithName("Health_Live");

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi("/openapi/{documentName}.json");
        }

        return app;
    }

    private sealed record HealthProbeResponse(string Status);
}
