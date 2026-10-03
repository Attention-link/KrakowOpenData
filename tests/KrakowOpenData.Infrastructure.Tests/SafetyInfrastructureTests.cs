using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Mobility;
using KrakowOpenData.Domain.Safety;
using KrakowOpenData.Infrastructure.Gtfs;
using KrakowOpenData.Infrastructure.OpenStreetMap;
using KrakowOpenData.Infrastructure.Options;
using KrakowOpenData.Infrastructure.Repositories;
using KrakowOpenData.Infrastructure.Safety;
using Microsoft.Extensions.Logging.Abstractions;

namespace KrakowOpenData.Infrastructure.Tests;

public class SafetyPlacesParserTests
{
    private const string Json = """
    {"elements":[
      {"type":"node","id":1,"lat":50.06,"lon":19.93,"tags":{"amenity":"pharmacy","name":"Apteka Nocna","opening_hours":"24/7"}},
      {"type":"way","id":2,"bounds":{"minlat":50.060,"minlon":19.930,"maxlat":50.064,"maxlon":19.938},"tags":{"leisure":"park","name":"Park Testowy"}},
      {"type":"way","id":3,"bounds":{"minlat":50.0,"minlon":19.0,"maxlat":51.0,"maxlon":21.0},"tags":{"leisure":"park","name":"Huge"}},
      {"type":"relation","id":4,"bounds":{"minlat":50.070,"minlon":19.940,"maxlat":50.071,"maxlon":19.941},"tags":{"amenity":"hospital","name":"Szpital"}},
      {"type":"node","id":5,"lat":50.05,"lon":19.92,"tags":{"amenity":"police"}},
      {"type":"node","id":6,"lat":50.05,"lon":19.92,"tags":{"amenity":"library","name":"Biblioteka"}},
      {"type":"node","id":7,"lat":50.05,"lon":19.92,"tags":{"shop":"bakery"}},
      {"type":"node","id":8,"tags":{"amenity":"pharmacy"}},
      {"type":"node","id":9,"lat":50.05,"lon":19.92}
    ]}
    """;

    [Fact]
    public void Maps_places_and_skips_elements_it_cannot_use()
    {
        var places = SafetyPlacesParser.Parse(Json);

        Assert.Equal(new[] { "osm-n1", "osm-w2", "osm-w3", "osm-r4", "osm-n5", "osm-n6" }, places.Select(p => p.Id).ToArray());
        Assert.Equal(
            new[] { SafetyPlaceKind.Pharmacy, SafetyPlaceKind.Park, SafetyPlaceKind.Park, SafetyPlaceKind.Hospital, SafetyPlaceKind.Police, SafetyPlaceKind.Library },
            places.Select(p => p.Kind).ToArray());
    }

