using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Domain.Accessibility;

/// <summary>
/// One concrete barrier or amenity for people who use a wheelchair, push a pram or walk with difficulty, taken from
/// OpenStreetMap: a flight of steps, a kerb, a lift, an entrance, a place with a wheelchair tag, a toilet, a bench,
/// tactile paving or a stretch of path with a known surface or slope.
/// <para>
/// Rules (ported from "Kraków bez barier"): a value that is not mapped stays <c>null</c>, never a default, and
/// <see cref="AccessStatus.Unknown"/> is never treated as accessible. <see cref="LastEdited"/> is the last edit of the
/// OSM element, not proof that anybody checked the tag on that day.
/// </para>
/// </summary>
/// <param name="Id">OSM reference, e.g. <c>node/123</c> or <c>way/456</c>.</param>
/// <param name="Status">What OSM says from a wheelchair user's point of view; the API re-evaluates it per profile.</param>
/// <param name="Line">For ways (steps, paths): the geometry, used to match a walking route. Null for points.</param>
public sealed record AccessFeature(
    string Id,
    AccessKind Kind,
    GeoPoint Location,
    string? Name,
    AccessStatus Status,
    AccessAttributes Attributes,
    DateTimeOffset? LastEdited,
    string Source,
    IReadOnlyList<GeoPoint>? Line = null) : IEntity
{
    public string OsmType => Id.Split('/')[0];

    public string OsmId => Id.Split('/').ElementAtOrDefault(1) ?? string.Empty;

    /// <summary>Link to the element on openstreetmap.org.</summary>
    public string OsmUrl => $"https://www.openstreetmap.org/{Id}";

    /// <summary>Link that opens the element in the OpenStreetMap editor, so anyone can correct it at the source.</summary>
    public string EditUrl => $"https://www.openstreetmap.org/edit?{OsmType}={OsmId}";
}

public enum AccessKind
{
    Steps,
    Kerb,
    Elevator,
    Entrance,
    /// <summary>A shop, office, museum, … with a <c>wheelchair</c> or <c>toilets:wheelchair</c> tag.</summary>
    Place,
    Toilets,
    Bench,
    TactilePaving,
    /// <summary>A footway, pedestrian street or road stretch with a surface, smoothness, slope or width tag.</summary>
    Path
}

/// <summary>Accessibility of one item. Unknown = no data: shown grey as "brak danych", never as accessible.</summary>
public enum AccessStatus
{
    Yes,
    Limited,
    No,
    Unknown
}

/// <summary>
/// The normalised OSM attributes behind an item. Every field is null when OSM does not say (never a guessed default).
/// </summary>
/// <param name="Wheelchair">yes | limited | no (designated → yes).</param>
/// <param name="Kerb">flush | lowered | raised (rolled → lowered, kerb=no → flush).</param>
/// <param name="Ramp">wheelchair | stroller | yes (ramp tags on steps or an entrance); <see cref="RampNone"/> when tagged "no".</param>
/// <param name="SurfaceClass">paved_smooth | paved_rough | unpaved.</param>
/// <param name="InclinePercent">Absolute slope in percent; a direction without a number ("up") is null.</param>
/// <param name="Category">For places: the raw OSM kind (amenity, shop, tourism, … value), e.g. "restaurant".</param>
public sealed record AccessAttributes(
    string? Wheelchair = null,
    string? Kerb = null,
    string? Ramp = null,
    bool RampNone = false,
    int? StepCount = null,
    bool? Handrail = null,
    string? SurfaceClass = null,
    string? SurfaceRaw = null,
    string? Smoothness = null,
    double? InclinePercent = null,
    double? WidthMeters = null,
    bool? Tactile = null,
    string? ToiletsWheelchair = null,
    bool? ChangingTable = null,
    string? Door = null,
    bool? Backrest = null,
    string? Highway = null,
    string? EntranceType = null,
    string? Description = null,
    string? Category = null)
{
    /// <summary>True when any accessibility attribute is mapped (used for coverage counts).</summary>
    public bool HasAccessData =>
        Wheelchair is not null || Kerb is not null || Ramp is not null || RampNone || StepCount is not null ||
        SurfaceClass is not null || InclinePercent is not null || WidthMeters is not null || Tactile is not null ||
        ToiletsWheelchair is not null || Door is not null;
}

/// <summary>
/// Reliability levels (same codes as "Kraków bez barier" docs/DATA-FORMAT.md):
/// <c>confirmed</c> (owner data or three independent confirmations; none yet), <c>osm_recent</c> (OSM element edited within
/// 24 months), <c>osm_old</c> (edited earlier), <c>user_unverified</c> (a resident report, shown separately), <c>unknown</c> (no data).
/// </summary>
public static class AccessReliability
{
    public const string Confirmed = "confirmed";
    public const string OsmRecent = "osm_recent";
    public const string OsmOld = "osm_old";
    public const string UserUnverified = "user_unverified";
    public const string Unknown = "unknown";

    public const int RecentMonths = 24;

    /// <summary>The level for an OSM item: unknown without data, otherwise recent or old by the element's last edit.</summary>
    public static string Of(bool hasData, DateTimeOffset? lastEdited, DateTimeOffset now)
    {
        if (!hasData) return Unknown;
        if (lastEdited is null) return OsmOld;
        return lastEdited.Value >= now.AddMonths(-RecentMonths) ? OsmRecent : OsmOld;
    }
}
