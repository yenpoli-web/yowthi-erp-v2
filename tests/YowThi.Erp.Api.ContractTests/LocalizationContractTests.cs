using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using YowThi.Erp.Api.Localization;

namespace YowThi.Erp.Api.ContractTests;

public sealed class LocalizationContractTests
{
    [Fact]
    public void Resolver_uses_highest_quality_supported_Accept_Language()
    {
        var resolver = new ApiLocaleResolver(Options.Create(new ApiLocalizationOptions
        {
            DefaultLocale = ApiLocales.ThTh,
        }));
        var request = new DefaultHttpContext().Request;
        request.Headers.AcceptLanguage = "fr-FR, th-TH;q=0.5, zh-TW;q=0.9";

        Assert.Equal(ApiLocales.ZhTw, resolver.Resolve(request));
    }

    [Fact]
    public void Resolver_uses_configured_default_when_no_supported_language_is_requested()
    {
        var resolver = new ApiLocaleResolver(Options.Create(new ApiLocalizationOptions
        {
            DefaultLocale = ApiLocales.ThTh,
        }));
        var request = new DefaultHttpContext().Request;
        request.Headers.AcceptLanguage = "en-US, fr-FR;q=0.8";

        Assert.Equal(ApiLocales.ThTh, resolver.Resolve(request));
    }

    [Fact]
    public void Supported_locales_are_exactly_zh_TW_and_th_TH()
    {
        Assert.Equal(new[] { "zh-TW", "th-TH" }, ApiLocales.Supported);
    }
}
