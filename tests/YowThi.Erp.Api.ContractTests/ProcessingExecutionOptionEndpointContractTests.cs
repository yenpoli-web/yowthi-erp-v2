using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Hosting;
using YowThi.Erp.Api.Idempotency;
using YowThi.Erp.Api.Processing;

namespace YowThi.Erp.Api.ContractTests;

public sealed class ProcessingExecutionOptionEndpointContractTests
{
    [Fact]
    public async Task Processing_execution_option_queries_are_authenticated_purpose_specific_gets_without_idempotency()
    {
        await using var app = CreateApp();
        app.MapProcessingExecutionOptionEndpoints();

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v1/processing/execution-options/employees"] = ProcessingExecutionOptionEndpoints.EmployeesOperationId,
            ["/api/v1/processing/execution-options/batches"] = ProcessingExecutionOptionEndpoints.BatchesOperationId,
            ["/api/v1/processing/execution-options/modules"] = ProcessingExecutionOptionEndpoints.ModulesOperationId,
            ["/api/v1/processing/execution-options/suppliers"] = ProcessingExecutionOptionEndpoints.SuppliersOperationId,
            ["/api/v1/processing/execution-options/input-storage-locations"] = ProcessingExecutionOptionEndpoints.InputStorageLocationsOperationId,
            ["/api/v1/processing/execution-options/module-outputs"] = ProcessingExecutionOptionEndpoints.ModuleOutputsOperationId,
            ["/api/v1/processing/execution-options/storage-locations"] = ProcessingExecutionOptionEndpoints.StorageLocationsOperationId,
        };

        foreach (var pair in expected)
        {
            var endpoint = Assert.Single(
                GetRouteEndpoints(app),
                endpoint => endpoint.RoutePattern.RawText == pair.Key);

            Assert.Contains(HttpMethods.Get, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
            Assert.Equal(pair.Value, endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                metadata => metadata.Policy == CapabilityPolicies.ProcessingConfirm);
            Assert.Null(endpoint.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>(),
                metadata => metadata.StatusCode == StatusCodes.Status200OK);
        }
    }

    [Fact]
    public async Task Cascading_processing_option_queries_expose_not_found_contract()
    {
        await using var app = CreateApp();
        app.MapProcessingExecutionOptionEndpoints();

        var cascadingRoutes = new[]
        {
            "/api/v1/processing/execution-options/modules",
            "/api/v1/processing/execution-options/input-storage-locations",
            "/api/v1/processing/execution-options/module-outputs",
        };

        foreach (var route in cascadingRoutes)
        {
            var endpoint = Assert.Single(GetRouteEndpoints(app), endpoint => endpoint.RoutePattern.RawText == route);
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>(),
                metadata => metadata.StatusCode == StatusCodes.Status404NotFound);
        }
    }

    private static WebApplication CreateApp()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = Environments.Development,
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{YowThi.Erp.Api.Localization.ApiLocalizationOptions.SectionName}:DefaultLocale"] = "th-TH",
        });
        builder.AddYowThiApi();

        var app = builder.Build();
        app.UseYowThiApiInfrastructure();
        app.MapYowThiTechnicalEndpoints();
        return app;
    }

    private static RouteEndpoint[] GetRouteEndpoints(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
}
