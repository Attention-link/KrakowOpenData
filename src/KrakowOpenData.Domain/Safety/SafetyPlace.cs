using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Domain.Safety;

/// <summary>
/// A place that matters for heat relief or night safety but is not a "public amenity": a park, a library,
/// a pharmacy, a hospital or a police station (OpenStreetMap).
/// </summary>
/// <param name="EquivalentRadiusMeters">
/// For parks only: half the side of a square with the same bounding-box area, so a large park counts as
/// reachable from its edge, not only from its centre point. Zero for point-like places.
/// </param>
/// <param name="Wheelchair">OSM wheelchair tag, normalised to yes | limited | no; null = no data (never "accessible").</param>
public sealed record SafetyPlace(
    string Id,
    SafetyPlaceKind Kind,
    string? Name,
    GeoPoint Location,
    string? OpeningHours,
    double EquivalentRadiusMeters,
    string Source,
    string? Wheelchair = null) : IEntity
{
    /// <summary>True when OSM says the place is open around the clock (<c>opening_hours=24/7</c>).</summary>
    public bool IsOpenAllNight => string.Equals(OpeningHours?.Trim(), "24/7", StringComparison.OrdinalIgnoreCase);
}

public enum SafetyPlaceKind
{
    Police,
    Hospital,
    Pharmacy,
    Library,
    Park,
    /// <summary>A point sampled along a river, stream or canal (about every 120 m).</summary>
    Waterway,
    /// <summary>A point sampled along a motorway, trunk or primary road (about every 120 m).</summary>
    MajorRoad
}
