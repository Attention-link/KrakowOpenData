using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Services;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Mobility;

namespace KrakowOpenData.Application.Tests;

public class TransitQueryServiceTests
{
    // Saturday 3 Oct 2026, 08:00 in Kraków (UTC+2 in October).
    private static readonly DateOnly Today = new(2026, 10, 3);
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 8, 0, 0, TimeSpan.FromHours(2));

    private readonly InMemoryRepository<TransitStop> _stops = new(
        new TransitStop("T:1", "1", "Rondo Mogilskie", new GeoPoint(50.0656, 19.9591), "T", true),
        new TransitStop("T:2", "2", "Dworzec Główny", new GeoPoint(50.0662, 19.9455), "T", true),
        new TransitStop("A:9", "9", "Kraków Łagiewniki", new GeoPoint(50.0270, 19.9330), "A", null));

    private readonly InMemoryRepository<TransitRoute> _routes = new(
        new TransitRoute("T:52", "52", null, TransportMode.Tram, "#1F6FEB", "T"),
        new TransitRoute("A:179", "179", null, TransportMode.Bus, null, "A"));

    private readonly InMemoryRepository<VehiclePosition> _vehicles = new();
    private readonly InMemoryRepository<TripUpdate> _updates = new();
    private readonly InMemoryRepository<ServiceAlert> _alerts = new();
    private readonly InMemoryRepository<ParkAndRideFacility> _parkAndRide = new();
    private readonly FakeScheduleRepository _schedule = new();
    private readonly FakeClock _clock = new(Now);

    private TransitQueryService CreateService() =>
        new(_stops, _routes, _vehicles, _updates, _alerts, _parkAndRide, _schedule, _clock);

    [Fact]
    public async Task SearchStops_ignores_diacritics_and_case()
    {
        var result = await CreateService().SearchStopsAsync("dworzec glowny", 1, 10);
        var stop = Assert.Single(result.Items);
        Assert.Equal("T:2", stop.Id);
    }

    [Fact]
    public async Task SearchStops_matches_l_with_stroke()
    {
        var result = await CreateService().SearchStopsAsync("lagiewniki", 1, 10);
        Assert.Equal("A:9", Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task SearchStops_pages_results()
    {
        var page2 = await CreateService().SearchStopsAsync(null, page: 2, pageSize: 2);
        Assert.Equal(3, page2.TotalCount);
        Assert.Equal(2, page2.TotalPages);
        Assert.Single(page2.Items);
    }

    [Fact]
    public async Task NearbyStops_are_sorted_by_distance_and_limited_by_radius()
    {
        var nearStation = new GeoPoint(50.0660, 19.9460);
        var result = await CreateService().NearbyStopsAsync(nearStation, 1500, 10);

        Assert.Equal(new[] { "T:2", "T:1" }, result.Select(s => s.Id).ToArray());
        Assert.True(result[0].DistanceMeters < result[1].DistanceMeters);
    }

    [Fact]
    public async Task Departures_combine_timetable_with_live_delay()
    {
        AddTrip("T:trip1", "T:52", "svc", "Krowodrza Górka");
        _schedule.StopTimes.Add(new StopTime("T:trip1", "T:1", 4, new TimeSpan(8, 10, 0), new TimeSpan(8, 10, 0)));
        _schedule.ActiveDays.Add(("svc", Today));
        _updates.Items.Add(new TripUpdate("tu1", "T:trip1", "T:52", 180, [], Now, "T"));

        var departures = await CreateService().GetDeparturesAsync("T:1", 10, TimeSpan.FromHours(1));

        var d = Assert.Single(departures);
        Assert.Equal("52", d.RouteShortName);
        Assert.Equal("Tram", d.Mode);
        Assert.Equal(180, d.DelaySeconds);
        Assert.True(d.IsRealtime);
        Assert.Equal(d.ScheduledDeparture.AddMinutes(3), d.ExpectedDeparture);
        Assert.Equal(KrakowTime.FromServiceDay(Today, new TimeSpan(8, 10, 0)), d.ScheduledDeparture);
    }

    [Fact]
    public async Task Departures_skip_trips_whose_service_does_not_run_today()
    {
        AddTrip("T:trip1", "T:52", "weekday-only", null);
        _schedule.StopTimes.Add(new StopTime("T:trip1", "T:1", 1, new TimeSpan(8, 10, 0), new TimeSpan(8, 10, 0)));

        Assert.Empty(await CreateService().GetDeparturesAsync("T:1", 10, TimeSpan.FromHours(1)));
    }

    [Fact]
    public async Task Departures_include_after_midnight_trips_from_yesterdays_service_day()
    {
        _clock.UtcNow = new DateTimeOffset(2026, 10, 3, 0, 30, 0, TimeSpan.FromHours(2)); // 00:30 local
        AddTrip("T:night", "T:52", "svc", null);
        _schedule.StopTimes.Add(new StopTime("T:night", "T:1", 1, new TimeSpan(24, 40, 0), new TimeSpan(24, 40, 0)));
        _schedule.ActiveDays.Add(("svc", Today.AddDays(-1)));

        var d = Assert.Single(await CreateService().GetDeparturesAsync("T:1", 10, TimeSpan.FromHours(1)));
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 0, 40, 0, TimeSpan.FromHours(2)), d.ScheduledDeparture);
        Assert.False(d.IsRealtime);
    }

    [Fact]
    public async Task Departures_are_ordered_by_expected_time_and_limited()
    {
        _schedule.ActiveDays.Add(("svc", Today));
        for (var i = 0; i < 5; i++)
        {
            AddTrip($"T:t{i}", "T:52", "svc", null);
            var time = new TimeSpan(8, 5 + i * 5, 0);
            _schedule.StopTimes.Add(new StopTime($"T:t{i}", "T:1", 1, time, time));
        }

        // The first trip is 20 minutes late, so it should drop behind others.
        _updates.Items.Add(new TripUpdate("tu", "T:t0", "T:52", 20 * 60, [], Now, "T"));

        var departures = await CreateService().GetDeparturesAsync("T:1", 3, TimeSpan.FromHours(1));

        Assert.Equal(3, departures.Count);
        Assert.Equal(new[] { "T:t1", "T:t2", "T:t3" }, departures.Select(d => d.TripId).ToArray());
    }

    [Fact]
    public async Task Departures_leave_out_stops_marked_skipped()
    {
        AddTrip("T:trip1", "T:52", "svc", null);
        _schedule.StopTimes.Add(new StopTime("T:trip1", "T:1", 2, new TimeSpan(8, 10, 0), new TimeSpan(8, 10, 0)));
        _schedule.ActiveDays.Add(("svc", Today));
        _updates.Items.Add(new TripUpdate("tu", "T:trip1", "T:52", null,
            [new StopTimeUpdate("T:1", 2, null, null, null, null, Skipped: true)], Now, "T"));

        Assert.Empty(await CreateService().GetDeparturesAsync("T:1", 10, TimeSpan.FromHours(1)));
    }

    [Fact]
    public async Task Vehicles_are_joined_with_route_names_and_filtered_by_route()
    {
        _vehicles.Items.Add(new VehiclePosition("T:v1", "T:trip1", "T:52", new GeoPoint(50.06, 19.94), null, 5f, "RZ123", null, Now, "T", true));
        _vehicles.Items.Add(new VehiclePosition("A:v2", "A:trip2", "A:179", new GeoPoint(50.05, 19.93), null, null, "BH001", null, Now, "A", null));

        var trams = await CreateService().GetVehiclesAsync("T:52");

        var v = Assert.Single(trams);
        Assert.Equal("52", v.RouteShortName);
        Assert.Equal("Tram", v.Mode);
        Assert.Equal(18f, v.SpeedKmh);
    }

    [Theory]
    [InlineData("T:52", 1)]    // the feed's route id
    [InlineData("t:52", 1)]    // case does not matter
    [InlineData("52", 1)]      // the line number people read on the vehicle
    [InlineData(" 52 ", 1)]    // spaces do not matter
    [InlineData("T52", 1)]     // mode letter + number
    [InlineData("t 52", 1)]
    [InlineData("179", 1)]
    [InlineData("A179", 1)]
    [InlineData("T179", 0)]    // the wrong mode letter does not match
    [InlineData("5", 0)]       // not a prefix match: line 5 is not line 52
    [InlineData("nope", 0)]
    public async Task Vehicles_can_be_filtered_by_route_id_or_line_number(string typed, int expected)
    {
        _vehicles.Items.Add(new VehiclePosition("T:v1", "T:trip1", "T:52", new GeoPoint(50.06, 19.94), null, 5f, "RZ123", null, Now, "T", true));
        _vehicles.Items.Add(new VehiclePosition("A:v2", "A:trip2", "A:179", new GeoPoint(50.05, 19.93), null, null, "BH001", null, Now, "A", null));

        Assert.Equal(expected, (await CreateService().GetVehiclesAsync(typed)).Count);
    }

    [Fact]
    public async Task A_blank_vehicle_filter_returns_every_vehicle()
    {
        _vehicles.Items.Add(new VehiclePosition("T:v1", "T:trip1", "T:52", new GeoPoint(50.06, 19.94), null, 5f, "RZ123", null, Now, "T", true));
        _vehicles.Items.Add(new VehiclePosition("A:v2", "A:trip2", "A:179", new GeoPoint(50.05, 19.93), null, null, "BH001", null, Now, "A", null));

        Assert.Equal(2, (await CreateService().GetVehiclesAsync("  ")).Count);
        Assert.Equal(2, (await CreateService().GetVehiclesAsync(null)).Count);
    }

    [Fact]
    public async Task Vehicles_without_valid_coordinates_are_dropped()
    {
        _vehicles.Items.Add(new VehiclePosition("T:v0", null, null, new GeoPoint(0, 0), null, null, null, null, Now, "T", null));
        Assert.Empty(await CreateService().GetVehiclesAsync(null));
    }

    [Fact]
    public async Task Only_alerts_active_now_are_returned()
    {
        _alerts.Items.Add(new ServiceAlert("a1", "Now", null, "CONSTRUCTION", "DETOUR",
            [new ActivePeriod(Now.AddHours(-1), Now.AddHours(1))], [], [], null, "T"));
        _alerts.Items.Add(new ServiceAlert("a2", "Later", null, "CONSTRUCTION", "DETOUR",
            [new ActivePeriod(Now.AddDays(1), Now.AddDays(2))], [], [], null, "T"));

        var active = await CreateService().GetActiveAlertsAsync();

        Assert.Equal("a1", Assert.Single(active).Id);
    }

    [Fact]
    public async Task Routes_can_be_filtered_by_mode()
    {
        var buses = await CreateService().GetRoutesAsync(TransportMode.Bus);
        Assert.Equal("179", Assert.Single(buses).ShortName);
    }

    private void AddTrip(string id, string routeId, string serviceId, string? headsign) =>
        _schedule.Trips[id] = new TransitTrip(id, routeId, serviceId, headsign, 0, "T", true);
}