    [Fact]
    public void Rivers_and_main_roads_are_sampled_into_points_along_their_geometry()
    {
        // About 0.0045° of latitude is 500 m: one point at the start, then one about every 120 m (4 more), none for other roads.
        const string lines = """
        {"elements":[
          {"type":"way","id":10,"tags":{"waterway":"river","name":"Wisła"},
           "geometry":[{"lat":50.0000,"lon":19.9},{"lat":50.0045,"lon":19.9}]},
          {"type":"way","id":11,"tags":{"highway":"primary"},
           "geometry":[{"lat":50.01,"lon":19.9},{"lat":50.01,"lon":19.9005}]},
          {"type":"way","id":12,"tags":{"highway":"residential"},
           "geometry":[{"lat":50.02,"lon":19.9},{"lat":50.02,"lon":19.901}]},
          {"type":"way","id":13,"tags":{"waterway":"drain"},
           "geometry":[{"lat":50.03,"lon":19.9},{"lat":50.03,"lon":19.901}]}
        ]}
        """;

        var places = SafetyPlacesParser.Parse(lines);
        var river = places.Where(p => p.Kind == SafetyPlaceKind.Waterway).ToList();
        var road = places.Where(p => p.Kind == SafetyPlaceKind.MajorRoad).ToList();

        Assert.Equal(5, river.Count);                        // 0, 120, 240, 360, 480 m of a 500 m line
        Assert.All(river, p => Assert.Equal("Wisła", p.Name));
        Assert.Equal(50.0000, river[0].Location.Latitude, 6);
        Assert.InRange(river[1].Location.DistanceTo(river[0].Location), 115, 125);
        Assert.Single(road);                                 // a 35 m road: only its start
        Assert.Equal(places.Count, river.Count + road.Count);   // residential roads and drains are ignored
        Assert.Equal(places.Count, places.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void The_query_asks_for_waterways_and_main_roads_with_geometry()
    {
        var query = SafetyPlacesParser.BuildQuery("Kraków");
        Assert.Contains("waterway", query);
        Assert.Contains("motorway|trunk|primary", query);
        Assert.Contains("out geom", query);
    }

    [Fact]
    public void A_pharmacy_open_around_the_clock_is_recognised()
    {
        var pharmacy = SafetyPlacesParser.Parse(Json)[0];
        Assert.True(pharmacy.IsOpenAllNight);
        Assert.Equal("Apteka Nocna", pharmacy.Name);
        Assert.Equal(new GeoPoint(50.06, 19.93), pharmacy.Location);
        Assert.False(SafetyPlacesParser.Parse(Json)[4].IsOpenAllNight);
    }

    [Fact]
    public void Areas_are_located_at_the_middle_of_their_bounding_box_with_an_equivalent_radius()
    {
        var park = SafetyPlacesParser.Parse(Json)[1];

        Assert.Equal(50.062, park.Location.Latitude, 6);
        Assert.Equal(19.934, park.Location.Longitude, 6);
        // 0.004° lat ≈ 445 m by 0.008° lon ≈ 572 m: half the side of a square with that area ≈ 252 m.
        Assert.InRange(park.EquivalentRadiusMeters, 245, 260);
    }

    [Fact]
    public void Radii_are_capped_and_points_have_none()
    {
        var places = SafetyPlacesParser.Parse(Json);
        Assert.Equal(600, places[2].EquivalentRadiusMeters);      // a huge park is capped
        Assert.InRange(places[3].EquivalentRadiusMeters, 1, 150);  // a small hospital campus
        Assert.Equal(0, places[0].EquivalentRadiusMeters);
        Assert.Equal(0, places[4].EquivalentRadiusMeters);
    }

    [Fact]
    public void An_empty_or_unexpected_response_gives_no_places()
    {
        Assert.Empty(SafetyPlacesParser.Parse("""{"elements":[]}"""));
        Assert.Empty(SafetyPlacesParser.Parse("""{"remark":"timeout"}"""));
    }

    [Fact]
    public void The_query_asks_for_boxes_and_the_five_kinds_of_place()
    {
        var query = SafetyPlacesParser.BuildQuery("Kraków");
        Assert.Contains("out bb tags", query);
        Assert.Contains("police|hospital|pharmacy|library", query);
        Assert.Contains("\"leisure\"=\"park\"", query);
        Assert.Contains("Kraków", query);
    }
}

public sealed class JsonFileSafetyStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"krk-safety-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private JsonFileSafetyStore Store(bool persist = true, string? file = null) =>
        new(Microsoft.Extensions.Options.Options.Create(new SafetyOptions { Persist = persist, StorePath = file ?? Path.Combine(_dir, "store.json") }), NullLogger<JsonFileSafetyStore>.Instance);

    private static CitizenReport Report(string id) =>
        new(id, ReportType.LightOut, new GeoPoint(50.06, 19.93), "note", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, ["device-0001", "device-0002"], true, ReportStatus.Open, null, null);

    [Fact]
    public async Task Everything_written_survives_a_restart()
    {
        var first = Store();
        await first.SaveReportAsync(Report("rep-1"));
        await first.SaveAlertAsync(new PlannerAlert("alt-1", ScoreLayer.Heat, AlertSeverity.Warning, "Title", "Message",
            new Dictionary<string, string> { ["pl"] = "Treść" }, new GeoPoint(50.06, 19.93), 800, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), "54-93", AlertStatus.Active));
        await first.AddDispatchAsync(new AgencyDispatch("dis-1", "zdmk", "ZDMK", "Subject", "Body", new GeoPoint(50.06, 19.93), "54-93", "simulated", "SIM-1", DateTimeOffset.UtcNow));

        var second = Store();
        var report = (await second.ListReportsAsync()).Single();
        Assert.Equal(["device-0001", "device-0002"], report.DeviceIds);
        Assert.Equal(ReportType.LightOut, report.Type);
        Assert.True(report.VerifiedByPlanner);

        var alert = (await second.ListAlertsAsync()).Single();
        Assert.Equal(ScoreLayer.Heat, alert.Layer);
        Assert.Equal("Treść", alert.MessageTranslations["pl"]);
        Assert.Equal(AlertStatus.Active, alert.Status);
        Assert.Equal("SIM-1", (await second.ListDispatchesAsync()).Single().Reference);
    }

