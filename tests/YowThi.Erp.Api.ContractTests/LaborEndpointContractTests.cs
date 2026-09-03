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
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Api.Hosting;
using YowThi.Erp.Api.Idempotency;
using YowThi.Erp.Api.Labor;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Serialization;
using YowThi.Erp.Application.Labor;

namespace YowThi.Erp.Api.ContractTests;

public sealed class LaborEndpointContractTests
{
    [Fact]
    public async Task Confirm_employee_daily_wage_endpoint_matches_v1_contract()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapLaborEndpoints();

        var endpoint = GetEndpoint(app);

        Assert.Contains(HttpMethods.Post, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        Assert.Equal(
            LaborEndpoints.ConfirmEmployeeDailyWageOperationId,
            endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            metadata => metadata.Policy == CapabilityPolicies.LaborDailyWageConfirm);
        Assert.NotNull(endpoint.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());

        var statuses = endpoint.Metadata
            .GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode)
            .ToHashSet();

        Assert.Contains(StatusCodes.Status201Created, statuses);
        Assert.Contains(StatusCodes.Status400BadRequest, statuses);
        Assert.Contains(StatusCodes.Status401Unauthorized, statuses);
        Assert.Contains(StatusCodes.Status403Forbidden, statuses);
        Assert.Contains(StatusCodes.Status404NotFound, statuses);
        Assert.Contains(StatusCodes.Status409Conflict, statuses);
        Assert.Contains(StatusCodes.Status422UnprocessableEntity, statuses);
    }

    [Fact]
    public async Task Confirm_employee_daily_wage_builds_canonical_command_and_returns_created_response()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapLaborEndpoints();

        var employeeId = Guid.NewGuid();
        var outputId = Guid.NewGuid();
        var wageId = Guid.NewGuid();
        var payableId = Guid.NewGuid();
        fixture.Executor.Result = ApplicationResult<ConfirmEmployeeDailyWageResult>.Success(
            new ConfirmEmployeeDailyWageResult(wageId, 1, payableId, 120, 80, 200));

        var body = $$"""
        {
          "workDate": "2026-09-03",
          "employeeId": "{{employeeId}}",
          "processingWageRateOverrides": [
            {
              "processingModuleOutputId": "{{outputId}}",
              "configuredWageRateSnapshot": 2.5,
              "appliedWageRate": 3.75
            }
          ]
        }
        """;

        var response = await InvokeAsync(app, GetEndpoint(app), Guid.NewGuid(), body);

        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        Assert.NotNull(fixture.Executor.LastExecution);
        Assert.Equal(new DateOnly(2026, 9, 3), fixture.Executor.LastExecution.Command.WorkDate);
        Assert.Equal(employeeId, fixture.Executor.LastExecution.Command.EmployeeId);
        var rateOverride = Assert.Single(fixture.Executor.LastExecution.Command.ProcessingWageRateOverrides);
        Assert.Equal(outputId, rateOverride.ProcessingModuleOutputId);
        Assert.Equal(2.5m, rateOverride.ConfiguredWageRateSnapshot);
        Assert.Equal(3.75m, rateOverride.AppliedWageRate);

        Assert.NotNull(fixture.Hasher.LastPayloadJson);
        using (var canonical = JsonDocument.Parse(fixture.Hasher.LastPayloadJson))
        {
            var root = canonical.RootElement;
            Assert.Equal(
                LaborEndpoints.ConfirmEmployeeDailyWageCommandType,
                root.GetProperty("commandType").GetString());
            var command = root.GetProperty("command");
            Assert.Equal("2026-09-03", command.GetProperty("workDate").GetString());
            Assert.Equal(employeeId, command.GetProperty("employeeId").GetGuid());
            Assert.Equal(
                3.75m,
                command.GetProperty("processingWageRateOverrides")[0]
                    .GetProperty("appliedWageRate")
                    .GetDecimal());
        }

        using var responseJson = JsonDocument.Parse(response.Body);
        Assert.Equal(wageId, responseJson.RootElement.GetProperty("employeeDailyWageId").GetGuid());
        Assert.Equal(1, responseJson.RootElement.GetProperty("rowVersion").GetInt64());
        Assert.Equal(payableId, responseJson.RootElement.GetProperty("payableId").GetGuid());
        Assert.Equal(120, responseJson.RootElement.GetProperty("processingWageTotalThb").GetInt64());
        Assert.Equal(80, responseJson.RootElement.GetProperty("salesPackagingWageTotalThb").GetInt64());
        Assert.Equal(200, responseJson.RootElement.GetProperty("totalWageThb").GetInt64());
    }

    [Fact]
    public async Task Confirm_employee_daily_wage_rejects_missing_override_collection_as_transport_validation()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapLaborEndpoints();

        var response = await InvokeAsync(
            app,
            GetEndpoint(app),
            Guid.NewGuid(),
            $$"""{"workDate":"2026-09-03","employeeId":"{{Guid.NewGuid()}}","processingWageRateOverrides":null}""");

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Null(fixture.Executor.LastExecution);
        AssertProblemCode(response.Body, ApiErrorCodes.RequestValidationFailed);
    }

    [Fact]
    public async Task Confirm_employee_daily_wage_maps_existing_daily_wage_to_conflict_problem_details()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapLaborEndpoints();
        fixture.Executor.Result = ApplicationResult<ConfirmEmployeeDailyWageResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Conflict,
                LaborApplicationErrorCodes.DailyWageAlreadyConfirmed));

        var response = await InvokeValidAsync(app, Guid.NewGuid());

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        AssertProblemCode(response.Body, LaborApplicationErrorCodes.DailyWageAlreadyConfirmed);
    }

    [Fact]
    public async Task Confirm_employee_daily_wage_maps_invalid_rate_override_to_unprocessable_entity()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapLaborEndpoints();
        fixture.Executor.Result = ApplicationResult<ConfirmEmployeeDailyWageResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Validation,
                LaborApplicationErrorCodes.ProcessingRateOverrideInvalid));

        var response = await InvokeValidAsync(app, Guid.NewGuid());

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, response.StatusCode);
        AssertProblemCode(response.Body, LaborApplicationErrorCodes.ProcessingRateOverrideInvalid);
    }

    private static async Task<HttpInvocationResult> InvokeValidAsync(WebApplication app, Guid commandId)
    {
        var employeeId = Guid.NewGuid();
        return await InvokeAsync(
            app,
            GetEndpoint(app),
            commandId,
            $$"""{"workDate":"2026-09-03","employeeId":"{{employeeId}}","processingWageRateOverrides":[]}""");
    }

    private static void AssertProblemCode(string body, string expectedCode)
    {
        using var document = JsonDocument.Parse(body);
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
        Assert.Equal($"urn:yowthi:error:{expectedCode}", document.RootElement.GetProperty("type").GetString());
    }

    private static async Task<HttpInvocationResult> InvokeAsync(
        WebApplication app,
        RouteEndpoint endpoint,
        Guid commandId,
        string body)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        await using var requestBody = new MemoryStream(bodyBytes);
        await using var responseBody = new MemoryStream();

        var context = new DefaultHttpContext
        {
            RequestServices = app.Services,
        };
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new RequestBodyDetectionFeature());
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/v1/labor/daily-wages";
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
        var responseText = await reader.ReadToEndAsync();
        return new HttpInvocationResult(context.Response.StatusCode, responseText);
    }

    private static RouteEndpoint GetEndpoint(WebApplication app)
    {
        return Assert.Single(
            GetRouteEndpoints(app),
            endpoint => endpoint.RoutePattern.RawText == "/api/v1/labor/daily-wages");
    }

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

        var executor = new StubConfirmEmployeeDailyWageExecutor();
        var hasher = new CapturingRequestHasher();
        builder.Services.AddSingleton<IActorContext>(
            new StubActorContext(ActorAccountId.From(Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339b0"))));
        builder.Services.AddSingleton<ICommandRequestHasher>(hasher);
        builder.Services.AddSingleton<IConfirmEmployeeDailyWageExecutor>(executor);

        var app = builder.Build();
        app.UseYowThiApiInfrastructure();
        app.MapYowThiTechnicalEndpoints();
        return new AppFixture(app, executor, hasher);
    }

    private static RouteEndpoint[] GetRouteEndpoints(WebApplication app)
    {
        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
    }

    private sealed record AppFixture(
        WebApplication App,
        StubConfirmEmployeeDailyWageExecutor Executor,
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
                Enumerable.Repeat((byte)0x3A, CommandRequestHash.Sha256Length).ToArray());
        }
    }

    private sealed class StubConfirmEmployeeDailyWageExecutor : IConfirmEmployeeDailyWageExecutor
    {
        public ApplicationResult<ConfirmEmployeeDailyWageResult> Result { get; set; } =
            ApplicationResult<ConfirmEmployeeDailyWageResult>.Success(
                new ConfirmEmployeeDailyWageResult(
                    Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339b1"),
                    1,
                    Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339b2"),
                    0,
                    0,
                    0));

        public ConfirmEmployeeDailyWageExecution? LastExecution { get; private set; }

        public ValueTask<ApplicationResult<ConfirmEmployeeDailyWageResult>> ExecuteAsync(
            ConfirmEmployeeDailyWageExecution execution,
            CancellationToken cancellationToken)
        {
            LastExecution = execution;
            return ValueTask.FromResult(Result);
        }
    }
}
