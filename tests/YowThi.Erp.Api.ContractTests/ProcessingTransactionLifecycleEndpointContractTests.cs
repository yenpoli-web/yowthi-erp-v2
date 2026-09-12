using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Hosting;
using YowThi.Erp.Api.Idempotency;
using YowThi.Erp.Api.Processing;

namespace YowThi.Erp.Api.ContractTests;

public sealed class ProcessingTransactionLifecycleEndpointContractTests
{
    [Fact]
    public async Task Processing_transaction_lifecycle_routes_are_explicit_idempotent_and_guarded()
    {
        await using var app = CreateApp();
        app.MapProcessingTransactionLifecycleEndpoints();
        var endpoints = GetRouteEndpoints(app);

        var routes = new[]
        {
            ("/api/v1/processing/executions/{processingExecutionId:guid}/soft-delete", true, false),
            ("/api/v1/processing/executions/{processingExecutionId:guid}/restore", false, false),
            ("/api/v1/processing/executions/{processingExecutionId:guid}/hard-delete", true, true),
            ("/api/v1/processing/executions/{processingExecutionId:guid}/input/soft-delete", true, false),
            ("/api/v1/processing/executions/{processingExecutionId:guid}/input/restore", false, false),
            ("/api/v1/processing/executions/{processingExecutionId:guid}/input/hard-delete", true, true),
            ("/api/v1/processing/execution-outputs/{processingExecutionOutputId:guid}/soft-delete", true, false),
            ("/api/v1/processing/execution-outputs/{processingExecutionOutputId:guid}/restore", false, false),
            ("/api/v1/processing/execution-outputs/{processingExecutionOutputId:guid}/hard-delete", true, true),
        };

        foreach (var (route, requiresReauth, hardDelete) in routes)
        {
            var endpoint = Assert.Single(endpoints, candidate => candidate.RoutePattern.RawText == route);
            Assert.Contains(HttpMethods.Post, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
            AssertPolicy(endpoint, CapabilityPolicies.ProcessingTransactionLifecycle);
            Assert.NotNull(endpoint.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());
            Assert.Equal(requiresReauth, endpoint.Metadata.GetMetadata<RequiresDeletionReauthenticationMetadata>() is not null);
            if (hardDelete)
            {
                AssertPolicy(endpoint, CapabilityPolicies.DataProtectionHardDelete);
            }
        }
    }

    [Fact]
    public async Task Processing_workspace_routes_are_read_only_and_use_existing_module_access_capability()
    {
        await using var app = CreateApp();
        app.MapProcessingWorkspaceEndpoints();
        var endpoints = GetRouteEndpoints(app);

        var list = Assert.Single(
            endpoints,
            endpoint => endpoint.RoutePattern.RawText == "/api/v1/processing/workspace/");
        var detail = Assert.Single(
            endpoints,
            endpoint => endpoint.RoutePattern.RawText == "/api/v1/processing/workspace/{processingExecutionId:guid}");

        AssertRead(list);
        AssertRead(detail);
        Assert.Equal(
            ProcessingWorkspaceEndpoints.ListExecutionsOperationId,
            list.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Equal(
            ProcessingWorkspaceEndpoints.GetExecutionOperationId,
            detail.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
    }

    private static void AssertRead(RouteEndpoint endpoint)
    {
        Assert.Contains(HttpMethods.Get, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        AssertPolicy(endpoint, CapabilityPolicies.ProcessingConfirm);
        Assert.Null(endpoint.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());
        Assert.Null(endpoint.Metadata.GetMetadata<RequiresDeletionReauthenticationMetadata>());
    }

    private static void AssertPolicy(RouteEndpoint endpoint, string policy) =>
        Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(), metadata => metadata.Policy == policy);

    private static WebApplication CreateApp()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = "Development",
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Api:Localization:DefaultLocale"] = "zh-TW",
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
