using KrakowOpenData.Domain.ClimateAndCrisis;
using KrakowOpenData.Domain.Environment;
using KrakowOpenData.Domain.Mobility;

namespace KrakowOpenData.Domain.Tests;

public class AlertsAndWarningsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Alert_without_periods_is_always_active()
    {
        var alert = NewAlert();
        Assert.True(alert.IsActiveAt(Now));
    }

    [Fact]
    public void Alert_is_active_only_inside_one_of_its_periods()
    {
        var alert = NewAlert(new ActivePeriod(Now.AddHours(1), Now.AddHours(2)));
        Assert.False(alert.IsActiveAt(Now));
        Assert.True(alert.IsActiveAt(Now.AddHours(1.5)));
    }

    [Fact]
    public void Open_ended_period_is_active_after_start()
    {
        var alert = NewAlert(new ActivePeriod(Now.AddHours(-1), null));
        Assert.True(alert.IsActiveAt(Now.AddDays(30)));
    }

    [Theory]
    [InlineData("1261", "1261", true)]
    [InlineData("12", "1261", true)]      // voivodeship-wide warning covers Kraków
    [InlineData("126101", "1261", true)]  // finer code inside Kraków
    [InlineData("1206", "1261", false)]
    public void Warning_teryt_matching_is_hierarchical(string listed, string area, bool expected)
    {
        var warning = new WeatherWarning("w", "Upał", 2, 80, null, null, null, [listed], "IMGW");
        Assert.Equal(expected, warning.AppliesTo(area));
    }

    [Fact]
    public void Warning_validity_window_is_respected()
    {
        var warning = new WeatherWarning("w", "Burze", 1, null, Now.AddHours(1), Now.AddHours(3), null, ["1261"], "IMGW");
        Assert.False(warning.IsActiveAt(Now));
        Assert.True(warning.IsActiveAt(Now.AddHours(2)));
    }

    [Theory]
    [InlineData(100.0, 120.0, 150.0, null, HydroState.Normal)]
    [InlineData(125.0, 120.0, 150.0, null, HydroState.AboveWarning)]
    [InlineData(160.0, 120.0, 150.0, null, HydroState.AboveAlarm)]
    [InlineData(118.0, null, null, HydroState.AboveWarning, HydroState.AboveWarning)]
    [InlineData(null, null, null, null, HydroState.Unknown)]
    public void Hydro_state_uses_thresholds_or_reported_state(
        double? level, double? warning, double? alarm, HydroState? reported, HydroState expected)
    {
        var gauge = new HydroObservation("g", "st", "Serafa", null, level, null, warning, alarm, reported, "test");
        Assert.Equal(expected, gauge.State);
    }

    [Theory]
    [InlineData(null, AirQualityBand.Unknown)]
    [InlineData(10.0, AirQualityBand.Good)]
    [InlineData(25.0, AirQualityBand.Moderate)]
    [InlineData(50.0, AirQualityBand.Poor)]
    [InlineData(80.0, AirQualityBand.VeryPoor)]
    public void Air_quality_band_follows_pm25(double? pm25, AirQualityBand expected)
    {
        var m = new AirQualityMeasurement("a", "st", null, Now, null, pm25, null, "test");
        Assert.Equal(expected, m.Band);
    }

    private static ServiceAlert NewAlert(params ActivePeriod[] periods) =>
        new("a", "Header", null, "UNKNOWN_CAUSE", "DETOUR", periods, [], [], null, "T");
}
