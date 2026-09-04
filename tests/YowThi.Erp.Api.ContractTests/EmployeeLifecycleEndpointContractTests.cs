using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Hosting;
using YowThi.Erp.Api.Idempotency;
using YowThi.Erp.Api.Party;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Serialization;
using YowThi.Erp.Application.Party;

namespace YowThi.Erp.Api.ContractTests;

public sealed class EmployeeLifecycleEndpointContractTests
{
    private const string SoftDeleteRoute = "/api/v1/party/employees/{employeeId:guid}/soft-delete";
    private const string RestoreRoute = "/api/v1/party/employees/{employeeId:guid}/restore";

    [Fact]
    public async Task Employee_lifecycle_routes_are_target_specific_authorized_idempotent_v1_posts()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapEmployeeLifecycleEndpoints();

        AssertLifecycleEndpoint(GetEndpoint(app, SoftDeleteRoute), EmployeeLifecycleEndpoints.SoftDeleteEmployeeOperationId);
        AssertLifecycleEndpoint(GetEndpoint(app, RestoreRoute), EmployeeLifecycleEndpoints.RestoreEmployeeOperationId);
    }

    [Fact]
    public async Task Employee_lifecycle_canonical_commands_include_route_id_and_expected_row_version()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapEmployeeLifecycleEndpoints();
        var employeeId = Guid.NewGuid();

        fixture.Executor.SoftDeleteResult = ApplicationResult<EmployeeLifecycleResult>.Success(
            new EmployeeLifecycleResult(employeeId, 8, true));
        var softDelete = await InvokeAsync(
            app,
            GetEndpoint(app, SoftDeleteRoute),
            employeeId,
            Guid.NewGuid(),
            """{"expectedRowVersion":7}""");
        Assert.Equal(StatusCodes.Status200OK, softDelete.StatusCode);
        Assert.Equal(employeeId, fixture.Executor.LastSoftDeleteExecution!.Command.EmployeeId);
        Assert.Equal(7, fixture.Executor.LastSoftDeleteExecution.Command.ExpectedRowVersion);
        AssertCanonical(
            fixture.Hasher.LastPayloadJson,
            EmployeeLifecycleEndpoints.SoftDeleteEmployeeCommandType,
            employeeId,
            7);

        fixture.Executor.RestoreResult = ApplicationResult<EmployeeLifecycleResult>.Success(
            new EmployeeLifecycleResult(employeeId, 9, false));
        var restore = await InvokeAsync(
            app,
            GetEndpoint(app, RestoreRoute),
            employeeId,
            Guid.NewGuid(),
            """{"expectedRowVersion":8}""");
        Assert.Equal(StatusCodes.Status200OK, restore.StatusCode);
        Assert.Equal(employeeId, fixture.Executor.LastRestoreExecution!.Command.EmployeeId);
        Assert.Equal(8, fixture.Executor.LastRestoreExecution.Command.ExpectedRowVersion);
        AssertCanonical(
            fixture.Hasher.LastPayloadJson,
            EmployeeLifecycleEndpoints.RestoreEmployeeCommandType,
            employeeId,
            8);
    }

    [Fact]
    public async Task Employee_lifecycle_maps_state_conflict_and_transport_version_without_reusing_other_policies()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapEmployeeLifecycleEndpoints();

        fixture.Executor.SoftDeleteResult = ApplicationResult<EmployeeLifecycleResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Conflict,
                EmployeeLifecycleErrorCodes.AlreadyDeleted));
        var conflict = await InvokeAsync(
            app,
            GetEndpoint(app, SoftDeleteRoute),
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"expectedRowVersion":1}""");
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        AssertProblemCode(conflict.Body, EmployeeLifecycleErrorCodes.AlreadyDeleted);

        var invalid = await InvokeAsync(
            app,
            GetEndpoint(app, RestoreRoute),
            Guid.NewGuid(),
            Guid.NewGuid(),
            """{"expectedRowVersion":0}""");
        Assert.Equal(StatusCodes.Status400BadRequest, invalid.StatusCode);
        AssertProblemCode(invalid.Body, "request.validation-failed");

        var endpoint = GetEndpoint(app, SoftDeleteRoute);
        var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(x => x.Policy).ToArray();
        Assert.Contains(CapabilityPolicies.EmployeeLifecycle, policies);
        Assert.DoesNotContain(CapabilityPolicies.SupplierLifecycle, policies);
        Assert.DoesNotContain(CapabilityPolicies.FarmerLifecycle, policies);
        Assert.DoesNotContain(CapabilityPolicies.CustomerLifecycle, policies);
        Assert.DoesNotContain(CapabilityPolicies.OutsourcedVendorLifecycle, policies);
        Assert.DoesNotContain(CapabilityPolicies.DataProtectionHardDelete, policies);
    }

    private static void AssertLifecycleEndpoint(RouteEndpoint endpoint, string operationId)
    {
        Assert.Contains(HttpMethods.Post, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        Assert.Equal(operationId, endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            metadata => metadata.Policy == CapabilityPolicies.EmployeeLifecycle);
        Assert.NotNull(endpoint.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());

        var statuses = endpoint.Metadata
            .GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode)
            .ToHashSet();
        Assert.Contains(StatusCodes.Status200OK, statuses);
        Assert.Contains(StatusCodes.Status400BadRequest, statuses);
        Assert.Contains(StatusCodes.Status401Unauthorized, statuses);
        Assert.Contains(StatusCodes.Status403Forbidden, statuses);
        Assert.Contains(StatusCodes.Status404NotFound, statuses);
        Assert.Contains(StatusCodes.Status409Conflict, statuses);
        Assert.Contains(StatusCodes.Status422UnprocessableEntity, statuses);
    }

    private static void AssertCanonical(
        string? payload,
        string commandType,
        Guid employeeId,
        long expectedRowVersion)
    {
        Assert.NotNull(payload);
        using var document = JsonDocument.Parse(payload);
        Assert.Equal(commandType, document.RootElement.GetProperty("commandType").GetString());
        var command = document.RootElement.GetProperty("command");
        Assert.Equal(employeeId, command.GetProperty("employeeId").GetGuid());
        Assert.Equal(expectedRowVersion, command.GetProperty("expectedRowVersion").GetInt64());
    }

    private static void AssertProblemCode(string body, string expectedCode)
    {
        using var document = JsonDocument.Parse(body);
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
    }

    private static async Task<HttpInvocationResult> InvokeAsync(
        WebApplication app,
        RouteEndpoint endpoint,
        Guid employeeId,
        Guid commandId,
        string body)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        await using var requestBody = new MemoryStream(bodyBytes);
        await using var responseBody = new MemoryStream();

        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new RequestBodyDetectionFeature());
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = endpoint.RoutePattern.RawText!
            .Replace("{employeeId:guid}", employeeId.ToString(), StringComparison.Ordinal);
        context.Request.RouteValues["employeeId"] = employeeId.ToString();
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = bodyBytes.Length;
        context.Request.Body = requestBody;
        context.Request.Headers[IdempotencyKeyEndpointFilter.HeaderName] = commandId.ToString();
        context.Response.Body = responseBody;
        context.SetEndpoint(endpoint);

        Assert.NotNull(endpoint.RequestDelegate);
        await endpoint.RequestDelegate(context);

        responseBody.Position = 0;
        using var reader = new StreamReader(responseBody, Encoding.UTF8, leaveOpen: true);
        return new HttpInvocationResult(context.Response.StatusCode, await reader.ReadToEndAsync());
    }

    private static RouteEndpoint GetEndpoint(WebApplication app, string route) =>
        Assert.Single(
            ((IEndpointRouteBuilder)app).DataSources
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>(),
            endpoint => endpoint.RoutePattern.RawText == route);

    private static AppFixture CreateApp()
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

        var executor = new StubEmployeeLifecycleExecutor();
        var hasher = new CapturingRequestHasher();
        builder.Services.AddSingleton<IActorContext>(
            new StubActorContext(ActorAccountId.From(Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339c0"))));
        builder.Services.AddSingleton<ICommandRequestHasher>(hasher);
        builder.Services.AddSingleton<IEmployeeLifecycleExecutor>(executor);

        var app = builder.Build();
        app.UseYowThiApiInfrastructure();
        app.MapYowThiTechnicalEndpoints();
        return new AppFixture(app, executor, hasher);
    }

    private sealed record AppFixture(
        WebApplication App,
        StubEmployeeLifecycleExecutor Executor,
        CapturingRequestHasher Hasher);

    private sealed record HttpInvocationResult(int StatusCode, string Body);

    private sealed class RequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }

    private sealed class StubActorContext(ActorAccountId actorAccountId) : IActorContext
    {
        public ActorAccountId ActorAccountId { get; } = actorAccountId;
    }

    private sealed class CapturingRequestHasher : ICommandRequestHasher
    {
        public string? LastPayloadJson { get; private set; }

        public CommandRequestHash Compute(JsonPayload canonicalCommandPayload)
        {
            LastPayloadJson = Encoding.UTF8.GetString(canonicalCommandPayload.Utf8Json.Span);
            return CommandRequestHash.FromSha256(
                Enumerable.Repeat((byte)0x7A, CommandRequestHash.Sha256Length).ToArray());
        }
    }

    private sealed class StubEmployeeLifecycleExecutor : IEmployeeLifecycleExecutor
    {
        public ApplicationResult<EmployeeLifecycleResult> SoftDeleteResult { get; set; } =
            ApplicationResult<EmployeeLifecycleResult>.Success(
                new EmployeeLifecycleResult(Guid.NewGuid(), 2, true));

        public ApplicationResult<EmployeeLifecycleResult> RestoreResult { get; set; } =
            ApplicationResult<EmployeeLifecycleResult>.Success(
                new EmployeeLifecycleResult(Guid.NewGuid(), 3, false));

        public SoftDeleteEmployeeExecution? LastSoftDeleteExecution { get; private set; }
        public RestoreEmployeeExecution? LastRestoreExecution { get; private set; }

        public ValueTask<ApplicationResult<EmployeeLifecycleResult>> SoftDeleteAsync(
            SoftDeleteEmployeeExecution execution,
            CancellationToken cancellationToken)
        {
            LastSoftDeleteExecution = execution;
            return ValueTask.FromResult(SoftDeleteResult);
        }

        public ValueTask<ApplicationResult<EmployeeLifecycleResult>> RestoreAsync(
            RestoreEmployeeExecution execution,
            CancellationToken cancellationToken)
        {
            LastRestoreExecution = execution;
            return ValueTask.FromResult(RestoreResult);
        }
    }
}
