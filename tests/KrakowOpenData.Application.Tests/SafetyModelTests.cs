using KrakowOpenData.Application.Safety;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Safety;

namespace KrakowOpenData.Application.Tests;

/// <summary>The scoring maths: documented on <see cref="SafetyModel"/>, pinned here with worked examples.</summary>
public class SafetyModelTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(150, 100)]
    [InlineData(475, 50)]   // halfway between 150 m (full) and 800 m (zero)
    [InlineData(800, 0)]
    [InlineData(5000, 0)]
    public void Proximity_is_a_straight_line_between_full_and_zero(double meters, double expected) =>
        Assert.Equal(expected, SafetyModel.Proximity(meters, 150, 800), 6);

    [Fact]
    public void Nothing_within_the_search_radius_scores_zero() =>
        Assert.Equal(0, SafetyModel.Proximity(null, 150, 800));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(40, 0)]
    [InlineData(220, 50)]   // halfway between 40 /km² (zero) and 400 /km² (full)
    [InlineData(400, 100)]
    [InlineData(900, 100)]
    public void Density_rises_from_zero_to_full(double perKm2, double expected) =>
        Assert.Equal(expected, SafetyModel.Density(perKm2, 400, 40), 6);

    [Fact]
    public void Factor_weights_add_up_to_100_in_both_layers()
    {
        Assert.Equal(100, SafetyModel.HeatFactors.Sum(f => f.Weight));
        Assert.Equal(100, SafetyModel.SafetyFactors.Sum(f => f.Weight));
    }

    [Fact]
    public void A_place_with_every_factor_at_full_score_scores_100()
    {
        var results = SafetyModel.HeatFactors
            .Select(f => new FactorResult(f, 0, SafetyModel.FactorScore(f, f.Kind == FactorKind.Density ? f.FullWithin : 0), null))
            .ToList();
        Assert.Equal(100, SafetyModel.BaseScore(results), 6);
    }

    [Fact]
    public void Layer_score_subtracts_the_capped_report_penalty_and_stays_within_0_and_100()
    {
        Assert.Equal(60, SafetyModel.LayerScore(80, 20));
        Assert.Equal(50, SafetyModel.LayerScore(80, 500));   // penalty capped at 30
        Assert.Equal(0, SafetyModel.LayerScore(10, 30));
    }

    [Theory]
    [InlineData(100, 100, ScoreBand.Good)]      // full cooling capacity = the best score
    [InlineData(75, 75, ScoreBand.Good)]
    [InlineData(55, 55, ScoreBand.Fair)]
    [InlineData(35, 35, ScoreBand.Weak)]
    [InlineData(10, 10, ScoreBand.Critical)]
    public void Heat_score_points_the_same_way_as_every_other_score(double cooling, double expectedHeat, ScoreBand expectedBand)
    {
        Assert.Equal(expectedHeat, SafetyModel.HeatScore(cooling), 6);
        Assert.Equal(expectedBand, SafetyModel.HeatBand(expectedHeat));
    }

    [Fact]
    public void Shade_outweighs_drinking_water_in_the_heat_score()
    {
        var weight = SafetyModel.HeatFactors.ToDictionary(f => f.Key, f => f.Weight);
        Assert.True(weight["green"] > weight["water"]);
        Assert.True(weight["water"] > weight["toilets"]);
    }

    [Fact]
    public void Combined_score_follows_the_weaker_layer()
    {
        Assert.Equal(32, SafetyModel.Combine(80, 20), 6);   // 0.6 * 20 + 0.4 * 50
        Assert.Equal(50, SafetyModel.Combine(50, 50), 6);
        Assert.True(SafetyModel.Combine(90, 10) < SafetyModel.Combine(50, 50));
    }

    [Theory]
    [InlineData(100, ScoreBand.Good)]
    [InlineData(75, ScoreBand.Good)]
    [InlineData(74.9, ScoreBand.Fair)]
    [InlineData(55, ScoreBand.Fair)]
    [InlineData(54.9, ScoreBand.Weak)]
    [InlineData(35, ScoreBand.Weak)]
    [InlineData(34.9, ScoreBand.Critical)]
    [InlineData(0, ScoreBand.Critical)]
    public void Bands_follow_the_documented_thresholds(double score, ScoreBand expected) =>
        Assert.Equal(expected, SafetyModel.Band(score));

    [Theory]
    [InlineData(0, 0, 0.2)]      // floor keeps thin areas visible
    [InlineData(50, 10, 0.4)]    // (50 + 3 * 10) / 200
    [InlineData(200, 0, 1.0)]
    [InlineData(900, 90, 1.0)]   // capped
    public void Exposure_comes_from_lamps_and_stops(int lamps, int stops, double expected) =>
        Assert.Equal(expected, SafetyModel.Exposure(lamps, stops), 6);

    [Fact]
    public void Priority_is_low_score_times_people_times_urgency()
    {
        Assert.Equal(40, SafetyModel.Priority(60, 1.0, 1.0), 6);
        Assert.Equal(20, SafetyModel.Priority(60, 0.5, 1.0), 6);
        Assert.Equal(60, SafetyModel.Priority(60, 1.0, 1.5), 6);
        Assert.Equal(100, SafetyModel.Priority(0, 1.0, 1.5));   // capped
    }

    [Fact]
    public void Heat_pressure_rises_with_the_warning_level()
    {
        var factors = new[] { HeatPressure.None, HeatPressure.HotDay, HeatPressure.WarningLevel1, HeatPressure.WarningLevel2, HeatPressure.WarningLevel3 }
            .Select(SafetyModel.HeatPressureFactor).ToArray();
        Assert.Equal(factors.OrderBy(x => x).ToArray(), factors);
        Assert.Equal(1.5, factors[^1]);
    }
}

