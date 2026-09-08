using YowThi.Erp.Api.Authorization;

namespace YowThi.Erp.Api.Hosting;

public static class ApiApplicationExtensions
{
    public static WebApplication UseYowThiApiInfrastructure(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseMiddleware<SecurityActorResolutionMiddleware>();
        app.UseMiddleware<ApiAntiforgeryMiddleware>();
        app.UseAuthorization();

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
