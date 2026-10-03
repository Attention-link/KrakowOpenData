using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Mobility;
using KrakowOpenData.Infrastructure.Gtfs;
using KrakowOpenData.Infrastructure.GtfsRealtime;

namespace KrakowOpenData.Infrastructure.Sample;

/// <summary>
/// Built-in SAMPLE transit network for offline demos and tests. Stop names are real Kraków places
/// with approximate coordinates; lines "S1" and "S2" and all times are invented. Never present
/// this as real ZTP data.
/// </summary>
public static class SampleTransitData
{
    public const string FeedKey = "S";

    private static readonly (string Id, string Name, double Lat, double Lon)[] SampleStops =
    [
        ("1", "Krowodrza Górka", 50.0868, 19.9305),
        ("2", "Politechnika", 50.0711, 19.9432),
        ("3", "Dworzec Główny", 50.0662, 19.9455),
        ("4", "Rondo Mogilskie", 50.0656, 19.9591),
        ("5", "Teatr Bagatela", 50.0632, 19.9326),
        ("6", "Plac Wszystkich Świętych", 50.0585, 19.9386),
        ("7", "Rondo Grunwaldzkie", 50.0487, 19.9300)
    ];

    // Ordered stop ids per line (outbound direction; inbound is the reverse).
    private static readonly (string RouteId, string ShortName, int RouteType, string Color, string[] Stops)[] SampleLines =
    [
        ("S1", "S1", 0, "1F6FEB", ["1", "2", "3", "6", "7"]),
        ("S2", "S2", 3, "D97706", ["4", "3", "5", "6", "7"])
    ];

    private static readonly Lazy<GtfsDataset> LazyDataset = new(Build);

    public static GtfsDataset Dataset => LazyDataset.Value;

    private static GtfsDataset Build()
    {
        var d = new GtfsDataset();
        string P(string id) => GtfsStaticParser.Prefix(FeedKey, id);

        foreach (var s in SampleStops)
        {
            d.Stops[P(s.Id)] = new TransitStop(P(s.Id), s.Id, s.Name, new GeoPoint(s.Lat, s.Lon), FeedKey, true);
        }

        var everyDay = new HashSet<DayOfWeek>(Enum.GetValues<DayOfWeek>());
        d.Calendars[P("daily")] = new ServiceCalendar(P("daily"), everyDay, new DateOnly(2020, 1, 1), new DateOnly(2099, 12, 31));

        foreach (var line in SampleLines)
        {
            d.Routes[P(line.RouteId)] = new TransitRoute(
                P(line.RouteId), line.ShortName, $"Sample line {line.ShortName}",
                TransportModeExtensions.FromGtfsRouteType(line.RouteType), "#" + line.Color, FeedKey);

            for (var direction = 0; direction < 2; direction++)
            {
                var stops = direction == 0 ? line.Stops : Enumerable.Reverse(line.Stops).ToArray();
                var headsign = d.Stops[P(stops[^1])].Name;

                // Every 10 minutes from 05:00 to 23:50, plus one after-midnight run at 24:20.
                var starts = Enumerable.Range(0, 114).Select(i => TimeSpan.FromHours(5) + TimeSpan.FromMinutes(10 * i))
                    .Append(new TimeSpan(24, 20, 0));

                foreach (var start in starts)
                {
                    var tripId = P($"{line.RouteId}-{direction}-{start.TotalMinutes:0000}");
                    d.Trips[tripId] = new TransitTrip(tripId, P(line.RouteId), P("daily"), headsign, direction, FeedKey, true);

                    for (var i = 0; i < stops.Length; i++)
                    {
                        var time = start + TimeSpan.FromMinutes(3 * i);
                        d.AddStopTime(new StopTime(tripId, P(stops[i]), i + 1, time, time));
                    }
                }
            }
        }

        d.Seal();
        return d;
    }