public class ReportRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly GeoPoint Spot = new(50.0617, 19.9373);

    private static CitizenReport Report(ReportType type, string[] devices, DateTimeOffset? at = null, bool verified = false, ReportStatus status = ReportStatus.Open) =>
        new($"r-{Guid.NewGuid():N}"[..10], type, Spot, null, at ?? Now, at ?? Now, devices, verified, status, null, null);

    [Fact]
    public void A_single_unconfirmed_report_counts_a_quarter()
    {
        var penalty = ReportRules.GroupPenalty(ReportType.LightOut, [Report(ReportType.LightOut, ["device-aaaa"])], Now);
        Assert.Equal(5 * 3 * 0.25, penalty, 6);   // 3.75
    }

    [Fact]
    public void Two_independent_supporters_count_in_full_with_a_boost()
    {
        var penalty = ReportRules.GroupPenalty(ReportType.LightOut, [Report(ReportType.LightOut, ["device-aaaa", "device-bbbb"])], Now);
        Assert.Equal(5 * 3 * 1.25, penalty, 6);   // 18.75
    }

    [Fact]
    public void The_same_device_twice_is_still_one_supporter()
    {
        var reports = new[] { Report(ReportType.LightOut, ["device-aaaa"]), Report(ReportType.LightOut, ["device-aaaa"]) };
        Assert.Equal(3.75, ReportRules.GroupPenalty(ReportType.LightOut, reports, Now), 6);
    }

    [Fact]
    public void A_planner_verified_report_counts_in_full_even_with_one_supporter()
    {
        var penalty = ReportRules.GroupPenalty(ReportType.LightOut, [Report(ReportType.LightOut, ["device-aaaa"], verified: true)], Now);
        Assert.Equal(15, penalty, 6);
    }

    [Fact]
    public void A_report_loses_half_its_weight_after_its_half_life()
    {
        var halfLife = TimeSpan.FromHours(ReportRules.For(ReportType.LightOut).HalfLifeHours);
        var fresh = ReportRules.GroupPenalty(ReportType.LightOut, [Report(ReportType.LightOut, ["device-aaaa", "device-bbbb"])], Now);
        var aged = ReportRules.GroupPenalty(ReportType.LightOut, [Report(ReportType.LightOut, ["device-aaaa", "device-bbbb"], Now - halfLife)], Now);
        Assert.Equal(fresh / 2, aged, 6);
    }

    [Fact]
    public void Cell_penalty_is_capped_and_only_counts_the_matching_layer()
    {
        var reports = new[]
        {
            Report(ReportType.LightOut, ["device-aaaa", "device-bbbb", "device-cccc", "device-dddd"]),
            Report(ReportType.UnsafeAtNight, ["device-aaaa", "device-bbbb"]),
            Report(ReportType.NoShade, ["device-aaaa", "device-bbbb"])
        };

        Assert.Equal(SafetyModel.MaxReportPenalty, ReportRules.CellPenalty(ScoreLayer.Safety, reports, Now));
        Assert.Equal(5 * 2 * 1.25, ReportRules.CellPenalty(ScoreLayer.Heat, reports, Now), 6);   // only the NoShade report
    }

    [Fact]
    public void Resolved_reports_do_not_count()
    {
        var resolved = Report(ReportType.LightOut, ["device-aaaa", "device-bbbb"], status: ReportStatus.Resolved);
        Assert.Equal(0, ReportRules.CellPenalty(ScoreLayer.Safety, [resolved], Now));
    }

    [Fact]
    public void Every_report_type_feeds_exactly_one_layer_with_a_positive_weight()
    {
        foreach (var type in Enum.GetValues<ReportType>())
        {
            var rule = ReportRules.For(type);
            Assert.InRange(rule.Weight, 1, 3);
            Assert.True(rule.HalfLifeHours > 0);
        }
    }
}

