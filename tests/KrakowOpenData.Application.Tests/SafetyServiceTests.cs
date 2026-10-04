using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Safety;
using KrakowOpenData.Application.Services;
using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.ClimateAndCrisis;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Environment;
using KrakowOpenData.Domain.Mobility;
using KrakowOpenData.Domain.Safety;
using KrakowOpenData.Domain.UrbanSpace;

namespace KrakowOpenData.Application.Tests;

public sealed class InMemorySafetyStore : ISafetyStore
{
    private readonly Dictionary<string, CitizenReport> _reports = new();
    private readonly Dictionary<string, PlannerAlert> _alerts = new();
    private readonly List<AgencyDispatch> _dispatches = [];

    public Task<IReadOnlyList<CitizenReport>> ListReportsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CitizenReport>>(_reports.Values.ToList());
    public Task<CitizenReport?> GetReportAsync(string id, CancellationToken ct = default) => Task.FromResult(_reports.GetValueOrDefault(id));
    public Task SaveReportAsync(CitizenReport report, CancellationToken ct = default) { _reports[report.Id] = report; return Task.CompletedTask; }
    public Task<IReadOnlyList<PlannerAlert>> ListAlertsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PlannerAlert>>(_alerts.Values.ToList());
    public Task<PlannerAlert?> GetAlertAsync(string id, CancellationToken ct = default) => Task.FromResult(_alerts.GetValueOrDefault(id));
    public Task SaveAlertAsync(PlannerAlert alert, CancellationToken ct = default) { _alerts[alert.Id] = alert; return Task.CompletedTask; }
    public Task<IReadOnlyList<AgencyDispatch>> ListDispatchesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AgencyDispatch>>(_dispatches.ToList());
    public Task AddDispatchAsync(AgencyDispatch dispatch, CancellationToken ct = default) { _dispatches.Add(dispatch); return Task.CompletedTask; }
    private IReadOnlyDictionary<string, double> _weights = new Dictionary<string, double>();
    public Task<IReadOnlyDictionary<string, double>> GetWeightOverridesAsync(CancellationToken ct = default) => Task.FromResult(_weights);
    public Task SaveWeightOverridesAsync(IReadOnlyDictionary<string, double> weights, CancellationToken ct = default) { _weights = new Dictionary<string, double>(weights); return Task.CompletedTask; }
    private IReadOnlyDictionary<string, double> _thresholds = new Dictionary<string, double>();
    public Task<IReadOnlyDictionary<string, double>> GetRouteThresholdOverridesAsync(CancellationToken ct = default) => Task.FromResult(_thresholds);
    public Task SaveRouteThresholdOverridesAsync(IReadOnlyDictionary<string, double> thresholds, CancellationToken ct = default) { _thresholds = new Dictionary<string, double>(thresholds); return Task.CompletedTask; }
}

public sealed class ThrowingRepository<T> : IReadRepository<T> where T : class, IEntity
{
    public Task<T?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => throw new HttpRequestException("down");
    public Task<IReadOnlyList<T>> ListAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default) => throw new HttpRequestException("down");
    public Task<int> CountAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default) => throw new HttpRequestException("down");
}

public sealed class FakeGateway : IAgencyGateway
{
    public List<(Agency Agency, string Subject)> Sent { get; } = [];

    public Task<AgencyDelivery> SendAsync(Agency agency, string subject, string body, CancellationToken ct = default)
    {
        Sent.Add((agency, subject));
        return Task.FromResult(new AgencyDelivery("simulated", $"SIM-TEST-{Sent.Count:0000}"));
    }
}