    /// <summary>
    /// Simulated realtime snapshot: a vehicle for each trip currently running (interpolated between
    /// stops), a deterministic delay per trip, and one sample alert.
    /// </summary>
    public static GtfsRealtimeFeed BuildRealtime(DateTimeOffset now)
    {
        var dataset = Dataset;
        var today = KrakowTime.LocalDate(now);
        var vehicles = new List<VehiclePosition>();
        var updates = new List<TripUpdate>();

        foreach (var (tripId, stopTimes) in dataset.StopTimesByTrip)
        {
            var trip = dataset.Trips[tripId];
            foreach (var serviceDay in new[] { today.AddDays(-1), today })
            {
                var first = KrakowTime.FromServiceDay(serviceDay, stopTimes[0].Departure);
                var last = KrakowTime.FromServiceDay(serviceDay, stopTimes[^1].Arrival);
                if (now < first || now > last) continue;

                var delaySeconds = (Math.Abs(StableHash(tripId)) % 5) * 60; // 0–4 minutes
                var shifted = now.AddSeconds(-delaySeconds);
                var position = Interpolate(dataset, stopTimes, serviceDay, shifted);

                var vehicleId = GtfsStaticParser.Prefix(FeedKey, $"V{Math.Abs(StableHash(tripId)) % 900 + 100}");
                vehicles.Add(new VehiclePosition(
                    vehicleId, tripId, trip.RouteId, position, null, 6.5f, vehicleId[2..], null, now, FeedKey, true));

                updates.Add(new TripUpdate(
                    GtfsStaticParser.Prefix(FeedKey, "tu-" + tripId), tripId, trip.RouteId, delaySeconds,
                    [], now, FeedKey));
            }
        }

        var alert = new ServiceAlert(
            GtfsStaticParser.Prefix(FeedKey, "alert-1"),
            "SAMPLE: Line S2 diverted via Rondo Mogilskie",
            "Sample alert for demos. Real alerts come from ServiceAlerts_*.pb.",
            "CONSTRUCTION",
            "DETOUR",
            [new ActivePeriod(now.AddHours(-1), now.AddHours(6))],
            [GtfsStaticParser.Prefix(FeedKey, "S2")],
            [GtfsStaticParser.Prefix(FeedKey, "5")],
            null,
            FeedKey);

        return new GtfsRealtimeFeed(now, vehicles, updates, [alert]);
    }

    private static GeoPoint Interpolate(GtfsDataset dataset, List<StopTime> stopTimes, DateOnly serviceDay, DateTimeOffset moment)
    {
        for (var i = 0; i < stopTimes.Count - 1; i++)
        {
            var a = KrakowTime.FromServiceDay(serviceDay, stopTimes[i].Departure);
            var b = KrakowTime.FromServiceDay(serviceDay, stopTimes[i + 1].Arrival);
            if (moment > b) continue;

            var from = dataset.Stops[stopTimes[i].StopId].Location;
            var to = dataset.Stops[stopTimes[i + 1].StopId].Location;
            var span = (b - a).TotalSeconds;
            var t = span <= 0 ? 0 : Math.Clamp((moment - a).TotalSeconds / span, 0, 1);
            return new GeoPoint(
                from.Latitude + (to.Latitude - from.Latitude) * t,
                from.Longitude + (to.Longitude - from.Longitude) * t);
        }

        return dataset.Stops[stopTimes[^1].StopId].Location;
    }

    /// <summary>string.GetHashCode is randomised per process; this one is stable.</summary>
    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 23;
            foreach (var c in value) hash = hash * 31 + c;
            return hash == int.MinValue ? 0 : hash;
        }
    }
}

/// <summary>Sample-mode timetable provider.</summary>
public sealed class SampleGtfsDatasetProvider : IGtfsDatasetProvider
{
    public Task<GtfsDataset> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(SampleTransitData.Dataset);
}

/// <summary>Sample-mode realtime provider; regenerates the simulation on every call.</summary>
public sealed class SampleRealtimeFeedProvider(IClock clock) : IGtfsRealtimeFeedProvider
{
    public Task<GtfsRealtimeFeed> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(SampleTransitData.BuildRealtime(clock.UtcNow));
}
