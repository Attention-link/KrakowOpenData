namespace KrakowOpenData.Web.Localization;

/// <summary>Languages the UI is available in. The order here is the order of the switcher.</summary>
public enum AppLanguage
{
    Pl,
    En,
    Uk
}

public static class AppLanguages
{
    public static AppLanguage Default => AppLanguage.En;

    public static IReadOnlyList<AppLanguage> All { get; } = Enum.GetValues<AppLanguage>();

    /// <summary>BCP-47 code used for the html lang attribute and in storage: pl, en, uk.</summary>
    public static string Code(this AppLanguage language) => language switch
    {
        AppLanguage.Pl => "pl",
        AppLanguage.Uk => "uk",
        _ => "en"
    };

    /// <summary>Short label shown on the switcher. Ukraine is commonly shown as UA.</summary>
    public static string ShortLabel(this AppLanguage language) => language switch
    {
        AppLanguage.Pl => "PL",
        AppLanguage.Uk => "UA",
        _ => "EN"
    };

    /// <summary>The language's own name, shown as a tooltip.</summary>
    public static string NativeName(this AppLanguage language) => language switch
    {
        AppLanguage.Pl => "Polski",
        AppLanguage.Uk => "Українська",
        _ => "English"
    };

    /// <summary>Parses "pl", "pl-PL", "uk", "uk-UA", "ua", "en-GB" etc. Unknown values give null.</summary>
    public static AppLanguage? TryParse(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var prefix = code.Trim().ToLowerInvariant();
        if (prefix.Length > 2) prefix = prefix[..2];

        return prefix switch
        {
            "pl" => AppLanguage.Pl,
            "en" => AppLanguage.En,
            "uk" or "ua" => AppLanguage.Uk,
            _ => null
        };
    }
}