    [Fact]
    public async Task Saving_the_same_id_replaces_the_record()
    {
        var store = Store();
        await store.SaveReportAsync(Report("rep-1"));
        await store.SaveReportAsync(Report("rep-1") with { Status = ReportStatus.Resolved });

        var all = await store.ListReportsAsync();
        Assert.Single(all);
        Assert.Equal(ReportStatus.Resolved, all[0].Status);
    }

    [Fact]
    public async Task Memory_only_mode_writes_no_file()
    {
        var store = Store(persist: false);
        await store.SaveReportAsync(Report("rep-1"));
        Assert.Single(await store.ListReportsAsync());
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public async Task A_damaged_file_starts_the_store_empty_instead_of_crashing()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "store.json");
        await File.WriteAllTextAsync(path, "{ this is not json");

        var store = Store(file: path);
        Assert.Empty(await store.ListReportsAsync());
        await store.SaveReportAsync(Report("rep-1"));
        Assert.Single(await Store(file: path).ListReportsAsync());
    }
}

public class NightServiceStopsTests
{
    private sealed class Provider(GtfsDataset dataset) : IGtfsDatasetProvider
    {
        public Task<GtfsDataset> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(dataset);
    }

    private static StopTime Call(string stop, string trip, double hours) =>
        new(trip, stop, 1, TimeSpan.FromHours(hours), TimeSpan.FromHours(hours));

