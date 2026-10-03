using KrakowOpenData.Domain.Mobility;

namespace KrakowOpenData.Application.Abstractions;

/// <summary>Timetable queries that don't fit a simple list-by-entity repository.</summary>
public interface ITransitScheduleRepository
{
    /// <summary>All scheduled calls at a stop, ordered by departure time.</summary>
    Task<IReadOnlyList<StopTime>> GetStopTimesAtStopAsync(string stopId, CancellationToken cancellationToken = default);

    /// <summary>All scheduled calls of one trip, ordered by stop sequence.</summary>
    Task<IReadOnlyList<StopTime>> GetStopTimesForTripAsync(string tripId, CancellationToken cancellationToken = default);

    Task<TransitTrip?> GetTripAsync(string tripId, CancellationToken cancellationToken = default);

    Task<bool> IsServiceActiveAsync(string serviceId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ids of stops that have at least one scheduled departure at night (23:00–04:30, on any service day).
    /// Used by the night-safety score to find stops that are still served after dark.
    /// </summary>
    Task<IReadOnlySet<string>> GetNightServiceStopIdsAsync(CancellationToken cancellationToken = default);
}
