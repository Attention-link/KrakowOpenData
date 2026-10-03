using System.Globalization;

namespace KrakowOpenData.Web.Components.Shared;

/// <summary>Language-neutral display helpers. Times are shown in Kraków local time. Words go through Translator.</summary>
public static class Format
{
    private static readonly TimeZoneInfo Krakow = Resolve();

    public static string Time(DateTimeOffset? moment) =>
        moment is null ? "–" : TimeZoneInfo.ConvertTime(moment.Value, Krakow).ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string DateTime(DateTimeOffset? moment) =>
        moment is null ? "–" : TimeZoneInfo.ConvertTime(moment.Value, Krakow).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static string Number(double? value, string unit = "", int decimals = 1) =>
        value is null ? "–" : value.Value.ToString("F" + decimals, CultureInfo.InvariantCulture) + unit;


    public static string DelayTone(int? seconds) => seconds switch
    {
        null => "neutral",
        <= 60 => "good",
        <= 300 => "warn",
        _ => "bad"
    };


    private static TimeZoneInfo Resolve()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Local; }
    }
}