public class SolarTimeTests
{
    private static readonly double Lat = 50.0617, Lon = 19.9373;

    [Fact]
    public void Midsummer_day_is_long_in_Krakow()
    {
        var (rise, set) = SolarTime.SunTimes(new DateOnly(2026, 6, 21), Lat, Lon);
        Assert.NotNull(rise);
        Assert.NotNull(set);
        Assert.InRange(rise!.Value.UtcDateTime.TimeOfDay.TotalHours, 1.9, 2.6);    // about 04:15 local (UTC+2)
        Assert.InRange(set!.Value.UtcDateTime.TimeOfDay.TotalHours, 18.6, 19.3);   // about 20:55 local
    }

    [Fact]
    public void Midwinter_day_is_short()
    {
        var (rise, set) = SolarTime.SunTimes(new DateOnly(2026, 12, 21), Lat, Lon);
        Assert.InRange(rise!.Value.UtcDateTime.TimeOfDay.TotalHours, 6.3, 7.1);    // about 07:40 local (UTC+1)
        Assert.InRange(set!.Value.UtcDateTime.TimeOfDay.TotalHours, 14.2, 14.9);   // about 15:30 local
    }

    [Theory]
    [InlineData(2026, 6, 21, 12, false)]
    [InlineData(2026, 6, 21, 23, true)]
    [InlineData(2026, 12, 21, 16, true)]    // 17:00 local in December is dark
    [InlineData(2026, 12, 21, 10, false)]
    public void IsDark_follows_the_sun(int y, int m, int d, int hourUtc, bool expected) =>
        Assert.Equal(expected, SolarTime.IsDark(new DateTimeOffset(y, m, d, hourUtc, 0, 0, TimeSpan.Zero), Lat, Lon));
}

public class GridSpecTests
{
    [Fact]
    public void A_point_is_inside_its_own_cell_and_the_centre_maps_back()
    {
        var p = new GeoPoint(50.0617, 19.9373);
        var (row, col) = GridSpec.CellOf(p);
        var centre = GridSpec.CenterOf(row, col);
        Assert.Equal((row, col), GridSpec.CellOf(centre));
        Assert.InRange(GridSpec.Distance(p, centre), 0, GridSpec.CellSizeMeters);
    }

    [Fact]
    public void Cells_are_about_250_metres_on_a_side()
    {
        var a = GridSpec.CenterOf(40, 40);
        Assert.InRange(GridSpec.Distance(a, GridSpec.CenterOf(41, 40)), 245, 255);
        Assert.InRange(GridSpec.Distance(a, GridSpec.CenterOf(40, 41)), 245, 255);
    }

    [Fact]
    public void Fast_distance_agrees_with_haversine_inside_Krakow()
    {
        var a = new GeoPoint(50.0617, 19.9373);
        var b = new GeoPoint(50.0910, 20.0200);
        Assert.InRange(GridSpec.Distance(a, b) / a.DistanceTo(b), 0.995, 1.005);
    }

    [Theory]
    [InlineData("12-34", true, 12, 34)]
    [InlineData("x-1", false, 0, 0)]
    [InlineData("12", false, 0, 0)]
    [InlineData(null, false, 0, 0)]
    public void Cell_ids_parse(string? id, bool ok, int row, int col)
    {
        Assert.Equal(ok, GridSpec.TryParseId(id, out var r, out var c));
        if (ok) Assert.Equal((row, col), (r, c));
    }
}
