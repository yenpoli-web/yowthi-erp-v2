using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.DataProtection;
using YowThi.Erp.Api.Hosting;
using YowThi.Erp.Api.Idempotency;

namespace YowThi.Erp.Api.ContractTests;

public sealed class ProcessingDataProtectionEndpointContractTests
{
    [Fact]
    public async Task Processing_data_protection_hard_delete_routes_are_highest_authority_idempotent_and_reauthenticated()
    {
        await using var app = CreateApp();
        app.MapDataProtectionEndpoints();

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v1/data-protection/processing-executions/{processingExecutionId:guid}/hard-delete"] = DataProtectionEndpoints.HardDeleteProcessingExecutionOperationId,
            ["/api/v1/data-protection/processing-execution-inputs/{processingExecutionId:guid}/hard-delete"] = DataProtectionEndpoints.HardDeleteProcessingExecutionInputOperationId,
            ["/api/v1/data-protection/processing-execution-outputs/{processingExecutionOutputId:guid}/hard-delete"] = DataProtectionEndpoints.HardDeleteProcessingExecutionOutputOperationId,
        };

        foreach (var pair in expected)
        {
            var endpoint = Assert.Single(GetRouteEndpoints(app), item => item.RoutePattern.RawText == pair.Key);
            Assert.Contains(HttpMethods.Post, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
            Assert.Equal(pair.Value, endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                metadata => metadata.Policy == CapabilityPolicies.ProcessingTransactionLifecycle);
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                metadata => metadata.Policy == CapabilityPolicies.DataProtectionHardDelete);
            Assert.NotNull(endpoint.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());
            Assert.NotNull(endpoint.Metadata.GetMetadata<RequiresDeletionReauthenticationMetadata>());
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
            [$"{YowThi.Erp.Api.Localization.ApiLocalizationOptions.SectionName}:DefaultLocale"] = "zh-TW",
        });
        builder.AddYowThiApi();
        return builder.Build();
    }

    private static RouteEndpoint[] GetRouteEndpoints(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
}