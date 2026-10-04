using KrakowOpenData.Domain.Accessibility;

namespace KrakowOpenData.Application.Accessibility;

/// <summary>
/// A set of preferences about barriers, never a diagnosis: the app asks what to avoid, not about anybody's health.
/// Thresholds are ported from "Kraków bez barier" (web/src/prefs/model.ts). The profile only changes which barriers count
/// and how serious they are; the underlying data is the same for everyone.
/// </summary>
/// <param name="Key">wheelchair | pram | mobility.</param>
/// <param name="StrollerRampOk">A ramp tagged for prams (ramp:stroller) is good enough.</param>
/// <param name="GenericRampOk">A ramp without a type (ramp=yes) is good enough.</param>
/// <param name="StepsBlock">Steps without a suitable ramp are a serious barrier (No) rather than a difficulty (Limited).</param>
/// <param name="RaisedKerbBlocks">A raised kerb is a serious barrier (No) rather than a difficulty (Limited).</param>
/// <param name="UnpavedBlocks">Unpaved ground (gravel, dirt, grass) is a serious barrier.</param>
/// <param name="MaxInclinePercent">Slopes steeper than this are a barrier.</param>
/// <param name="MinWidthMeters">Passages and doors narrower than this are a barrier.</param>
public sealed record AccessProfile(
    string Key,
    bool StrollerRampOk,
    bool GenericRampOk,
    bool StepsBlock,
    bool RaisedKerbBlocks,
    bool UnpavedBlocks,
    double MaxInclinePercent,
    double MinWidthMeters)
{
    public static readonly AccessProfile Wheelchair = new("wheelchair", false, true, true, true, true, 6, 0.9);
    public static readonly AccessProfile Pram = new("pram", true, true, true, true, false, 8, 0.8);
    public static readonly AccessProfile Mobility = new("mobility", true, true, false, false, false, 8, 0.0);

    public static IReadOnlyList<AccessProfile> All { get; } = [Wheelchair, Pram, Mobility];

    /// <summary>The profile for a key (case-insensitive); null for anything else. Empty means wheelchair.</summary>
    public static AccessProfile? Parse(string? key) => string.IsNullOrWhiteSpace(key)
        ? Wheelchair
        : All.FirstOrDefault(p => string.Equals(p.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Decides the status of an item for a profile. Rules: a missing value is never read as accessible; a value that
/// is mapped and breaks the profile's limits is a barrier (No) or a difficulty (Limited); an explicit OSM
/// <c>wheelchair</c> tag wins for places, lifts, entrances and toilets.
/// </summary>
public static class AccessRules
{
    public static AccessStatus StatusOf(string? wheelchair) => wheelchair switch
    {
        "yes" => AccessStatus.Yes,
        "limited" => AccessStatus.Limited,
        "no" => AccessStatus.No,
        _ => AccessStatus.Unknown
    };

    public static AccessStatus Status(AccessKind kind, AccessAttributes a, AccessProfile p)
    {
        var hard = AccessStatus.No;
        switch (kind)
        {
            case AccessKind.Steps:
                if (RampSuits(a.Ramp, p)) return AccessStatus.Yes;
                if (a.Ramp is not null) return AccessStatus.Limited;   // a ramp, but not one tagged for this profile
                return p.StepsBlock ? hard : AccessStatus.Limited;

            case AccessKind.Kerb:
                return a.Kerb switch
                {
                    "flush" or "lowered" => AccessStatus.Yes,
                    "raised" => p.RaisedKerbBlocks ? hard : AccessStatus.Limited,
                    _ => AccessStatus.Unknown
                };

            case AccessKind.Elevator:
            case AccessKind.Place:
                return StatusOf(a.Wheelchair);

            case AccessKind.Entrance:
            {
                if (a.Wheelchair is not null) return StatusOf(a.Wheelchair);
                var stepsWithoutRamp = a.StepCount is > 0 && !RampSuits(a.Ramp, p);
                if (stepsWithoutRamp) return p.StepsBlock ? hard : AccessStatus.Limited;
                if (a.WidthMeters is { } w && w < p.MinWidthMeters) return hard;
                return AccessStatus.Unknown;
            }

            case AccessKind.Toilets:
            {
                var step = StatusOf(a.Wheelchair ?? a.ToiletsWheelchair);
                if (p.Key == "pram" && a.ChangingTable is { } table) return table ? AccessStatus.Yes : step == AccessStatus.Unknown ? AccessStatus.Limited : step;
                return step;
            }

            case AccessKind.Bench:
                return AccessStatus.Yes;   // a mapped place to rest; whether it has a backrest is shown as a fact

            case AccessKind.TactilePaving:
                return a.Tactile switch { true => AccessStatus.Yes, false => AccessStatus.No, _ => AccessStatus.Unknown };

            case AccessKind.Path:
                return PathStatus(a, p);

            default:
                return AccessStatus.Unknown;
        }
    }

    /// <summary>A path is judged by surface, slope and width; with none of these mapped it is unknown.</summary>
    public static AccessStatus PathStatus(AccessAttributes a, AccessProfile p)
    {
        if (a.InclinePercent is { } incline && incline > p.MaxInclinePercent) return p.StepsBlock ? AccessStatus.No : AccessStatus.Limited;
        if (a.WidthMeters is { } width && width < p.MinWidthMeters) return AccessStatus.No;
        return a.SurfaceClass switch
        {
            "unpaved" => p.UnpavedBlocks ? AccessStatus.No : AccessStatus.Limited,
            "paved_rough" => AccessStatus.Limited,
            "paved_smooth" => AccessStatus.Yes,
            _ => AccessStatus.Unknown   // slope or width within limits but no surface: still not known to be accessible
        };
    }

    public static bool RampSuits(string? ramp, AccessProfile p) => ramp switch
    {
        "wheelchair" => true,
        "stroller" => p.StrollerRampOk,
        "yes" => p.GenericRampOk,
        _ => false
    };
}
