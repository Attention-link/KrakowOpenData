using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Domain.ClimateAndCrisis;

/// <summary>A meteorological or hydrological warning (IMGW). Level 1–3.</summary>
public sealed record WeatherWarning(
    string Id,
    string EventName,
    int Level,
    int? ProbabilityPercent,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    string? Content,
    IReadOnlyList<string> TerytCodes,
    string Source) : IEntity
{
    public bool IsActiveAt(DateTimeOffset moment) =>
        (ValidFrom is null || moment >= ValidFrom) && (ValidTo is null || moment <= ValidTo);

    /// <summary>
    /// True when the warning covers the area. TERYT codes are hierarchical, so county code "1261"
    /// (Kraków) matches warnings listed for "1261" or for the voivodeship "12".
    /// </summary>
    public bool AppliesTo(string terytCode) =>
        TerytCodes.Any(code =>
            terytCode.StartsWith(code, StringComparison.Ordinal) ||
            code.StartsWith(terytCode, StringComparison.Ordinal));
}
