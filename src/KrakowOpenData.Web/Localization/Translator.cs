using System.Globalization;

namespace KrakowOpenData.Web.Localization;

/// <summary>
/// Looks up UI text in one language. Missing translations fall back to English, then to the key
/// itself, so a forgotten entry shows up on screen instead of crashing the page.
/// One cached instance per language; cascaded to every component by MainLayout.
/// </summary>
public sealed class Translator
{
    private static readonly Dictionary<AppLanguage, Translator> Cache =
        AppLanguages.All.ToDictionary(l => l, l => new Translator(l));

    private Translator(AppLanguage language)
    {
        Language = language;
        Culture = CultureInfo.GetCultureInfo(language.Code());
    }

    public static Translator English => For(AppLanguage.En);

    public AppLanguage Language { get; }

    public CultureInfo Culture { get; }

    public static Translator For(AppLanguage language) => Cache[language];

    public string this[string key] => Lookup(key) ?? key;

    /// <summary>Formats a translation with arguments, e.g. T.F("stops.count", 12).</summary>
    public string F(string key, params object?[] args) => string.Format(Culture, this[key], args);

    /// <summary>Translation for a key built from data (an enum value, a dataset key), or the fallback.</summary>
    public string Or(string key, string? fallback) => Lookup(key) ?? fallback ?? key;

    public bool Has(string key) => Lookup(key) is not null;

    // ── Display helpers that need translated words ─────────────────────────────

    public string Delay(int? seconds) => seconds switch
    {
        null => "–",
        0 => this["delay.onTime"],
        > 0 => F("delay.minutes", "+" + Math.Round(seconds.Value / 60.0).ToString(CultureInfo.InvariantCulture)),
        < 0 => F("delay.minutes", Math.Round(seconds.Value / 60.0).ToString(CultureInfo.InvariantCulture))
    };

    public string YesNo(bool? value) => value switch
    {
        true => this["common.yes"],
        false => this["common.no"],
        null => this["common.unknown"]
    };

    public string Mode(string mode) => Or("mode." + mode, mode);

    private string? Lookup(string key)
    {
        if (!UiText.Entries.TryGetValue(key, out var texts)) return null;
        var text = texts[(int)Language];
        return string.IsNullOrEmpty(text) ? texts[(int)AppLanguage.En] : text;
    }
}
