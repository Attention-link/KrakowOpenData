using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Domain.Mobility;

/// <summary>Live vehicle location from GTFS-Realtime VehiclePositions.</summary>
public sealed record VehiclePosition(
    string Id,
    string? TripId,
    string? RouteId,
    GeoPoint Location,
    float? Bearing,
    float? SpeedMetersPerSecond,
    string? VehicleLabel,
    string? StopId,
    DateTimeOffset? Timestamp,
    string FeedKey,
    bool? WheelchairAccessible) : IEntity;
