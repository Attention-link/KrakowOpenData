using KrakowOpenData.Application.Safety;
using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.Safety;

namespace KrakowOpenData.Application.Tests;

public class FloodAirModelTests
{
    [Fact]
    public void Every_layer_has_weights_that_add_up_to_100()
    {
        foreach (var layer in Enum.GetValues<Layer>())
            Assert.Equal(100, SafetyModel.FactorsOf(layer).Sum(f => f.Weight));
    }

    [Theory]
    [InlineData(null, 100)]   // nothing within the search radius: far from the river is good
    [InlineData(600.0, 100)]
    [InlineData(500.0, 100)]
    [InlineData(275.0, 50)]     // halfway between 50 m (0) and 500 m (100)
    [InlineData(50.0, 0)]
    [InlineData(10.0, 0)]
    public void Distance_from_a_river_rises_with_distance(double? metres, double expected) =>
        Assert.Equal(expected, SafetyModel.Away(metres, 500, 50), 6);

    [Theory]
    [InlineData("Normal", 0)]
    [InlineData("AboveWarning", 0.5)]
    [InlineData("AboveAlarm", 1)]
    [InlineData(null, 0)]
    public void River_level_comes_from_the_worst_gauge_state(string? state, double expected) =>
        Assert.Equal(expected, SafetyModel.FloodLevel(state), 6);

    [Theory]
    [InlineData(null, 0)]
    [InlineData(10.0, 0)]
    [InlineData(15.0, 0)]
    [InlineData(45.0, 0.5)]
    [InlineData(75.0, 1)]
    [InlineData(200.0, 1)]
    public void Air_level_rises_from_the_who_guideline_to_75(double? pm25, double expected) =>
        Assert.Equal(expected, SafetyModel.AirLevel(pm25), 6);

    [Fact]
    public void A_high_river_hurts_places_on_the_bank_and_spares_distant_ones()
    {
        Assert.Equal(35, SafetyModel.FloodLivePenalty(1, 0), 6);       // alarm, on the bank
        Assert.Equal(17.5, SafetyModel.FloodLivePenalty(0.5, 0), 6);   // warning, on the bank
        Assert.Equal(0, SafetyModel.FloodLivePenalty(1, 100), 6);      // alarm, far away
        Assert.Equal(0, SafetyModel.FloodLivePenalty(0, 0), 6);        // calm river
    }

    [Fact]
    public void Smog_hurts_everywhere_but_most_where_protection_is_poor()
    {
        Assert.Equal(40, SafetyModel.AirLivePenalty(1, 0), 6);
        Assert.Equal(20, SafetyModel.AirLivePenalty(1, 100), 6);   // even a well-protected place loses half
        Assert.Equal(0, SafetyModel.AirLivePenalty(0, 0), 6);
    }

    [Fact]
    public void Priority_pressure_grows_with_the_live_level()
    {
        Assert.Equal(0.8, SafetyModel.FloodPressureFactor(0), 6);
        Assert.Equal(1.6, SafetyModel.FloodPressureFactor(1), 6);
        Assert.Equal(0.8, SafetyModel.AirPressureFactor(0), 6);
        Assert.Equal(1.5, SafetyModel.AirPressureFactor(1), 6);
    }

    [Fact]
    public void New_report_types_feed_flood_and_air_only()
    {
        foreach (var type in new[] { ReportType.FloodedStreet, ReportType.BlockedDrain, ReportType.RisingWater })
            Assert.Equal(ScoreLayer.Flood, ReportRules.For(type).Layer);
        foreach (var type in new[] { ReportType.SmokeOrBurning, ReportType.StrongFumes, ReportType.DustCloud })
            Assert.Equal(ScoreLayer.Air, ReportRules.For(type).Layer);
    }
}

public class FloodAirScoreTests
{
    private static SafetyWorld WorldWithRiverAndRoad()
    {
        var world = new SafetyWorld();
        // A river 30 m west of the centre and a main road 100 m south of it.
        world.Places.Items.Add(new SafetyPlace("river", SafetyPlaceKind.Waterway, "Wisła", SafetyWorld.Offset(SafetyWorld.Centre, 0, -30), null, 0, "T"));
        world.Places.Items.Add(new SafetyPlace("road", SafetyPlaceKind.MajorRoad, "Main road", SafetyWorld.Offset(SafetyWorld.Centre, -100, 0), null, 0, "T"));
        // The same kind of river and road, 1.2 km from the remote point.
        world.Places.Items.Add(new SafetyPlace("river2", SafetyPlaceKind.Waterway, "Wisła", SafetyWorld.Offset(SafetyWorld.Remote, 0, -1200), null, 0, "T"));
        world.Places.Items.Add(new SafetyPlace("road2", SafetyPlaceKind.MajorRoad, "Ring road", SafetyWorld.Offset(SafetyWorld.Remote, 1200, 0), null, 0, "T"));
        return world;
    }

    private static CreateReportRequest Report(string type, string device) =>
        new(type, SafetyWorld.Centre.Latitude, SafetyWorld.Centre.Longitude, null, device);

    [Fact]
    public async Task A_place_on_the_riverbank_scores_lower_for_flood_than_one_far_from_water()
    {
        var scores = WorldWithRiverAndRoad().Scores();
        var bank = await scores.GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Flood);
        var far = await scores.GetPlaceAsync(SafetyWorld.Remote, PlanningEvent.Flood);

