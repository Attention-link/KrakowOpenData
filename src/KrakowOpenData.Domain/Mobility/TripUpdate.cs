using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Domain.Mobility;

/// <summary>Live delay information for one trip from GTFS-Realtime TripUpdates.</summary>
public sealed record TripUpdate(
    string Id,
    string TripId,
    string? RouteId,
    int? DelaySeconds,
    IReadOnlyList<StopTimeUpdate> StopTimeUpdates,
    DateTimeOffset? Timestamp,
    string FeedKey) : IEntity
{
    /// <summary>
    /// Delay expected at a stop. Follows the GTFS-Realtime propagation rule: the most recent
    /// update at or before the stop's sequence applies downstream; falls back to the trip-level delay.
    /// </summary>
    public int? DelayAtStop(string stopId, int stopSequence)
    {
        var exact = StopTimeUpdates.FirstOrDefault(u =>
            (u.StopSequence.HasValue && u.StopSequence.Value == stopSequence) ||
            (!u.StopSequence.HasValue && u.StopId == stopId));
        if (exact is not null) return exact.Skipped ? null : exact.EffectiveDelaySeconds ?? DelaySeconds;

        var upstream = StopTimeUpdates
            .Where(u => u.StopSequence.HasValue && u.StopSequence.Value < stopSequence && !u.Skipped)
            .OrderByDescending(u => u.StopSequence)
            .FirstOrDefault(u => u.EffectiveDelaySeconds.HasValue);

        return upstream?.EffectiveDelaySeconds ?? DelaySeconds;
    }

    public bool IsStopSkipped(string stopId, int stopSequence) =>
        StopTimeUpdates.Any(u => u.Skipped &&
            (u.StopSequence == stopSequence || (!u.StopSequence.HasValue && u.StopId == stopId)));
}

public sealed record StopTimeUpdate(
    string? StopId,
    int? StopSequence,
    int? ArrivalDelaySeconds,
    int? DepartureDelaySeconds,
    DateTimeOffset? ArrivalTime,
    DateTimeOffset? DepartureTime,
    bool Skipped)
{
    public int? EffectiveDelaySeconds => DepartureDelaySeconds ?? ArrivalDelaySeconds;
}
