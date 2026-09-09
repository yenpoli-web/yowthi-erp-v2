using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Hosting;
using YowThi.Erp.Api.Idempotency;
using YowThi.Erp.Api.Security;
using YowThi.Erp.Application.Security;

namespace YowThi.Erp.Api.ContractTests;

public sealed class SecurityFoundationEndpointContractTests
{
    [Fact]
    public async Task Account_management_routes_require_security_account_manage_and_write_idempotency()
    {
        await using var app = CreateApp(Environments.Development);
        app.MapSecurityAccountEndpoints();

        var endpoints = GetRouteEndpoints(app);
        var list = Assert.Single(endpoints, endpoint => endpoint.RoutePattern.RawText == "/api/v1/security/accounts"
            && HasMethod(endpoint, HttpMethods.Get));
        var create = Assert.Single(endpoints, endpoint => endpoint.RoutePattern.RawText == "/api/v1/security/accounts"
            && HasMethod(endpoint, HttpMethods.Post));
        var update = Assert.Single(endpoints, endpoint => endpoint.RoutePattern.RawText == "/api/v1/security/accounts/{accountId:guid}/update");

        AssertPolicy(list, CapabilityPolicies.SecurityAccountManage);
        AssertPolicy(create, CapabilityPolicies.SecurityAccountManage);
        AssertPolicy(update, CapabilityPolicies.SecurityAccountManage);
        Assert.Null(list.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());
        Assert.NotNull(create.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());
        Assert.NotNull(update.Metadata.GetMetadata<RequiresIdempotencyKeyMetadata>());
    }