/// <summary>A small Kraków with a well-served block around <see cref="Centre"/> and nothing 2 km away at <see cref="Remote"/>.</summary>
public sealed class SafetyWorld
{
    public static readonly GeoPoint Centre = GridSpec.CenterOf(GridSpec.CellOf(new GeoPoint(50.0617, 19.9373)).Row, GridSpec.CellOf(new GeoPoint(50.0617, 19.9373)).Col);
    public static readonly GeoPoint Remote = new(Centre.Latitude + 0.02, Centre.Longitude);

    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero));
    public InMemorySafetyStore Store { get; } = new();
    public InMemoryRepository<StreetLight> Lights { get; } = new();
    public InMemoryRepository<Amenity> Amenities { get; } = new();
    public InMemoryRepository<SafetyPlace> Places { get; } = new();
    public InMemoryRepository<TransitStop> Stops { get; } = new();
    public InMemoryRepository<KrakowOpenData.Domain.Accessibility.AccessFeature> AccessFeatures { get; } = new();
    public FakeScheduleRepository Schedule { get; } = new();
    public InMemoryRepository<WeatherObservation> Weather { get; } = new();
    public InMemoryRepository<WeatherWarning> Warnings { get; } = new();
    public InMemoryRepository<HydroObservation> Hydro { get; } = new();
    public InMemoryRepository<AirQualityMeasurement> Air { get; } = new();
    public IReadRepository<SafetyPlace> PlacesForModel { get; set; }

    public SafetyWorld(bool wellServed = true)
    {
        PlacesForModel = Places;
        if (!wellServed) return;

        // 16 x 16 lamps, 30 m apart: about 450 lamps/km² around the centre cell, so lighting scores 100.
        for (var i = 0; i < 16; i++)
        for (var j = 0; j < 16; j++)
            Lights.Items.Add(new StreetLight($"l{i}-{j}", Offset(Centre, (i - 8) * 30, (j - 8) * 30), StreetLightTechnology.Led, null, null, null, null, null, "T"));

        Amenities.Items.Add(new Amenity("water", AmenityKind.DrinkingWater, "Fountain", Offset(Centre, 60, 0), null, "T"));
        Amenities.Items.Add(new Amenity("toilet", AmenityKind.Toilets, null, Offset(Centre, 0, 80), null, "T"));
        Amenities.Items.Add(new Amenity("aed", AmenityKind.Defibrillator, null, Offset(Centre, -50, 0), null, "T"));
        Places.Items.Add(new SafetyPlace("park", SafetyPlaceKind.Park, "Planty", Offset(Centre, 0, 300), null, 250, "T"));   // edge 50 m away
        Places.Items.Add(new SafetyPlace("lib", SafetyPlaceKind.Library, "Library", Offset(Centre, 100, 100), "Mo-Fr 09:00-18:00", 0, "T"));
        Places.Items.Add(new SafetyPlace("police", SafetyPlaceKind.Police, "Police", Offset(Centre, 120, 0), null, 0, "T"));
        Places.Items.Add(new SafetyPlace("pharm24", SafetyPlaceKind.Pharmacy, "Night pharmacy", Offset(Centre, -100, 100), "24/7", 0, "T"));
        Stops.Items.Add(new TransitStop("T:1", "01", "Rynek", Offset(Centre, 40, 40), "T", null));
        Stops.Items.Add(new TransitStop("T:2", "02", "Day only", Offset(Centre, -20, 20), "T", null));
        Schedule.NightStops.Add("T:1");
    }

    /// <summary>A point <paramref name="north"/> metres north and <paramref name="east"/> metres east of <paramref name="p"/>.</summary>
    public static GeoPoint Offset(GeoPoint p, double north, double east) =>
        new(p.Latitude + north / 111_320.0, p.Longitude + east / (111_320.0 * Math.Cos(50.06 * Math.PI / 180)));

    public SafetyModelProvider Models() => new(Lights, Amenities, PlacesForModel, Stops, Schedule, Clock, Store, AccessFeatures.Items.Count > 0 ? AccessFeatures : null);

    public ConditionsService Conditions() => new(new ClimateCrisisQueryService(Hydro, Warnings, Clock), new EnvironmentQueryService(Weather, Air), Clock);

    public ScoreService Scores() => new(Models(), Conditions(), Store, Clock);

    public ReportService Reports() => new(Store, Clock);
}

public class ScoreServiceTests
{
    [Fact]
    public async Task A_well_served_place_scores_high_and_a_remote_one_low()
    {
        var world = new SafetyWorld();
        var scores = world.Scores();

        var served = await scores.GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Both);
        var remote = await scores.GetPlaceAsync(SafetyWorld.Remote, PlanningEvent.Both);

