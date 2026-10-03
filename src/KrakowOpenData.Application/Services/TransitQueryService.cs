using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Contracts;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Mobility;

namespace KrakowOpenData.Application.Services;

/// <summary>Use cases for public transport data (Mobility category).</summary>
public sealed class TransitQueryService(
    IReadRepository<TransitStop> stops,
    IReadRepository<TransitRoute> routes,
    IReadRepository<VehiclePosition> vehicles,
    IReadRepository<TripUpdate> tripUpdates,
    IReadRepository<ServiceAlert> alerts,
    IReadRepository<ParkAndRideFacility> parkAndRide,
    ITransitScheduleRepository schedule,
    IClock clock)
{
    public async Task<PagedResult<StopDto>> SearchStopsAsync(
        string? query, int page, int pageSize, CancellationToken ct = default)
    {
        var spec = new Specification<TransitStop>(s =>
            TextNormalizer.ContainsAllWords(s.Name, query) ||
            string.Equals(s.Code, query, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.Id, query, StringComparison.OrdinalIgnoreCase));

        var matches = (await stops.ListAsync(spec, ct))
            .OrderBy(s => s.Name, StringComparer.CurrentCulture)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .Select(s => s.ToDto())
            .ToList();

        return PagedResult<StopDto>.From(matches, page, pageSize);
    }

    public async Task<StopDto?> GetStopAsync(string stopId, CancellationToken ct = default) =>
        (await stops.GetByIdAsync(stopId, ct))?.ToDto();

    public async Task<IReadOnlyList<StopDto>> NearbyStopsAsync(
        GeoPoint point, double radiusMeters, int limit, CancellationToken ct = default)
    {
        radiusMeters = Math.Clamp(radiusMeters, 1, 10_000);
        limit = Math.Clamp(limit, 1, 200);

        return (await stops.ListAsync(cancellationToken: ct))
            .Select(s => (Stop: s, Distance: s.Location.DistanceTo(point)))
            .Where(x => x.Distance <= radiusMeters)
            .OrderBy(x => x.Distance)
            .Take(limit)
            .Select(x => x.Stop.ToDto(x.Distance))
            .ToList();
    }

    public async Task<IReadOnlyList<RouteDto>> GetRoutesAsync(TransportMode? mode, CancellationToken ct = default)
    {
        var spec = mode is null ? null : new Specification<TransitRoute>(r => r.Mode == mode);
        return (await routes.ListAsync(spec, ct))
            .OrderBy(r => r.Mode)
            .ThenBy(r => r.ShortName.Length)
            .ThenBy(r => r.ShortName, StringComparer.Ordinal)
            .Select(r => r.ToDto())
            .ToList();
    }

    public async Task<IReadOnlyList<VehicleDto>> GetVehiclesAsync(string? routeId, CancellationToken ct = default)
    {
        var routeLookup = (await routes.ListAsync(cancellationToken: ct)).ToDictionary(r => r.Id);
        var spec = string.IsNullOrWhiteSpace(routeId)
            ? null
            : new Specification<VehiclePosition>(v => string.Equals(v.RouteId, routeId, StringComparison.OrdinalIgnoreCase));

        return (await vehicles.ListAsync(spec, ct))
            .Where(v => v.Location.IsValid)
            .Select(v => v.ToDto(v.RouteId is not null && routeLookup.TryGetValue(v.RouteId, out var r) ? r : null))
            .ToList();
    }

    public async Task<IReadOnlyList<TripUpdateDto>> GetTripUpdatesAsync(int? minDelaySeconds, CancellationToken ct = default)
    {
        var spec = minDelaySeconds is null
            ? null
            : new Specification<TripUpdate>(t => t.DelaySeconds >= minDelaySeconds);

        return (await tripUpdates.ListAsync(spec, ct))
            .OrderByDescending(t => t.DelaySeconds ?? 0)
            .Select(t => t.ToDto())
            .ToList();
    }

    public async Task<IReadOnlyList<AlertDto>> GetActiveAlertsAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        return (await alerts.ListAsync(new Specification<ServiceAlert>(a => a.IsActiveAt(now)), ct))
            .Select(a => a.ToDto())
            .ToList();
    }

    public async Task<IReadOnlyList<ParkAndRideDto>> GetParkAndRideAsync(CancellationToken ct = default) =>
        (await parkAndRide.ListAsync(cancellationToken: ct))
            .OrderBy(p => p.Name, StringComparer.CurrentCulture)
            .Select(p => p.ToDto())
            .ToList();

    /// <summary>
    /// Next departures from a stop: scheduled times for today's and yesterday's service day
    /// (after-midnight trips), adjusted by live TripUpdates where available.
    /// </summary>
    public async Task<IReadOnlyList<DepartureDto>> GetDeparturesAsync(
        string stopId, int limit, TimeSpan window, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var now = clock.UtcNow;
        var horizon = now + window;
        var today = KrakowTime.LocalDate(now);
        var serviceDays = new[] { today.AddDays(-1), today };

        var stopTimes = await schedule.GetStopTimesAtStopAsync(stopId, ct);
        if (stopTimes.Count == 0) return [];

        var updatesByTrip = (await tripUpdates.ListAsync(cancellationToken: ct))
            .GroupBy(t => t.TripId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.Timestamp).First());
        var routeLookup = (await routes.ListAsync(cancellationToken: ct)).ToDictionary(r => r.Id);

        var tripCache = new Dictionary<string, TransitTrip?>();
        var activeCache = new Dictionary<(string, DateOnly), bool>();
        var results = new List<DepartureDto>();

        foreach (var stopTime in stopTimes)
        {
            foreach (var serviceDay in serviceDays)
            {
                var scheduled = KrakowTime.FromServiceDay(serviceDay, stopTime.Departure);

                updatesByTrip.TryGetValue(stopTime.TripId, out var update);
                if (update?.IsStopSkipped(stopTime.StopId, stopTime.StopSequence) == true) continue;

                var delay = update?.DelayAtStop(stopTime.StopId, stopTime.StopSequence);
                var expected = scheduled.AddSeconds(delay ?? 0);

                // Keep departures that haven't left yet (small grace) and fall inside the window.
                if (expected < now.AddMinutes(-1) || scheduled > horizon) continue;

                if (!tripCache.TryGetValue(stopTime.TripId, out var trip))
                {
                    trip = await schedule.GetTripAsync(stopTime.TripId, ct);
                    tripCache[stopTime.TripId] = trip;
                }

                if (trip is null) continue;

                var activeKey = (trip.ServiceId, serviceDay);
                if (!activeCache.TryGetValue(activeKey, out var isActive))
                {
                    isActive = await schedule.IsServiceActiveAsync(trip.ServiceId, serviceDay, ct);
                    activeCache[activeKey] = isActive;
                }

                if (!isActive) continue;

                routeLookup.TryGetValue(trip.RouteId, out var route);
                results.Add(new DepartureDto(
                    trip.Id,
                    trip.RouteId,
                    route?.ShortName ?? trip.RouteId,
                    (route?.Mode ?? TransportMode.Other).ToString(),
                    trip.Headsign,
                    scheduled,
                    expected,
                    delay,
                    update is not null,
                    trip.WheelchairAccessible));
            }
        }

        return results
            .OrderBy(d => d.ExpectedDeparture)
            .Take(limit)
            .ToList();
    }
}