        Assert.NotNull(bank.Flood);
        Assert.NotNull(far.Flood);
        var river = bank.Flood!.Factors.Single(f => f.Key == "river");
        Assert.Equal(30, river.Value);
        Assert.True(river.Score < 10, $"river score was {river.Score}");
        Assert.True(far.Flood!.Factors.Single(f => f.Key == "river").Score >= 99);
    }

    [Fact]
    public async Task A_place_next_to_a_main_road_scores_lower_for_air()
    {
        var scores = WorldWithRiverAndRoad().Scores();
        var byRoad = await scores.GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Air);
        var far = await scores.GetPlaceAsync(SafetyWorld.Remote, PlanningEvent.Air);

        var traffic = byRoad.Air!.Factors.Single(f => f.Key == "traffic");
        Assert.Equal(100, traffic.Value);
        Assert.True(traffic.Score is > 20 and < 40, $"traffic score was {traffic.Score}");
        Assert.True(far.Air!.Factors.Single(f => f.Key == "traffic").Score >= 99);
    }

    [Fact]
    public async Task Missing_river_data_reads_as_unknown_not_as_safe()
    {
        var world = new SafetyWorld();   // places loaded, but no waterway or main road in them
        var place = await world.Scores().GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Flood);

        var river = place.Flood!.Factors.Single(f => f.Key == "river");
        Assert.Null(river.Value);
        Assert.Equal(50, river.Score);
        var model = await world.Models().GetAsync();
        Assert.Contains("rivers", model.DataGaps);
        Assert.Contains("mainRoads", model.DataGaps);
    }

    [Fact]
    public async Task A_flood_report_lowers_only_the_flood_score_and_an_air_report_only_the_air_score()
    {
        var world = WorldWithRiverAndRoad();
        var scores = world.Scores();
        var before = await scores.GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Flood);

        await world.Reports().CreateAsync(Report("FloodedStreet", "device-flood-0001"));
        var afterFlood = await scores.GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Flood);
        Assert.True(afterFlood.Flood!.Score < before.Flood!.Score);
        Assert.Equal(before.Air!.Score, afterFlood.Air!.Score);
        Assert.Equal(before.Safety.Score, afterFlood.Safety.Score);
        Assert.Equal(before.Heat.Score, afterFlood.Heat.Score);

        await world.Reports().CreateAsync(Report("SmokeOrBurning", "device-air-00001"));
        var afterAir = await scores.GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Air);
        Assert.True(afterAir.Air!.Score < before.Air.Score);
        Assert.Equal(afterFlood.Flood!.Score, afterAir.Flood!.Score);
    }

    [Fact]
    public async Task Live_river_levels_take_points_off_places_near_water_only()
    {
        var world = WorldWithRiverAndRoad();
        var scores = world.Scores();
        var model = await world.Models().GetAsync();
        var bank = model.Measure(SafetyWorld.Centre);
        var far = model.Measure(SafetyWorld.Remote);

        var calm = new LiveConditions(HeatPressure.None, 0, 0);
        var alarm = new LiveConditions(HeatPressure.None, 1, 0);

        Assert.True(scores.Score(bank, null, PlanningEvent.Flood, alarm).Flood < scores.Score(bank, null, PlanningEvent.Flood, calm).Flood);
        Assert.Equal(scores.Score(far, null, PlanningEvent.Flood, calm).Flood, scores.Score(far, null, PlanningEvent.Flood, alarm).Flood, 6);
    }

    [Fact]
    public async Task Polluted_air_lowers_the_clean_air_score_and_raises_priority()
    {
        var world = WorldWithRiverAndRoad();
        var scores = world.Scores();
        var model = await world.Models().GetAsync();
        var place = model.Measure(SafetyWorld.Centre);

        var clean = scores.Score(place, null, PlanningEvent.Air, new LiveConditions(HeatPressure.None, 0, 0));
        var smog = scores.Score(place, null, PlanningEvent.Air, new LiveConditions(HeatPressure.None, 0, 1));

        Assert.True(smog.Air < clean.Air);
        Assert.True(smog.Priority > clean.Priority);
        Assert.Equal(clean.Flood, smog.Flood, 6);
    }

    [Fact]
    public async Task The_grid_carries_flood_and_air_columns()
    {
        var grid = await WorldWithRiverAndRoad().Scores().GetGridAsync(PlanningEvent.Flood);

        Assert.Equal("flood", grid.Event);
        Assert.Equal(["flood", "air"], grid.Columns.Skip(8).ToArray());
        Assert.All(grid.Cells, c => Assert.Equal(grid.Columns.Count, c.Length));
    }

    [Fact]
    public async Task Reports_of_other_layers_do_not_count_for_the_flood_event()
    {
        var world = WorldWithRiverAndRoad();
        await world.Reports().CreateAsync(Report("LightOut", "device-night-0001"));
        var flood = await world.Scores().ScoreGridAsync(PlanningEvent.Flood);
        var night = await world.Scores().ScoreGridAsync(PlanningEvent.Night);

        Assert.Equal(0, flood.Cells.Sum(c => c.OpenReports));
        Assert.Equal(1, night.Cells.Sum(c => c.OpenReports));
    }
}