    [Fact]
    public async Task Development_passwordless_login_is_mapped_only_when_explicitly_enabled()
    {
        await using var disabled = CreateApp(Environments.Development, developmentTestAdminEnabled: false);
        disabled.MapYowThiAuthenticationEndpoints();
        Assert.DoesNotContain(
            GetRouteEndpoints(disabled),
            endpoint => endpoint.RoutePattern.RawText == "/auth/development-login");

        await using var enabled = CreateApp(Environments.Development, developmentTestAdminEnabled: true);
        enabled.MapYowThiAuthenticationEndpoints();
        var login = Assert.Single(
            GetRouteEndpoints(enabled),
            endpoint => endpoint.RoutePattern.RawText == "/auth/development-login");
        Assert.Contains(HttpMethods.Post, login.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        Assert.NotNull(login.Metadata.GetMetadata<IAllowAnonymous>());
    }

    [Fact]
    public async Task Development_deletion_reauthentication_is_mapped_only_when_test_admin_is_enabled_and_requires_authentication()
    {
        await using var disabled = CreateApp(Environments.Development, developmentTestAdminEnabled: false);
        disabled.MapYowThiAuthenticationEndpoints();
        Assert.DoesNotContain(
            GetRouteEndpoints(disabled),
            endpoint => endpoint.RoutePattern.RawText == "/auth/development-deletion-reauthenticate");

        await using var enabled = CreateApp(Environments.Development, developmentTestAdminEnabled: true);
        enabled.MapYowThiAuthenticationEndpoints();
        var endpoint = Assert.Single(
            GetRouteEndpoints(enabled),
            candidate => candidate.RoutePattern.RawText == "/auth/development-deletion-reauthenticate");

        Assert.Contains(HttpMethods.Post, endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []);
        Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
    }

    [Fact]
    public async Task Deletion_reauthentication_filter_marks_destructive_endpoint_contract()
    {
        await using var app = CreateApp(Environments.Development);
        app.MapPost("/test-delete", static () => Results.NoContent())
            .RequireRecentDeletionReauthentication();

        var endpoint = Assert.Single(
            GetRouteEndpoints(app),
            candidate => candidate.RoutePattern.RawText == "/test-delete");

        Assert.NotNull(endpoint.Metadata.GetMetadata<RequiresDeletionReauthenticationMetadata>());
    }

    [Fact]
    public async Task Authentication_session_is_anonymous_but_logout_requires_authentication()
    {
        await using var app = CreateApp(Environments.Development, developmentTestAdminEnabled: true);
        app.MapYowThiAuthenticationEndpoints();

        var session = Assert.Single(
            GetRouteEndpoints(app),
            endpoint => endpoint.RoutePattern.RawText == "/auth/session");
        var logout = Assert.Single(
            GetRouteEndpoints(app),
            endpoint => endpoint.RoutePattern.RawText == "/auth/logout");

        Assert.NotNull(session.Metadata.GetMetadata<IAllowAnonymous>());
        Assert.NotEmpty(logout.Metadata.GetOrderedMetadata<IAuthorizeData>());
    }

    [Fact]
    public void Production_rejects_development_test_admin_enablement()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = Environments.Production,
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Api:Localization:DefaultLocale"] = "zh-TW",
            ["Security:DevelopmentTestAdmin:Enabled"] = "true",
        });

        Assert.Throws<InvalidOperationException>(() => builder.AddYowThiApi());
    }

    [Fact]
    public async Task Deletion_reauthentication_freshness_defaults_to_two_minutes_and_rejects_unsafe_configuration()
    {
        await using var app = CreateApp(Environments.Development);
        var runtimeOptions = app.Services.GetRequiredService<SecurityRuntimeOptions>();
        Assert.Equal(TimeSpan.FromMinutes(2), runtimeOptions.DeletionReauthenticationMaxAge);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = Environments.Development,
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Api:Localization:DefaultLocale"] = "zh-TW",
            ["Security:DeletionReauthentication:MaxAgeSeconds"] = "10",
        });

        Assert.Throws<InvalidOperationException>(() => builder.AddYowThiApi());
    }

    [Fact]
    public async Task Development_cookie_security_supports_loopback_http_while_production_stays_secure()
    {
        await using var development = CreateApp(Environments.Development);
        var developmentCookie = development.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var developmentAntiforgery = development.Services
            .GetRequiredService<IOptions<AntiforgeryOptions>>()
            .Value;

        Assert.Equal(CookieSecurePolicy.SameAsRequest, developmentCookie.Cookie.SecurePolicy);
        Assert.Equal(CookieSecurePolicy.SameAsRequest, developmentAntiforgery.Cookie.SecurePolicy);

        await using var production = CreateApp(Environments.Production);
        var productionCookie = production.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var productionAntiforgery = production.Services
            .GetRequiredService<IOptions<AntiforgeryOptions>>()
            .Value;

        Assert.Equal(CookieSecurePolicy.Always, productionCookie.Cookie.SecurePolicy);
        Assert.Equal(CookieSecurePolicy.Always, productionAntiforgery.Cookie.SecurePolicy);
    }

    [Fact]
    public void Security_capability_registry_is_explicit_and_contains_account_management()
    {
        Assert.Equal("security.account.manage", SecurityCapabilities.SecurityAccountManage);
        Assert.Equal(29, SecurityCapabilities.All.Count);
        Assert.Contains(SecurityCapabilities.ProcurementTransactionLifecycle, SecurityCapabilities.All);
        Assert.Contains(SecurityCapabilities.ProcessingTransactionLifecycle, SecurityCapabilities.All);
        Assert.Contains(SecurityCapabilities.ProcurementProductManage, SecurityCapabilities.All);
        Assert.Contains(SecurityCapabilities.SalesProductManage, SecurityCapabilities.All);
        Assert.Contains(SecurityCapabilities.InfrastructureWarehouseManage, SecurityCapabilities.All);
        Assert.Contains(SecurityCapabilities.InfrastructureStorageLocationManage, SecurityCapabilities.All);
        Assert.Contains(SecurityCapabilities.InventoryView, SecurityCapabilities.All);
        Assert.Equal(SecurityCapabilities.All.Count, SecurityCapabilities.All.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("*", SecurityCapabilities.All);
    }

    private static WebApplication CreateApp(string environment, bool developmentTestAdminEnabled = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = environment,
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Api:Localization:DefaultLocale"] = "zh-TW",
            ["Security:DevelopmentTestAdmin:Enabled"] = developmentTestAdminEnabled ? "true" : "false",
        });
        builder.AddYowThiApi();
        return builder.Build();
    }

    private static RouteEndpoint[] GetRouteEndpoints(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

    private static bool HasMethod(RouteEndpoint endpoint, string method) =>
        endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase) == true;

    private static void AssertPolicy(RouteEndpoint endpoint, string policy) =>
        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            metadata => metadata.Policy == policy);
}
