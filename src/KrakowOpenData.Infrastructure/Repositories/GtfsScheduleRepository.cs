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

    public async Task<bool> IsServiceActiveAsync(string serviceId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var dataset = await provider.GetAsync(cancellationToken);
        return dataset.IsServiceActive(serviceId, date);
    }
}
