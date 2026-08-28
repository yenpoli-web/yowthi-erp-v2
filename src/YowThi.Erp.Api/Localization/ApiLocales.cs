namespace YowThi.Erp.Api.Localization;

public static class ApiLocales
{
    public const string ZhTw = "zh-TW";
    public const string ThTh = "th-TH";

    public static IReadOnlyList<string> Supported { get; } = [ZhTw, ThTh];

    public static bool TryNormalize(string? value, out string locale)
    {
        if (string.Equals(value, ZhTw, StringComparison.OrdinalIgnoreCase))
        {
            locale = ZhTw;
            return true;
        }

        if (string.Equals(value, ThTh, StringComparison.OrdinalIgnoreCase))
        {
            locale = ThTh;
            return true;
        }

        locale = string.Empty;
        return false;
    }
}