        Assert.True(served.Heat.Score >= 90, $"heat was {served.Heat.Score}");   // like every score, HIGH is good
        Assert.True(served.Safety.Score >= 90, $"safety was {served.Safety.Score}");
        Assert.True(remote.Heat.Score <= 15 && remote.Safety.Score <= 15);   // a remote place cools badly and is unsafe (both low)
        Assert.Equal("Good", served.Heat.Band);
        Assert.Equal("Critical", remote.Heat.Band);
        Assert.True(served.Combined > remote.Combined);
    }

    [Fact]
    public async Task Factors_report_the_measured_distance_and_the_nearest_names()
    {
        var place = await new SafetyWorld().Scores().GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Heat);

        var water = place.Heat.Factors.Single(f => f.Key == "water");
        Assert.Equal(60, water.Value);
        Assert.Equal(100, water.Score);
        Assert.Equal(25, water.Weight);
        Assert.Equal(25, water.Contribution);           // water is close, so it earns its full 25 points

        var green = place.Heat.Factors.Single(f => f.Key == "green");
        Assert.Equal(50, green.Value);                  // 300 m to the centre minus the 250 m radius
        Assert.Equal("Planty", green.NearestName);

    }

    [Fact]
    public async Task Only_stops_with_night_service_count_for_night_transport()
    {
        var place = await new SafetyWorld().Scores().GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Night);
        var night = place.Safety.Factors.Single(f => f.Key == "nightTransit");
        var any = place.Heat.Factors.Single(f => f.Key == "transit");

        Assert.Equal(57, night.Value!.Value, 1);   // "Rynek" at about (40, 40) m
        Assert.True(any.Value < night.Value);       // the nearer "Day only" stop counts for plain transit only
    }

    [Fact]
    public async Task Open_places_include_police_and_24_7_pharmacies_but_not_ordinary_pharmacies()
    {
        var world = new SafetyWorld();
        world.Places.Items.RemoveAll(p => p.Id == "police" || p.Id == "pharm24");
        world.Places.Items.Add(new SafetyPlace("pharm", SafetyPlaceKind.Pharmacy, "Day pharmacy", SafetyWorld.Offset(SafetyWorld.Centre, 10, 10), "Mo-Fr 08:00-20:00", 0, "T"));

        var without = (await world.Scores().GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Night)).Safety.Factors.Single(f => f.Key == "openPlaces");
        Assert.Null(without.Value);
        Assert.Equal(0, without.Score);

        world.Places.Items.Add(new SafetyPlace("pharm24", SafetyPlaceKind.Pharmacy, "Night pharmacy", SafetyWorld.Offset(SafetyWorld.Centre, 50, 0), "24/7", 0, "T"));
        var with = (await world.Scores().GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Night)).Safety.Factors.Single(f => f.Key == "openPlaces");
        Assert.Equal(100, with.Score);
    }

    [Fact]
    public async Task A_confirmed_report_lowers_only_its_layer_and_resolving_it_restores_the_score()
    {
        var world = new SafetyWorld();
        var scores = world.Scores();
        var before = await scores.GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Both);

        var created = await world.Reports().CreateAsync(new CreateReportRequest("LightOut", SafetyWorld.Centre.Latitude, SafetyWorld.Centre.Longitude, null, "device-0001"));
        await world.Reports().ConfirmAsync(created.Id, "device-0002");
        var during = await scores.GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Both);

        Assert.Equal(before.Heat.Score, during.Heat.Score);                       // a light report does not touch heat
        Assert.Equal(18.8, during.Safety.ReportPenalty, 1);                       // 5 x 3 x 1.25
        Assert.True(during.Safety.Score < before.Safety.Score);
        Assert.Single(during.Reports);

        await world.Reports().ResolveAsync(created.Id, "lamp replaced");
        var after = await scores.GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Both);
        Assert.Equal(before.Safety.Score, after.Safety.Score);
        Assert.Empty(after.Reports);
    }

    [Fact]
    public async Task The_grid_keeps_built_up_cells_and_drops_empty_land()
    {
        var grid = await new SafetyWorld().Scores().GetGridAsync(PlanningEvent.Both);

        var (row, col) = GridSpec.CellOf(SafetyWorld.Centre);
        Assert.Contains(grid.Cells, c => c[0] == row && c[1] == col);
        Assert.DoesNotContain(grid.Cells, c => c[0] == GridSpec.CellOf(SafetyWorld.Remote).Row);
        Assert.Equal(["row", "col", "heat", "safety", "combined", "exposure", "openReports", "priority", "flood", "air", "access", "accessPram", "accessMobility", "accessData"], grid.Columns);
        Assert.All(grid.Cells, c => Assert.Equal(grid.Columns.Count, c.Length));
        Assert.Equal(75, grid.Grid.GoodFrom);
    }

    [Fact]
    public async Task A_heat_warning_raises_priority_for_the_same_cells()
    {
        var calm = new SafetyWorld();
        var hot = new SafetyWorld();
        hot.Warnings.Items.Add(new WeatherWarning("w", "Upał", 3, 90, null, null, "Heat", ["1261"], "T"));

        var calmCell = (await calm.Scores().GetGridAsync(PlanningEvent.Heat)).Cells.OrderByDescending(c => c[7]).First();
        Assert.True(calmCell[7] > 0);
        var hotGrid = await hot.Scores().GetGridAsync(PlanningEvent.Heat);
        var hotCell = hotGrid.Cells.Single(c => c[0] == calmCell[0] && c[1] == calmCell[1]);

        Assert.True(hotCell[7] > calmCell[7]);
        Assert.Equal("WarningLevel3", hotGrid.Conditions.Heat.Pressure);
        Assert.Equal("heat", hotGrid.Conditions.SuggestedMode);
    }

    [Fact]
    public async Task The_model_degrades_instead_of_failing_when_one_dataset_is_down()
    {
        var world = new SafetyWorld { PlacesForModel = new ThrowingRepository<SafetyPlace>() };
        var grid = await world.Scores().GetGridAsync(PlanningEvent.Both);

        Assert.Contains("places", grid.Conditions.DataGaps);
        Assert.NotEmpty(grid.Cells);
        var place = await world.Scores().GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Heat);
        Assert.Equal(0, place.Heat.Factors.Single(f => f.Key == "green").Score);   // no park data: scores zero, not an error
    }

    [Fact]
    public async Task With_no_data_at_all_the_model_asks_the_caller_to_retry()
    {
        var world = new SafetyWorld(wellServed: false) { PlacesForModel = new ThrowingRepository<SafetyPlace>() };
        var provider = new SafetyModelProvider(
            new ThrowingRepository<StreetLight>(), new ThrowingRepository<Amenity>(), new ThrowingRepository<SafetyPlace>(),
            new ThrowingRepository<TransitStop>(), world.Schedule, world.Clock);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetAsync());
    }

    [Fact]
    public async Task The_corridor_scores_every_50_metres_and_flags_the_weakest_point()
    {
        var world = new SafetyWorld();
        var corridor = await world.Scores().GetCorridorAsync(SafetyWorld.Centre, SafetyWorld.Offset(SafetyWorld.Centre, 1500, 0));

        Assert.InRange(corridor.LengthMeters, 1495, 1505);
        Assert.Equal(31, corridor.Samples.Count);                         // 30 steps of 50 m plus the start
        Assert.True(corridor.MinSafety < corridor.Samples[0].Safety);      // it gets worse away from the served block
        Assert.Equal(corridor.MinSafety, corridor.Samples[corridor.WeakestSampleIndex].Safety);
        Assert.Equal(19, corridor.WalkingMinutes);                         // 1500 m at 80 m/min
    }

    [Fact]
    public async Task A_corridor_longer_than_5_km_is_refused()
    {
        var world = new SafetyWorld();
        await Assert.ThrowsAsync<SafetyValidationException>(() =>
            world.Scores().GetCorridorAsync(SafetyWorld.Centre, SafetyWorld.Offset(SafetyWorld.Centre, 6000, 0)));
    }

    [Fact]
    public async Task Features_are_exposed_for_offline_use()
    {
        var features = await new SafetyWorld().Scores().GetFeaturesAsync();
        Assert.Contains(features, f => f.Key == "water" && f.Kind == "DrinkingWater");
        Assert.Contains(features, f => f.Key == "green" && f.RadiusMeters == 250);
        Assert.DoesNotContain(features, f => f.Key is "transit" or "nightTransit");
    }

    [Fact]
    public async Task Places_get_a_label_from_the_nearest_stop()
    {
        var place = await new SafetyWorld().Scores().GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Both);
        Assert.Equal("Day only", place.Label);
        Assert.NotNull(place.Label);
        Assert.Null((await new SafetyWorld().Scores().GetPlaceAsync(SafetyWorld.Remote, PlanningEvent.Both)).Label);
    }

    [Fact]
    public async Task Suggested_actions_follow_the_weak_factors_of_the_event()
    {
        var world = new SafetyWorld(wellServed: false);
        world.Lights.Items.AddRange(Enumerable.Range(0, 10).Select(i => new StreetLight($"x{i}", SafetyWorld.Offset(SafetyWorld.Centre, i, i), StreetLightTechnology.Unknown, null, null, null, null, null, "T")));

        var heat = await world.Scores().GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Heat);
        var night = await world.Scores().GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Night);

        Assert.Contains(heat.Actions, a => a.Code == "ADD_WATER_POINT");
        Assert.DoesNotContain(heat.Actions, a => a.Code == "FIX_LIGHTING");
        Assert.Contains(night.Actions, a => a.Code == "FIX_LIGHTING");
        Assert.DoesNotContain(night.Actions, a => a.Code == "ADD_WATER_POINT");
    }

}

