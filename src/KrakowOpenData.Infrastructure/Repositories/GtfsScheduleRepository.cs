using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Domain.Mobility;
using KrakowOpenData.Infrastructure.Gtfs;

namespace KrakowOpenData.Infrastructure.Repositories;

/// <summary>Timetable queries answered from the in-memory GTFS indexes.</summary>
public sealed class GtfsScheduleRepository(IGtfsDatasetProvider provider) : ITransitScheduleRepository
{
    public async Task<IReadOnlyList<StopTime>> GetStopTimesAtStopAsync(string stopId, CancellationToken cancellationToken = default)
    {
        var dataset = await provider.GetAsync(cancellationToken);
        return dataset.StopTimesByStop.TryGetValue(stopId, out var list) ? list : [];
    }

    public async Task<IReadOnlyList<StopTime>> GetStopTimesForTripAsync(string tripId, CancellationToken cancellationToken = default)
    {
        var dataset = await provider.GetAsync(cancellationToken);
        return dataset.StopTimesByTrip.TryGetValue(tripId, out var list) ? list : [];
    }

    public async Task<TransitTrip?> GetTripAsync(string tripId, CancellationToken cancellationToken = default)
    {
        var dataset = await provider.GetAsync(cancellationToken);
        return dataset.Trips.GetValueOrDefault(tripId);
    }

    private (GtfsDataset Dataset, IReadOnlySet<string> Stops)? _night;

    /// <summary>Night = departing from 23:00 to 04:30 (GTFS times past 24:00 count for the same service day).</summary>
    public async Task<IReadOnlySet<string>> GetNightServiceStopIdsAsync(CancellationToken cancellationToken = default)
    {
        var dataset = await provider.GetAsync(cancellationToken);
        if (_night is { } cached && ReferenceEquals(cached.Dataset, dataset)) return cached.Stops;

        var from = TimeSpan.FromHours(23);
        var until = TimeSpan.FromHours(28.5);
        var earlyUntil = TimeSpan.FromHours(4.5);
        var stops = dataset.StopTimesByStop
            .Where(kv => kv.Value.Any(s => (s.Departure >= from && s.Departure < until) || s.Departure < earlyUntil))
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.Ordinal);

        _night = (dataset, stops);
        return stops;
    }

    public async Task<bool> IsServiceActiveAsync(string serviceId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var dataset = await provider.GetAsync(cancellationToken);
        return dataset.IsServiceActive(serviceId, date);
    }
}