    [Fact]
    public async Task A_stop_has_night_service_when_something_leaves_between_23_00_and_04_30()
    {
        var dataset = new GtfsDataset();
        dataset.AddStopTime(Call("day", "t1", 12));
        dataset.AddStopTime(Call("day", "t2", 22.9));      // just before the night window
        dataset.AddStopTime(Call("late", "t3", 23.2));     // 23:12
        dataset.AddStopTime(Call("after-midnight", "t4", 24.5));   // GTFS 24:30 = 00:30 next day
        dataset.AddStopTime(Call("early", "t5", 4.2));     // 04:12
        dataset.AddStopTime(Call("morning", "t6", 4.6));   // 04:36, after the window
        dataset.AddStopTime(Call("night-bus", "t7", 28.4)); // 04:24 on the next service day
        dataset.Seal();

        var stops = await new GtfsScheduleRepository(new Provider(dataset)).GetNightServiceStopIdsAsync();

        Assert.Equal(new[] { "after-midnight", "early", "late", "night-bus" }, stops.OrderBy(s => s, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task The_answer_is_cached_per_dataset()
    {
        var dataset = new GtfsDataset();
        dataset.AddStopTime(Call("a", "t1", 23.5));
        dataset.Seal();
        var repo = new GtfsScheduleRepository(new Provider(dataset));

        Assert.Same(await repo.GetNightServiceStopIdsAsync(), await repo.GetNightServiceStopIdsAsync());
    }
}

public class DrinkingWaterMappingTests
{
    private static IReadOnlyList<string> Water(string tagsJson)
    {
        var json = "{\"elements\":[{\"type\":\"node\",\"id\":1,\"lat\":50.06,\"lon\":19.93,\"tags\":" + tagsJson + "}]}";
        return OverpassParser.Parse(json).Amenities.Select(a => a.Kind.ToString()).ToList();
    }

    [Theory]
    [InlineData("{\"amenity\":\"drinking_water\"}", true)]
    [InlineData("{\"amenity\":\"water_point\"}", true)]
    [InlineData("{\"amenity\":\"drinking_water\",\"drinking_water\":\"no\"}", false)]
    [InlineData("{\"amenity\":\"water_point\",\"drinking_water\":\"no\"}", false)]
    [InlineData("{\"man_made\":\"water_tap\"}", false)]                                  // cemetery taps and the like
    [InlineData("{\"man_made\":\"water_tap\",\"drinking_water\":\"yes\"}", true)]
    [InlineData("{\"amenity\":\"fountain\"}", false)]                                    // decorative
    [InlineData("{\"amenity\":\"fountain\",\"drinking_water\":\"yes\"}", true)]
    [InlineData("{\"natural\":\"spring\",\"drinking_water\":\"yes\"}", true)]
    [InlineData("{\"natural\":\"spring\"}", false)]
    public void Only_drinkable_water_counts(string tags, bool counted) =>
        Assert.Equal(counted ? new[] { "DrinkingWater" } : Array.Empty<string>(), Water(tags));

    [Fact]
    public void The_query_asks_for_every_way_water_is_mapped()
    {
        var query = OverpassParser.BuildQuery("Kraków");
        Assert.Contains("water_point", query);
        Assert.Contains("water_tap", query);
        Assert.Contains("fountain", query);
        Assert.Contains("spring", query);
    }
}

public class PhotonParserTests
{
    private const string Json = """
    {"features":[
      {"geometry":{"coordinates":[19.9373511,50.0617012]},"properties":{"type":"house","name":"Sukiennice","street":"Rynek Główny","housenumber":"3","district":"Stare Miasto","postcode":"31-042"}},
      {"geometry":{"coordinates":[19.9390377,50.0620177]},"properties":{"type":"street","name":"Rynek Główny","street":"Rynek Główny","district":"Stare Miasto"}},
      {"geometry":{"coordinates":[19.9371133,50.0614956]},"properties":{"type":"locality","name":"Rynek Główny","district":"Stare Miasto"}},
      {"geometry":{"coordinates":[19.9371133,50.0614956]},"properties":{"type":"locality","name":"Rynek Główny","district":"Stare Miasto"}},
      {"geometry":{"coordinates":[19.93,50.06]},"properties":{"type":"other"}},
      {"properties":{"name":"No geometry"}}
    ]}
    """;

    [Fact]
    public void Labels_name_the_place_street_number_and_district_without_repeating_parts()
    {
        var results = KrakowOpenData.Infrastructure.Geocoding.PhotonParser.Parse(Json);

        Assert.Equal(new[] { "Sukiennice, Rynek Główny 3, Stare Miasto", "Rynek Główny, Stare Miasto" }, results.Select(r => r.Label).Take(2).ToArray());
        Assert.Equal(3, results.Count);      // the duplicate and the nameless / geometry-less entries are dropped
        Assert.Equal(50.061701, results[0].Latitude, 6);
        Assert.Equal(19.937351, results[0].Longitude, 6);
        Assert.Equal("31-042", results[0].Postcode);
        Assert.Equal("house", results[0].Kind);
    }

    [Fact]
    public void An_empty_response_gives_nothing() =>
        Assert.Empty(KrakowOpenData.Infrastructure.Geocoding.PhotonParser.Parse("""{"features":[]}"""));
}

public class OsrmWalkingRouterTests
{
    [Fact]
    public void A_route_response_becomes_paths_of_lat_lon_points()
    {
        const string json = """
        {"code":"Ok","routes":[
          {"distance":1295.1,"geometry":{"type":"LineString","coordinates":[[19.9373,50.0617],[19.9400,50.0640],[19.9450,50.0700]]}},
          {"distance":1362.5,"geometry":{"type":"LineString","coordinates":[[19.9373,50.0617],[19.9450,50.0700]]}},
          {"distance":5,"geometry":{"type":"LineString","coordinates":[[19.9,50.0]]}}
        ]}
        """;

        var routes = KrakowOpenData.Infrastructure.Geocoding.OsrmWalkingRouter.Parse(json);

        Assert.Equal(2, routes.Count);                       // the one-point geometry is dropped
        Assert.Equal(1295.1, routes[0].DistanceMeters);
        Assert.Equal(new GeoPoint(50.0617, 19.9373), routes[0].Points[0]);   // GeoJSON is [lon, lat]
        Assert.Equal(3, routes[0].Points.Count);
    }

    [Fact]
    public void An_error_response_gives_no_routes() =>
        Assert.Empty(KrakowOpenData.Infrastructure.Geocoding.OsrmWalkingRouter.Parse("""{"code":"NoRoute","message":"x"}"""));
}
