using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Accessibility;
using KrakowOpenData.Application.Safety;
using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.Accessibility;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Mobility;
using KrakowOpenData.Domain.UrbanSpace;
using KrakowOpenData.Domain.Safety;

namespace KrakowOpenData.Application.Tests;

/// <summary>The accessibility layer (Layer.Access / AccessPram / AccessMobility): scores per profile, factors, weights, no-data handling, reports, routes.</summary>
public class AccessLayerTests
{
    private static GeoPoint At(double north, double east) => SafetyWorld.Offset(SafetyWorld.Centre, north, east);

    private static AccessFeature Item(string id, AccessKind kind, GeoPoint at, AccessAttributes? a = null, IReadOnlyList<GeoPoint>? line = null) =>
        new(id, kind, at, null, AccessStatus.Unknown, a ?? new AccessAttributes(), null, "OpenStreetMap", line);

    /// <summary>The well-served world with accessibility data around the centre and two anchors 1.5 km away that widen the data area.</summary>
    private static SafetyWorld WorldWithAccess(params AccessFeature[] extra)
    {
        var world = new SafetyWorld();
        world.AccessFeatures.Items.Add(Item("a-anchor1", AccessKind.Bench, At(1500, 1500)));
        world.AccessFeatures.Items.Add(Item("a-anchor2", AccessKind.Bench, At(-1500, -1500)));
        world.AccessFeatures.Items.AddRange(extra);
        return world;
    }

    private static AccessFeature Steps(string id, double north, double east, AccessAttributes? a = null) =>
        Item(id, AccessKind.Steps, At(north, east), a ?? new AccessAttributes(StepCount: 5), [At(north, east - 5), At(north, east + 5)]);

    private static AccessFeature Footway(string id, double north, double east, AccessAttributes a) =>
        Item(id, AccessKind.Path, At(north, east), a with { Highway = "footway" }, [At(north, east - 20), At(north, east + 20)]);

    private static async Task<ScoredCell> CentreCell(SafetyWorld world, PlanningEvent evt = PlanningEvent.Access)
    {
        var grid = await world.Scores().ScoreGridAsync(evt);
        return grid.Cells.Single(c => c.Measure.CellId == GridSpec.IdOf(SafetyWorld.Centre));
    }

    [Fact]
    public void Each_profile_has_its_own_nine_factors_adding_up_to_100_with_unique_keys()
    {
        var all = new[] { Layer.Access, Layer.AccessPram, Layer.AccessMobility }.SelectMany(SafetyModel.FactorsOf).ToList();
        Assert.Equal(27, all.Count);
        Assert.Equal(27, all.Select(f => f.Key).Distinct().Count());
        foreach (var layer in new[] { Layer.Access, Layer.AccessPram, Layer.AccessMobility })
        {
            Assert.Equal(100, SafetyModel.FactorsOf(layer).Sum(f => f.Weight));
            Assert.Equal(AccessKeys.Bases, SafetyModel.FactorsOf(layer).Select(f => AccessKeys.Base(f.Key)).ToArray());
        }

        // Steps and kerbs weigh most for a wheelchair, surface for a pram, rest and slope for limited mobility.
        double W(Layer l, string b) => SafetyModel.FactorsOf(l).Single(f => AccessKeys.Base(f.Key) == b).Weight;
        Assert.Equal("steps", AccessKeys.Base(SafetyModel.FactorsOf(Layer.Access).MaxBy(f => f.Weight)!.Key));
        Assert.Equal("surface", AccessKeys.Base(SafetyModel.FactorsOf(Layer.AccessPram).MaxBy(f => f.Weight)!.Key));
        Assert.Equal("rest", AccessKeys.Base(SafetyModel.FactorsOf(Layer.AccessMobility).MaxBy(f => f.Weight)!.Key));
        Assert.True(W(Layer.AccessMobility, "slope") > W(Layer.Access, "slope"));
        Assert.Equal("access.pram.surface", AccessKeys.Make(AccessProfile.Pram, "surface"));
    }

