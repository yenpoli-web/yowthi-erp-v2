using System.Globalization;
using Microsoft.Extensions.Options;

namespace YowThi.Erp.Api.Localization;

public sealed class ApiLocaleResolver(IOptions<ApiLocalizationOptions> options) : IApiLocaleResolver
{
    private readonly string defaultLocale = NormalizeConfiguredDefault(options.Value.DefaultLocale);

    public string Resolve(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var raw = request.Headers.AcceptLanguage.ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultLocale;
        }

        var candidates = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select((entry, index) => Parse(entry, index))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!.Value)
            .OrderByDescending(candidate => candidate.Quality)
            .ThenBy(candidate => candidate.Index);

        foreach (var candidate in candidates)
        {
            if (ApiLocales.TryNormalize(candidate.Locale, out var locale))
            {
                return locale;
            }
        }

        return defaultLocale;
    }

    private static (string Locale, decimal Quality, int Index)? Parse(string entry, int index)
    {
        var parts = entry.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts[0] == "*")
        {
            return null;
        }

        var quality = 1m;
        foreach (var part in parts.Skip(1))
        {
            if (!part.StartsWith("q=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!decimal.TryParse(part[2..], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out quality) || quality < 0m || quality > 1m)
            {
                quality = 0m;
            }
        }

        return (parts[0], quality, index);
    }

    private static string NormalizeConfiguredDefault(string configured)
    {
        if (ApiLocales.TryNormalize(configured, out var locale))
        {
            return locale;
        }

        throw new InvalidOperationException($"Configured API default locale '{configured}' is not supported.");
    }
}
