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
using YowThi.Erp.Api.SalesHandling;
using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Idempotency;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;
using YowThi.Erp.Application.Common.Serialization;
using YowThi.Erp.Application.SalesHandling;

namespace YowThi.Erp.Api.ContractTests;

public sealed class SalesHandlingEndpointContractTests
{
    [Fact]
    public async Task Record_packaging_work_endpoint_matches_v1_contract()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapSalesHandlingEndpoints();

        var endpoint = GetEndpoint(app);

        Assert.Contains(HttpMethods.Post, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        Assert.Equal(
            SalesHandlingEndpoints.RecordPackagingWorkOperationId,
            endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            metadata => metadata.Policy == CapabilityPolicies.SalesHandlingWorkRecord);
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
    public async Task Record_packaging_work_builds_canonical_command_and_returns_created_response()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapSalesHandlingEndpoints();

        var salesId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var workRecordId = Guid.NewGuid();
        fixture.Executor.Result = ApplicationResult<RecordSalesPackagingWorkResult>.Success(
            new RecordSalesPackagingWorkResult(workRecordId, 1));

        var body = $$"""
        {
          "salesId": "{{salesId}}",
          "workDate": "2026-09-03",
          "employeeId": "{{employeeId}}",
          "salesPackagingItemId": "{{itemId}}",
          "confirmedWageThb": 125
        }
        """;

        var response = await InvokeAsync(app, GetEndpoint(app), Guid.NewGuid(), body);

        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        Assert.NotNull(fixture.Executor.LastExecution);
        Assert.Equal(salesId, fixture.Executor.LastExecution.Command.SalesId);
        Assert.Equal(new DateOnly(2026, 9, 3), fixture.Executor.LastExecution.Command.WorkDate);
        Assert.Equal(employeeId, fixture.Executor.LastExecution.Command.EmployeeId);
        Assert.Equal(itemId, fixture.Executor.LastExecution.Command.SalesPackagingItemId);
        Assert.Equal(125, fixture.Executor.LastExecution.Command.ConfirmedWageThb);

        Assert.NotNull(fixture.Hasher.LastPayloadJson);
        using (var canonical = JsonDocument.Parse(fixture.Hasher.LastPayloadJson))
        {
            var root = canonical.RootElement;
            Assert.Equal(
                SalesHandlingEndpoints.RecordPackagingWorkCommandType,
                root.GetProperty("commandType").GetString());
            var command = root.GetProperty("command");
            Assert.Equal(salesId, command.GetProperty("salesId").GetGuid());
            Assert.Equal("2026-09-03", command.GetProperty("workDate").GetString());
            Assert.Equal(employeeId, command.GetProperty("employeeId").GetGuid());
            Assert.Equal(itemId, command.GetProperty("salesPackagingItemId").GetGuid());
            Assert.Equal(125, command.GetProperty("confirmedWageThb").GetInt64());
        }

        using var responseJson = JsonDocument.Parse(response.Body);
        Assert.Equal(workRecordId, responseJson.RootElement.GetProperty("salesPackagingWorkRecordId").GetGuid());
        Assert.Equal(1, responseJson.RootElement.GetProperty("rowVersion").GetInt64());
    }

    [Fact]
    public async Task Record_packaging_work_rejects_empty_sales_id_as_transport_validation()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapSalesHandlingEndpoints();

        var response = await InvokeAsync(
            app,
            GetEndpoint(app),
            Guid.NewGuid(),
            $$"""{"salesId":"{{Guid.Empty}}","workDate":"2026-09-03","employeeId":"{{Guid.NewGuid()}}","salesPackagingItemId":"{{Guid.NewGuid()}}","confirmedWageThb":100}""");

        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Null(fixture.Executor.LastExecution);
        AssertProblemCode(response.Body, ApiErrorCodes.RequestValidationFailed);
    }

    [Fact]
    public async Task Record_packaging_work_maps_existing_daily_wage_to_conflict_problem_details()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapSalesHandlingEndpoints();
        fixture.Executor.Result = ApplicationResult<RecordSalesPackagingWorkResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Conflict,
                SalesHandlingApplicationErrorCodes.DailyWageAlreadyConfirmed));

        var response = await InvokeValidAsync(app, Guid.NewGuid(), 100);

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        AssertProblemCode(response.Body, SalesHandlingApplicationErrorCodes.DailyWageAlreadyConfirmed);
    }

    [Fact]
    public async Task Record_packaging_work_keeps_negative_wage_as_semantic_validation()
    {
        var fixture = CreateApp();
        await using var app = fixture.App;
        app.MapSalesHandlingEndpoints();
        fixture.Executor.Result = ApplicationResult<RecordSalesPackagingWorkResult>.Failure(
            ApplicationError.Create(
                ApplicationErrorKind.Validation,
                SalesHandlingApplicationErrorCodes.InvalidInput));

        var response = await InvokeValidAsync(app, Guid.NewGuid(), -1);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, response.StatusCode);
        Assert.NotNull(fixture.Executor.LastExecution);
        Assert.Equal(-1, fixture.Executor.LastExecution.Command.ConfirmedWageThb);
        AssertProblemCode(response.Body, SalesHandlingApplicationErrorCodes.InvalidInput);
    }

    private static async Task<HttpInvocationResult> InvokeValidAsync(
        WebApplication app,
        Guid commandId,
        long confirmedWageThb)
    {
        return await InvokeAsync(
            app,
            GetEndpoint(app),
            commandId,
            $$"""{"salesId":"{{Guid.NewGuid()}}","workDate":"2026-09-03","employeeId":"{{Guid.NewGuid()}}","salesPackagingItemId":"{{Guid.NewGuid()}}","confirmedWageThb":{{confirmedWageThb}}}""");
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
        context.Request.Path = "/api/v1/sales-handling/work-records";
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
            endpoint => endpoint.RoutePattern.RawText == "/api/v1/sales-handling/work-records");
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

        var executor = new StubRecordSalesPackagingWorkExecutor();
        var hasher = new CapturingRequestHasher();
        builder.Services.AddSingleton<IActorContext>(
            new StubActorContext(ActorAccountId.From(Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339c0"))));
        builder.Services.AddSingleton<ICommandRequestHasher>(hasher);
        builder.Services.AddSingleton<IRecordSalesPackagingWorkExecutor>(executor);

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
        StubRecordSalesPackagingWorkExecutor Executor,
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
                Enumerable.Repeat((byte)0x4A, CommandRequestHash.Sha256Length).ToArray());
        }
    }

    private sealed class StubRecordSalesPackagingWorkExecutor : IRecordSalesPackagingWorkExecutor
    {
        public ApplicationResult<RecordSalesPackagingWorkResult> Result { get; set; } =
            ApplicationResult<RecordSalesPackagingWorkResult>.Success(
                new RecordSalesPackagingWorkResult(
                    Guid.Parse("018f5ec7-3c42-7a91-92e8-c7732d8339c1"),
                    1));

        public RecordSalesPackagingWorkExecution? LastExecution { get; private set; }

        public ValueTask<ApplicationResult<RecordSalesPackagingWorkResult>> ExecuteAsync(
            RecordSalesPackagingWorkExecution execution,
            CancellationToken cancellationToken)
        {
            LastExecution = execution;
            return ValueTask.FromResult(Result);
        }
    }
}