public class ConditionsServiceTests
{
    [Fact]
    public async Task A_hot_reading_without_a_warning_is_a_hot_day()
    {
        var world = new SafetyWorld();
        world.Weather.Items.Add(new WeatherObservation("1", "Kraków", world.Clock.UtcNow, 31.2, null, null, null, null, null, "T"));

        var c = await world.Conditions().GetAsync();
        Assert.Equal("HotDay", c.Heat.Pressure);
        Assert.Equal("heat", c.SuggestedMode);
    }

    [Fact]
    public async Task A_heat_warning_wins_over_the_temperature_and_other_warnings_are_ignored_for_heat()
    {
        var world = new SafetyWorld();
        world.Weather.Items.Add(new WeatherObservation("1", "Kraków", world.Clock.UtcNow, 25, null, null, null, null, null, "T"));
        world.Warnings.Items.Add(new WeatherWarning("a", "Silny wiatr", 3, 80, null, null, null, ["1261"], "T"));
        world.Warnings.Items.Add(new WeatherWarning("b", "Upał", 2, 90, null, null, null, ["12"], "T"));

        var c = await world.Conditions().GetAsync();
        Assert.Equal("WarningLevel2", c.Heat.Pressure);
        Assert.Equal(2, c.Warnings.Count);
    }

    [Fact]
    public async Task A_failing_live_source_is_reported_as_a_gap_not_an_error()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero));
        var service = new ConditionsService(
            new ClimateCrisisQueryService(new ThrowingRepository<HydroObservation>(), new InMemoryRepository<WeatherWarning>(), clock),
            new EnvironmentQueryService(new InMemoryRepository<WeatherObservation>(), new ThrowingRepository<AirQualityMeasurement>()),
            clock);

        var c = await service.GetAsync();
        Assert.Contains("rivers", c.DataGaps);
        Assert.Contains("air", c.DataGaps);
        Assert.Equal("None", c.Heat.Pressure);
    }

    [Fact]
    public async Task Night_suggests_the_safety_view()
    {
        var world = new SafetyWorld();
        world.Clock.UtcNow = new DateTimeOffset(2026, 10, 3, 21, 0, 0, TimeSpan.Zero);
        var c = await world.Conditions().GetAsync();
        Assert.True(c.IsDark);
        Assert.Equal("safety", c.SuggestedMode);
    }
}
