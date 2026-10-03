using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Domain.ClimateAndCrisis;
using KrakowOpenData.Domain.Environment;
using KrakowOpenData.Domain.Mobility;
using KrakowOpenData.Domain.PublicServices;
using KrakowOpenData.Domain.UrbanSpace;
using KrakowOpenData.Infrastructure.Sample;
using KrakowOpenData.Infrastructure.Seed;

namespace KrakowOpenData.Infrastructure.Tests;

public class SeedAndSampleDataTests
{
    [Fact]
    public void All_seed_files_are_embedded()
    {
        var files = SeedData.AvailableFiles();
        foreach (var name in new[]
                 {
                     SeedData.ParkAndRide, SeedData.Districts, SeedData.ServiceCards, SeedData.LocalRiverGauges,
                     SeedData.SampleAirQuality, SeedData.SampleWeather, SeedData.SampleWarnings
                 })
        {
            Assert.Contains(files, f => f.EndsWith("." + name, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Districts_seed_has_all_18_unique_districts()
    {
        var districts = SeedData.Load<District>(SeedData.Districts);
        Assert.Equal(18, districts.Count);
        Assert.Equal(18, districts.Select(d => d.Id).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 18), districts.Select(d => d.Number).OrderBy(n => n));
    }

    [Fact]
    public void Park_and_ride_seed_loads_with_nullable_fields()
    {
        var items = SeedData.Load<ParkAndRideFacility>(SeedData.ParkAndRide);
        Assert.NotEmpty(items);
        Assert.Contains(items, p => p.Capacity == 165);
        Assert.All(items, p => Assert.False(string.IsNullOrWhiteSpace(p.Name)));
    }

    [Fact]
    public void Service_cards_seed_contains_GD_35_with_source_links()
    {
        var cards = SeedData.Load<CityServiceCard>(SeedData.ServiceCards);
        Assert.Contains(cards, c => c.Id == "GD-35");
        Assert.All(cards, c => Assert.StartsWith("https://", c.SourceUrl));
        Assert.All(cards, c => Assert.NotEmpty(c.Steps));
    }

    [Fact]
    public void Historical_river_readings_parse_enum_states()
    {
        var gauges = SeedData.Load<HydroObservation>(SeedData.LocalRiverGauges);
        Assert.Contains(gauges, g => g.River == "Serafa" && g.State == HydroState.AboveWarning);
        Assert.Contains(gauges, g => g.State == HydroState.AboveAlarm);
    }

    [Fact]
    public void Sample_files_are_clearly_labelled()
    {
        Assert.All(SeedData.Load<AirQualityMeasurement>(SeedData.SampleAirQuality), a => Assert.Contains("SAMPLE", a.Source));
        Assert.All(SeedData.Load<WeatherObservation>(SeedData.SampleWeather), w => Assert.Contains("SAMPLE", w.Source));
        Assert.All(SeedData.Load<WeatherWarning>(SeedData.SampleWarnings), w => Assert.Contains("SAMPLE", w.Source));
    }

    [Fact]
    public void Sample_network_is_consistent()
    {
        var d = SampleTransitData.Dataset;
        Assert.NotEmpty(d.Stops);
        Assert.Equal(2, d.Routes.Count);
        Assert.All(d.Trips.Values, t => Assert.Contains(t.RouteId, d.Routes.Keys));
        Assert.All(d.StopTimesByStop.Keys, stopId => Assert.Contains(stopId, d.Stops.Keys));
        Assert.True(d.IsServiceActive("S:daily", new DateOnly(2026, 10, 3)));
    }

    [Fact]
    public void Sample_realtime_puts_vehicles_on_the_network_during_the_day()
    {
        var noon = KrakowTime.FromServiceDay(new DateOnly(2026, 10, 3), new TimeSpan(12, 0, 0));
        var feed = SampleTransitData.BuildRealtime(noon);

        Assert.NotEmpty(feed.Vehicles);
        Assert.Equal(feed.Vehicles.Count, feed.TripUpdates.Count);
        Assert.All(feed.Vehicles, v => Assert.InRange(v.Location.Latitude, 50.0, 50.1));
        Assert.Single(feed.Alerts);
    }

    [Fact]
    public void Sample_realtime_is_quiet_in_the_small_hours()
    {
        var night = KrakowTime.FromServiceDay(new DateOnly(2026, 10, 3), new TimeSpan(3, 0, 0));
        Assert.Empty(SampleTransitData.BuildRealtime(night).Vehicles);
    }
}