    [Fact]
    public void Event_names_and_profiles_map_to_the_access_events()
    {
        Assert.Equal(PlanningEvent.Access, ScoreService.ParseEvent("access"));
        Assert.Equal(PlanningEvent.Access, ScoreService.ParseEvent("access", "wheelchair"));
        Assert.Equal(PlanningEvent.AccessPram, ScoreService.ParseEvent("access", "pram"));
        Assert.Equal(PlanningEvent.AccessMobility, ScoreService.ParseEvent("ACCESS", "Mobility"));
        Assert.Equal(PlanningEvent.Heat, ScoreService.ParseEvent("heat", "pram"));
        Assert.Equal("access", ScoreService.EventName(PlanningEvent.AccessPram));
    }

    [Fact]
    public async Task Without_any_accessibility_data_there_is_no_score_anywhere()
    {
        var world = new SafetyWorld();   // no accessibility repository at all
        var grid = await world.Scores().GetGridAsync(PlanningEvent.Access);
        var access = Array.IndexOf(grid.Columns.ToArray(), "access");
        var data = Array.IndexOf(grid.Columns.ToArray(), "accessData");

        Assert.NotEmpty(grid.Cells);
        Assert.All(grid.Cells, c => Assert.Equal((-1, 0), (c[access], c[data])));
        Assert.False(grid.Access!.HasData);
        Assert.Equal(0, grid.Access.CellsWithData);
    }

    [Fact]
    public async Task A_square_inside_the_data_area_gets_a_score_per_profile_and_one_outside_gets_none()
    {
        var world = WorldWithAccess(Item("bench", AccessKind.Bench, At(40, 40)));
        for (var i = 0; i < 12; i++)   // a built-up cell far outside the downloaded area
            world.Lights.Items.Add(new StreetLight($"far{i}", SafetyWorld.Offset(SafetyWorld.Remote, i, i), StreetLightTechnology.Unknown, null, null, null, null, null, "T"));

        var scores = world.Scores();
        var grid = await scores.GetGridAsync(PlanningEvent.Access);
        var cols = grid.Columns.ToList();
        var centre = GridSpec.CellOf(SafetyWorld.Centre);
        var remote = GridSpec.CellOf(SafetyWorld.Remote);

        var inside = grid.Cells.Single(c => c[0] == centre.Row && c[1] == centre.Col);
        var outside = grid.Cells.Single(c => c[0] == remote.Row && c[1] == remote.Col);
        Assert.Equal(1, inside[cols.IndexOf("accessData")]);
        Assert.All(new[] { "access", "accessPram", "accessMobility" }, k => Assert.InRange(inside[cols.IndexOf(k)], 0, 100));
        Assert.Equal(0, outside[cols.IndexOf("accessData")]);
        Assert.All(new[] { "access", "accessPram", "accessMobility" }, k => Assert.Equal(-1, outside[cols.IndexOf(k)]));
        Assert.Equal(0, outside[cols.IndexOf("priority")]);   // no data never ranks

        Assert.True(grid.Access!.HasData);
        Assert.Equal("wheelchair", grid.Access.Profile);
        Assert.True(grid.Access.CellsWithoutData >= 1);
        Assert.NotNull(grid.Access.DataArea);
    }

