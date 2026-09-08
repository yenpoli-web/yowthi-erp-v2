using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using YowThi.Erp.Api.Authorization;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Api.Idempotency;
using YowThi.Erp.Api.Localization;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Security;

namespace YowThi.Erp.Api.Hosting;

public static class ApiServiceCollectionExtensions
{
    private const string MaxRequestBodySizeKey = "Api:RequestLimits:MaxRequestBodySizeBytes";
    private const string DevelopmentTestAdminEnabledKey = "Security:DevelopmentTestAdmin:Enabled";

    public static WebApplicationBuilder AddYowThiApi(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var developmentTestAdminConfigured = builder.Configuration.GetValue<bool>(DevelopmentTestAdminEnabledKey);
        if (developmentTestAdminConfigured && !builder.Environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"{DevelopmentTestAdminEnabledKey} may only be enabled in the Development environment.");
        }

        builder.Services.AddSingleton(new SecurityRuntimeOptions(
            builder.Environment.IsDevelopment() && developmentTestAdminConfigured));

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Add(IPAddress.Loopback);
            options.KnownProxies.Add(IPAddress.IPv6Loopback);
        });

        builder.Services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Instance ??= context.HttpContext.Request.Path.Value;

                if (!context.ProblemDetails.Extensions.ContainsKey("traceId"))
                {
                    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
                }

                if (context.ProblemDetails.Extensions.TryGetValue("code", out var codeValue)
                    && codeValue is string code
                    && !string.IsNullOrWhiteSpace(code))
                {
                    context.ProblemDetails.Type = $"urn:yowthi:error:{code}";
                }
            };
        });

        builder.Services.AddOpenApi("v1");
        builder.Services.AddValidation();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IActorContext, HttpActorContext>();

        builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = ".YowThi.Erp.Session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.Events.OnRedirectToLogin = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/api/v1"))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }

                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/api/v1"))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }

                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                };
            });

        builder.Services.AddAuthorization(options =>
        {
            foreach (var capability in SecurityCapabilities.All)
            {
                options.AddPolicy(
                    capability,
                    policy => policy
                        .RequireAuthenticatedUser()
                        .RequireClaim(SecurityClaimTypes.Capability, capability));
            }
        });

        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = ".YowThi.Erp.Antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, _) =>
            {
                await ApiProblemResults.Create(
                        context.HttpContext,
                        StatusCodes.Status429TooManyRequests,
                        ApiErrorCodes.RequestRateLimitExceeded,
                        "Request rate limit exceeded.")
                    .ExecuteAsync(context.HttpContext);
            };
        });

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        });

        builder.Services
            .AddOptions<ApiLocalizationOptions>()
            .Bind(builder.Configuration.GetSection(ApiLocalizationOptions.SectionName))
            .Validate(
                options => ApiLocales.TryNormalize(options.DefaultLocale, out _),
                $"{ApiLocalizationOptions.SectionName}:DefaultLocale must be one of: {string.Join(", ", ApiLocales.Supported)}")
            .ValidateOnStart();

        builder.Services.AddSingleton<IApiLocaleResolver, ApiLocaleResolver>();
        builder.Services.AddTransient<IdempotencyKeyEndpointFilter>();

        var configuredBodyLimit = builder.Configuration.GetValue<long?>(MaxRequestBodySizeKey);
        if (configuredBodyLimit is <= 0)
        {
            throw new InvalidOperationException($"{MaxRequestBodySizeKey} must be greater than zero when configured.");
        }

        if (configuredBodyLimit.HasValue)
        {
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.MaxRequestBodySize = configuredBodyLimit.Value;
            });
        }

        return builder;
    }
}
