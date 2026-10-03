using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Domain.Mobility;

/// <summary>A stop or platform from GTFS stops.txt. Id is prefixed with the feed key, e.g. "T:123".</summary>
public sealed record TransitStop(
    string Id,
    string? Code,
    string Name,
    GeoPoint Location,
    string FeedKey,
    bool? WheelchairAccessible) : IEntity;
