using System.Text.Json;
using System.Text.Json.Serialization;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Api.Idempotency;
using YowThi.Erp.Api.Localization;

namespace YowThi.Erp.Api.Hosting;

public static class ApiServiceCollectionExtensions
{
    private const string MaxRequestBodySizeKey = "Api:RequestLimits:MaxRequestBodySizeBytes";

    public static WebApplicationBuilder AddYowThiApi(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

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
        builder.Services.AddAuthorization();

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
