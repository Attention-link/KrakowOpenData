using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Domain.Mobility;

/// <summary>One scheduled run of a route from GTFS trips.txt.</summary>
public sealed record TransitTrip(
    string Id,
    string RouteId,
    string ServiceId,
    string? Headsign,
    int? DirectionId,
    string FeedKey,
    bool? WheelchairAccessible) : IEntity;

/// <summary>A scheduled call at a stop from GTFS stop_times.txt. Times may exceed 24h for after-midnight runs.</summary>
public sealed record StopTime(
    string TripId,
    string StopId,
    int StopSequence,
    TimeSpan Arrival,
    TimeSpan Departure);