    [Fact]
    public async Task The_place_card_says_no_data_instead_of_a_score()
    {
        var world = WorldWithAccess();
        var scores = world.Scores();

        var outside = await scores.GetPlaceAsync(SafetyWorld.Remote, PlanningEvent.Access);
        Assert.Null(outside.Access!.Score);
        Assert.False(outside.Access.HasData);
        Assert.Equal("NoData", outside.Access.Band);
        Assert.Equal("outside_area", outside.Access.DataNoteCode);
        Assert.NotNull(outside.Access.DataNote);
        Assert.All(outside.AccessByProfile!.Values, l => Assert.Null(l.Score));

        // Inside the box but with nothing mapped within 250 m of the point: no data too, with its own code.
        var empty = await scores.GetPlaceAsync(At(900, 0), PlanningEvent.Access);
        Assert.Null(empty.Access!.Score);
        Assert.Equal("no_data", empty.Access.DataNoteCode);

        // Where something is mapped there is a score with 9 factors and its profile.
        var world2 = WorldWithAccess(Item("bench", AccessKind.Bench, At(30, 30)));
        var inside = await world2.Scores().GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Access);
        Assert.NotNull(inside.Access!.Score);
        Assert.True(inside.Access.HasData);
        Assert.Equal(9, inside.Access.Factors.Count);
        Assert.All(inside.Access.Factors, f => Assert.StartsWith("access.wheelchair.", f.Key));
        Assert.Equal("wheelchair", inside.AccessProfile);
        Assert.Equal(new[] { "mobility", "pram", "wheelchair" }, inside.AccessByProfile!.Keys.Order().ToArray());
    }

    [Fact]
    public async Task The_profile_chooses_the_score_the_place_card_leads_with()
    {
        var world = WorldWithAccess(Item("bench", AccessKind.Bench, At(30, 30)));
        var scores = world.Scores();

        var pram = await scores.GetPlaceAsync(SafetyWorld.Centre, ScoreService.ParseEvent("access", "pram"));
        Assert.Equal("pram", pram.AccessProfile);
        Assert.All(pram.Access!.Factors, f => Assert.StartsWith("access.pram.", f.Key));
        Assert.Equal("pram", pram.Access.Profile);
    }

    [Fact]
    public async Task Steps_without_a_ramp_hurt_a_wheelchair_more_than_limited_mobility()
    {
        var world = WorldWithAccess(
            Steps("s1", 20, 0), Steps("s2", -20, 0), Steps("s3", 0, 25),
            Item("k1", AccessKind.Kerb, At(10, 10), new AccessAttributes(Kerb: "raised")),
            Item("k2", AccessKind.Kerb, At(-10, 10), new AccessAttributes(Kerb: "raised")));
        var cell = await CentreCell(world);

        // Points lost to steps and kerbs: far more for a wheelchair (they stop it) than for limited mobility (a difficulty).
        double Lost(IReadOnlyList<FactorResult> l) => l.Where(f => AccessKeys.Base(f.Definition.Key) is "steps" or "kerbs").Sum(f => f.Definition.Weight * (100 - f.Score) / 100);
        Assert.True(Lost(cell.Measure.Access) > 2 * Lost(cell.Measure.AccessMobility), $"{Lost(cell.Measure.Access)} vs {Lost(cell.Measure.AccessMobility)}");
        var steps = cell.Measure.Access.Single(f => AccessKeys.Base(f.Definition.Key) == "steps");
        Assert.Equal(3, steps.Value);                 // three flights, each counts 1 for a wheelchair
        Assert.Equal(1.5, cell.Measure.AccessMobility.Single(f => AccessKeys.Base(f.Definition.Key) == "steps").Value);   // a difficulty, counted half
        Assert.True(steps.Score < 70);
    }

    [Fact]
    public async Task A_stroller_ramp_is_fine_for_a_pram_but_only_partly_for_a_wheelchair()
    {
        var world = WorldWithAccess(Steps("s1", 20, 0, new AccessAttributes(StepCount: 4, Ramp: "stroller")), Item("bench", AccessKind.Bench, At(30, 30)));
        var cell = await CentreCell(world);

        Assert.Equal(0, cell.Measure.AccessPram.Single(f => AccessKeys.Base(f.Definition.Key) == "steps").Value);
        Assert.Equal(0.5, cell.Measure.Access.Single(f => AccessKeys.Base(f.Definition.Key) == "steps").Value);
    }

    [Fact]
    public async Task Smooth_footways_and_steep_ones_are_told_apart_per_profile()
    {
        var world = WorldWithAccess(
            Footway("p1", 10, 0, new AccessAttributes(SurfaceClass: "paved_smooth")),
            Footway("p2", -10, 0, new AccessAttributes(SurfaceClass: "paved_smooth", InclinePercent: 7)),   // over 6 % (wheelchair) but within 8 % (pram)
            Footway("p3", 30, 0, new AccessAttributes(SurfaceClass: "paved_rough")));
        var cell = await CentreCell(world);

        double? Value(IReadOnlyList<FactorResult> l, string b) => l.Single(f => AccessKeys.Base(f.Definition.Key) == b).Value;
        Assert.Equal(1, Value(cell.Measure.Access, "slope"));
        Assert.Equal(0, Value(cell.Measure.AccessPram, "slope"));
        Assert.Equal(66.7, Value(cell.Measure.AccessPram, "surface"));      // pram: smooth and within 8 % on 2 of 3 stretches
        Assert.Equal(33.3, Value(cell.Measure.Access, "surface"));          // wheelchair: the 7 % stretch does not count either
    }

    [Fact]
    public async Task Missing_data_is_never_read_as_accessible()
    {
        // Only a bench is mapped: no footway data, no steps data. The surface factor must score 0, not 100.
        var cell = await CentreCell(WorldWithAccess(Item("bench", AccessKind.Bench, At(30, 30))));

        var surface = cell.Measure.Access.Single(f => AccessKeys.Base(f.Definition.Key) == "surface");
        Assert.Null(surface.Value);
        Assert.Equal(0, surface.Score);
        // A stop without the wheelchair flag, and a toilet without a wheelchair tag, do not count as accessible.
        var world = WorldWithAccess(Item("wc", AccessKind.Toilets, At(30, 30)));
        world.Stops.Items.Add(new TransitStop("T:9", "09", "Unflagged", At(20, 20), "T", null));
        world.Stops.Items.Add(new TransitStop("T:8", "08", "Not accessible", At(25, 25), "T", false));
        var other = await CentreCell(world);
        Assert.Null(other.Measure.Access.Single(f => AccessKeys.Base(f.Definition.Key) == "accessStops").Value);
        Assert.Null(other.Measure.Access.Single(f => AccessKeys.Base(f.Definition.Key) == "accessToilets").Value);
    }

    [Fact]
    public async Task Accessible_stops_toilets_and_entrances_come_from_the_data_and_the_wheelchair_flag()
    {
        var world = WorldWithAccess(
            Item("wc", AccessKind.Toilets, At(60, 0), new AccessAttributes(Wheelchair: "yes")),
            Item("lift", AccessKind.Elevator, At(0, 50), new AccessAttributes(Wheelchair: "yes")),
            Item("shop", AccessKind.Place, At(0, -50), new AccessAttributes(Wheelchair: "yes")),
            Item("badshop", AccessKind.Place, At(5, 5), new AccessAttributes(Wheelchair: "no")),
            Item("bench", AccessKind.Bench, At(30, 30)));
        world.Stops.Items.Add(new TransitStop("T:7", "07", "Low floor", At(40, 0), "T", true));
        var cell = await CentreCell(world);

        FactorResult F(string b) => cell.Measure.Access.Single(f => AccessKeys.Base(f.Definition.Key) == b);
        Assert.Equal(100, F("accessStops").Score);
        Assert.Equal("Low floor", F("accessStops").NearestName);
        Assert.Equal(100, F("accessToilets").Score);   // 60 m: inside the 300 m full-score distance
        Assert.Equal(100, F("stepFree").Score);        // the lift and the accessible shop; the "no" shop does not count
        Assert.InRange(F("stepFree").Value!.Value, 49, 51);
        Assert.Equal(100, F("rest").Score);
    }

    private static SafetyWorld WorldForStops(bool flaggedStop)
    {
        var world = WorldWithAccess(
            Steps("s1", 20, 0), Item("bench", AccessKind.Bench, At(30, 30)),
            Item("wc", AccessKind.Toilets, At(60, 0), new AccessAttributes(Wheelchair: "yes")),
            Footway("p1", 10, 0, new AccessAttributes(SurfaceClass: "paved_smooth")));
        world.Stops.Items.Add(new TransitStop("T:9", "09", "Unflagged", At(20, 20), "T", null));
        if (flaggedStop) world.Stops.Items.Add(new TransitStop("T:7", "07", "Low floor", At(40, 0), "T", true));
        return world;
    }

    [Theory]
    [InlineData(Layer.Access)]
    [InlineData(Layer.AccessPram)]
    [InlineData(Layer.AccessMobility)]
    public async Task With_no_stop_flagged_accessible_the_stops_factor_is_left_out_and_the_other_weights_are_rescaled(Layer layer)
    {
        var world = WorldForStops(flaggedStop: false);
        var cell = await CentreCell(world, ScoreService.AccessEvent(AccessKeys.ProfileOf(layer)!));
        var configured = SafetyModel.FactorsOf(layer);
        var stopsWeight = configured.Single(f => AccessKeys.Base(f.Key) == "accessStops").Weight;

        // The expected score: the factor removed, the other configured weights scaled by 100 / (100 - stops weight).
        var factors = cell.Measure.Factors(layer);
        var expected = factors.Where(f => AccessKeys.Base(f.Definition.Key) != "accessStops")
            .Sum(f => configured.Single(c => c.Key == f.Definition.Key).Weight * 100 / (100 - stopsWeight) * f.Score / 100);
        Assert.Equal(expected, cell.AccessOf(layer)!.Value, 6);
        Assert.Equal(expected, cell.Measure.AccessBaseOf(layer), 6);

        var stops = factors.Single(f => AccessKeys.Base(f.Definition.Key) == "accessStops");
        Assert.False(stops.HasData);
        Assert.Equal(0, stops.Points);
        Assert.False(stops.IsWeak);
        Assert.Equal(100, factors.Where(f => f.HasData).Sum(f => f.Definition.Weight), 6);

        // The configured (planner) weights are untouched.
        Assert.Equal(100, SafetyModel.FactorsOf(layer).Sum(f => f.Weight));
    }

    [Fact]
    public async Task With_a_flagged_stop_the_stops_factor_counts_as_before()
    {
        var cell = await CentreCell(WorldForStops(flaggedStop: true));
        var configured = SafetyModel.FactorsOf(Layer.Access);

        Assert.All(cell.Measure.Access, f => Assert.True(f.HasData));
        var expected = cell.Measure.Access.Sum(f => configured.Single(c => c.Key == f.Definition.Key).Weight * f.Score / 100);
        Assert.Equal(expected, cell.Access!.Value, 6);
        Assert.Equal(100, cell.Measure.Access.Single(f => AccessKeys.Base(f.Definition.Key) == "accessStops").Score);
    }

    [Fact]
    public async Task Without_flagged_stops_the_place_card_shows_no_data_for_the_factor_and_there_is_no_action_gap_or_kpi()
    {
        var world = WorldForStops(flaggedStop: false);
        var scores = world.Scores();
        var place = await scores.GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Access);
        var stops = place.Access!.Factors.Single(f => f.Key == "access.wheelchair.accessStops");
        Assert.False(stops.HasData);
        Assert.Equal("no stop in the ZTP open data is flagged wheelchair-accessible", stops.DataNote);
        Assert.Equal(0, stops.Points);
        Assert.All(place.Access.Factors.Where(f => f.Key != stops.Key), f => Assert.True(f.HasData));
        Assert.DoesNotContain(place.Actions, a => a.Code == "REVIEW_ACCESSIBLE_STOPS");
        Assert.DoesNotContain(place.Actions, a => a.FactorKey == stops.Key);

        var grid = await scores.ScoreGridAsync(PlanningEvent.Access);
        Assert.Contains("access_stops", grid.Conditions.DataGaps);

        var summary = await new PlannerService(scores, world.Store, new PresenceTracker(world.Clock)).GetSummaryAsync(PlanningEvent.Access, 5);
        Assert.DoesNotContain(summary.Kpis, k => k.Key == "noAccessibleStop400");
        Assert.DoesNotContain(summary.FactorGaps, g => g.Key.EndsWith(".accessStops"));
        Assert.DoesNotContain(summary.TopPriority.SelectMany(p => p.WeakFactors), k => k.EndsWith(".accessStops"));
    }

    [Fact]
    public async Task With_a_flagged_stop_the_kpi_and_the_gap_are_back_and_no_data_gap_is_reported()
    {
        var world = WorldForStops(flaggedStop: true);
        var scores = world.Scores();
        var summary = await new PlannerService(scores, world.Store, new PresenceTracker(world.Clock)).GetSummaryAsync(PlanningEvent.Access, 5);
        Assert.Contains(summary.Kpis, k => k.Key == "noAccessibleStop400");
        Assert.Contains(summary.FactorGaps, g => g.Key.EndsWith(".accessStops"));
        Assert.DoesNotContain("access_stops", (await scores.ScoreGridAsync(PlanningEvent.Access)).Conditions.DataGaps);
        var place = await scores.GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Access);
        Assert.True(place.Access!.Factors.Single(f => f.Key.EndsWith(".accessStops")).HasData);
    }

    [Fact]
    public async Task The_air_live_penalty_is_reported_apart_from_the_report_penalty()
    {
        var place = await new SafetyWorld().Scores().GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Air);
        Assert.True(place.Air.LivePenalty <= place.Air.ReportPenalty);
        Assert.Equal(place.Air.ReportPenalty, place.Air.LivePenalty);   // no reports in this world: the whole penalty is live
    }

    [Fact]
    public async Task A_barrier_report_lowers_the_access_score_and_not_the_night_score()
    {
        var world = WorldWithAccess(Item("bench", AccessKind.Bench, At(30, 30)));
        var scores = world.Scores();
        var before = await scores.ScoreGridAsync(PlanningEvent.Access);
        var b = before.Cells.Single(c => c.Measure.CellId == GridSpec.IdOf(SafetyWorld.Centre));

        var reports = world.Reports();
        await reports.CreateAsync(new CreateReportRequest("PathHazard", SafetyWorld.Centre.Latitude, SafetyWorld.Centre.Longitude, null, "device-access-0001"));
        await reports.CreateAsync(new CreateReportRequest("PathHazard", SafetyWorld.Centre.Latitude, SafetyWorld.Centre.Longitude, null, "device-access-0002"));
        var after = await world.Scores().ScoreGridAsync(PlanningEvent.Access);
        var a = after.Cells.Single(c => c.Measure.CellId == b.Measure.CellId);

        Assert.Equal(ScoreLayer.Access, ReportRules.For(ReportType.PathHazard).Layer);
        Assert.True(a.Access < b.Access, $"{a.Access} vs {b.Access}");
        Assert.True(a.AccessPram < b.AccessPram);
        Assert.Equal(b.Safety, a.Safety);
        Assert.Equal(1, a.OpenReports);
        Assert.True(ScoreService.InEvent(ReportType.PathHazard, PlanningEvent.AccessPram));
        Assert.False(ScoreService.InEvent(ReportType.PathHazard, PlanningEvent.Night));
    }

    [Fact]
    public async Task The_planner_summary_leaves_out_squares_without_data_and_says_so()
    {
        var world = WorldWithAccess(Steps("s1", 20, 0), Item("bench", AccessKind.Bench, At(30, 30)));
        for (var i = 0; i < 12; i++)
            world.Lights.Items.Add(new StreetLight($"far{i}", SafetyWorld.Offset(SafetyWorld.Remote, i, i), StreetLightTechnology.Unknown, null, null, null, null, null, "T"));
        var scores = world.Scores();
        var planner = new PlannerService(scores, world.Store, new PresenceTracker(world.Clock));

        var summary = await planner.GetSummaryAsync(PlanningEvent.AccessPram, 5);
        var grid = await scores.ScoreGridAsync(PlanningEvent.AccessPram);

        Assert.Equal("access", summary.Event);
        Assert.Equal("pram", summary.Profile);
        Assert.Equal(grid.Cells.Count(c => c.HasAccessData), summary.Cells);
        Assert.True(summary.Cells < grid.Cells.Count);
        Assert.Contains(summary.Kpis, k => k.Key == "cellsNoData" && k.Value >= 1);
        Assert.Contains(summary.Kpis, k => k.Key == "accessCoverage");
        Assert.All(summary.TopPriority, p => Assert.NotNull(p.Access));
        Assert.All(summary.FactorGaps, g => Assert.Equal("AccessPram", g.Layer));
        Assert.DoesNotContain(summary.FactorGaps, g => g.Key.EndsWith(".tactile"));   // weight 0 for a pram: no gap to act on
        Assert.Contains("no accessibility data", string.Join(' ', summary.Notes));
        Assert.Equal(grid.Cells.Count(c => c.HasAccessData), summary.Histogram.Sum(h => h.Cells));
    }

    [Fact]
    public async Task The_weights_of_each_profile_are_set_separately_and_keep_their_reasons()
    {
        var world = new SafetyWorld();
        var models = new SafetyModelProvider(world.Lights, world.Amenities, world.PlacesForModel, world.Stops, world.Schedule, world.Clock, world.Store);
        var weights = new WeightService(new MethodService(models, world.Conditions()), models, world.Store);

        var all = await weights.GetAsync();
        Assert.Equal(new[] { "Access", "AccessPram", "AccessMobility" }, all.Layers.Skip(4).Select(l => l.Layer).ToArray());
        Assert.All(all.Layers.Skip(4), l =>
        {
            Assert.Equal(9, l.Factors.Count);
            Assert.Equal(100, l.Factors.Sum(f => f.Weight));
            Assert.All(l.Factors, f => Assert.False(string.IsNullOrWhiteSpace(f.Why)));
        });

        var changed = await weights.SetAsync(new SetWeightsRequest(new Dictionary<string, double> { ["access.pram.rest"] = 60, ["access.pram.steps"] = 0 }));
        Assert.True(changed.Layers.Single(l => l.Layer == "AccessPram").Customized);
        Assert.False(changed.Layers.Single(l => l.Layer == "Access").Customized);
        Assert.False(changed.Layers.Single(l => l.Layer == "AccessMobility").Customized);
        var pram = changed.Layers.Single(l => l.Layer == "AccessPram").Factors;
        Assert.Equal(100, pram.Sum(f => f.Weight));
        Assert.Equal(0, pram.Single(f => f.Key == "access.pram.steps").Weight);
        Assert.True(pram.Single(f => f.Key == "access.pram.rest").Weight > 30);

        await Assert.ThrowsAsync<SafetyValidationException>(() => weights.SetAsync(new SetWeightsRequest(new Dictionary<string, double> { ["access.nobody.rest"] = 1 })));
    }

    [Fact]
    public async Task The_method_describes_three_access_layers_with_factors_and_no_data_rules()
    {
        var world = new SafetyWorld();
        var models = new SafetyModelProvider(world.Lights, world.Amenities, world.PlacesForModel, world.Stops, world.Schedule, world.Clock, world.Store);
        var method = await new MethodService(models, world.Conditions()).GetAsync();

        Assert.Equal(7, method.Layers.Count);
        var access = method.Layers.Where(l => l.Layer.StartsWith("Access")).ToList();
        Assert.Equal(3, access.Count);
        Assert.All(access, l =>
        {
            Assert.Equal(4, l.Bands.Count);
            Assert.Equal(9, l.Factors.Count);
            Assert.Contains("no data is never counted as accessible", l.Meaning);
            Assert.All(l.Factors, f => Assert.False(string.IsNullOrWhiteSpace(f.WhyWeighted)));
        });
        Assert.Contains(method.Kpis, k => k.Key == "accessCoverage");
    }

    [Fact]
    public async Task Route_thresholds_exist_for_each_access_profile()
    {
        var thresholds = await new RouteThresholdService(new SafetyWorld().Store).GetAsync();
        Assert.Equal(new[] { "Safety", "Heat", "Flood", "Air", "Access", "AccessPram", "AccessMobility" }, thresholds.Layers.Select(l => l.Layer).ToArray());
        Assert.All(thresholds.Layers, l => Assert.False(string.IsNullOrWhiteSpace(l.Why)));
        Assert.Equal(PlanningEvent.AccessPram, ScoreService.ParseEvent("access", "pram"));
    }

    [Fact]
    public async Task A_route_with_no_accessibility_data_says_so_and_a_better_one_is_not_invented()
    {
        var world = WorldWithAccess(Item("bench", AccessKind.Bench, At(30, 30)));
        var far = SafetyWorld.Remote;
        var farEnd = SafetyWorld.Offset(far, 0, 600);
        var routes = await new RouteService(world.Scores(), new ScriptedRouter(new RoutePath([far, farEnd], 600))).GetRoutesAsync(far, farEnd, PlanningEvent.Access);

        Assert.Equal("access", routes.Mode);
        Assert.Equal("wheelchair", routes.Profile);
        Assert.Equal(1, routes.Fastest.NoDataShare);
        Assert.All(routes.Fastest.Samples, s => Assert.Null(s.Access));
        Assert.Null(routes.Better);
        Assert.False(routes.FastestIsAcceptable);
        Assert.Contains("no accessibility data", routes.Note);
    }

    [Fact]
    public async Task The_most_accessible_route_is_the_one_with_fewer_barriers()
    {
        // Two candidate paths through the centre block: one passes flights of steps, the other is a smooth footway.
        var world = WorldWithAccess(
            Steps("s1", 100, -100), Steps("s2", 100, -50), Steps("s3", 100, 0), Steps("s4", 100, 50), Steps("s5", 100, 100),
            Footway("p1", -100, 0, new AccessAttributes(SurfaceClass: "paved_smooth")),
            Item("bench", AccessKind.Bench, At(30, 30)));
        var stepped = new RoutePath([At(100, -120), At(100, 120)], 240);
        var smooth = new RoutePath([At(-100, -120), At(-100, 120)], 300);
        var service = new RouteService(world.Scores(), new ScriptedRouter(stepped, smooth));

        var routes = await service.GetRoutesAsync(At(100, -120), At(100, 120), PlanningEvent.Access);

        Assert.Equal("mostAccessible", RouteService.BetterKind(PlanningEvent.Access));
        Assert.Equal("wheelchair", routes.Profile);
        Assert.Equal(0, routes.Fastest.NoDataShare);
        Assert.NotNull(routes.Better);
        Assert.Equal("mostAccessible", routes.BetterKind);
        Assert.True(routes.Better!.Average >= routes.Fastest.Average);
    }

    [Fact]
    public void A_route_that_is_mostly_unknown_is_not_acceptable()
    {
        var unknown = new RouteOptionDto("fastest", 600, 8, [], [], 90, 90, 0, 0, NoDataShare: 0.8);
        var known = new RouteOptionDto("fastest", 600, 8, [], [], 90, 90, 0, 0);
        Assert.False(RouteService.IsAcceptable(unknown, PlanningEvent.Access));
        Assert.True(RouteService.IsAcceptable(known, PlanningEvent.Access));
    }

    [Fact]
    public async Task The_demo_data_puts_barrier_reports_only_where_there_is_access_data()
    {
        var world = WorldWithAccess(Steps("s1", 20, 0), Item("bench", AccessKind.Bench, At(30, 30)));
        var demo = new DemoDataService(world.Scores(), world.Store, world.Clock);

        Assert.True(await demo.SeedAsync() > 0);
        var barrierReports = (await world.Store.ListReportsAsync()).Where(r => r.Type == ReportType.PathHazard).ToList();
        Assert.NotEmpty(barrierReports);
        var model = await world.Models().GetAsync();
        Assert.All(barrierReports, r => Assert.True(model.Measure(r.Location).AccessCovered));
        Assert.Equal(0, await demo.SeedAsync());
    }
}
