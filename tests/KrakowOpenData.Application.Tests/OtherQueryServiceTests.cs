using KrakowOpenData.Application.Catalog;
using KrakowOpenData.Application.Services;
using KrakowOpenData.Domain.ClimateAndCrisis;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Environment;
using KrakowOpenData.Domain.PublicServices;
using KrakowOpenData.Domain.UrbanSpace;

namespace KrakowOpenData.Application.Tests;

public class OtherQueryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Warnings_are_filtered_to_Krakow_and_to_the_active_window()
    {
        var warnings = new InMemoryRepository<WeatherWarning>(
            new WeatherWarning("krk", "Burze", 2, 70, Now.AddHours(-1), Now.AddHours(5), null, ["1261"], "IMGW"),
            new WeatherWarning("tarnow", "Upał", 2, 80, Now.AddHours(-1), Now.AddHours(5), null, ["1216"], "IMGW"),
            new WeatherWarning("expired", "Mgła", 1, 80, Now.AddDays(-2), Now.AddDays(-1), null, ["1261"], "IMGW"));
        var svc = new ClimateCrisisQueryService(new InMemoryRepository<HydroObservation>(), warnings, new FakeClock(Now));

        var active = await svc.GetActiveWarningsAsync(null);

        Assert.Equal("krk", Assert.Single(active).Id);
    }

    [Fact]
    public async Task River_gauges_can_be_limited_to_elevated_levels_and_sorted_worst_first()
    {
        var gauges = new InMemoryRepository<HydroObservation>(
            new HydroObservation("a", "A", "Serafa", null, 118, null, null, null, HydroState.AboveWarning, "city"),
            new HydroObservation("b", "B", "Kościelnicki", null, 223, null, null, null, HydroState.AboveAlarm, "city"),
            new HydroObservation("c", "C", "Dłubnia", null, 118, null, null, null, HydroState.Normal, "city"));
        var svc = new ClimateCrisisQueryService(gauges, new InMemoryRepository<WeatherWarning>(), new FakeClock(Now));

        var elevated = await svc.GetRiverGaugesAsync(onlyElevated: true);

        Assert.Equal(new[] { "b", "a" }, elevated.Select(g => g.Id).ToArray());
    }

    [Fact]
    public async Task Service_card_search_ranks_by_relevance()
    {
        var cards = new InMemoryRepository<CityServiceCard>(
            Card("A", "Parking P+R", ["parking"]),
            Card("B", "Strefa Czystego Transportu", ["parking", "sct"]),
            Card("C", "Dane przestrzenne", ["msip"]));
        var svc = new PublicServicesQueryService(cards);

        var results = await svc.SearchAsync("parking", 10);

        Assert.Equal("A", results[0].Id); // title + keyword beats keyword only
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Relevance > 0));
    }

    [Fact]
    public async Task Empty_service_card_query_lists_everything_alphabetically()
    {
        var cards = new InMemoryRepository<CityServiceCard>(Card("Z", "Zeta", []), Card("A", "Alfa", []));
        var results = await new PublicServicesQueryService(cards).SearchAsync("", 10);
        Assert.Equal(new[] { "A", "Z" }, results.Select(r => r.Id).ToArray());
    }

    [Fact]
    public async Task Districts_are_returned_in_official_order()
    {
        var repo = new InMemoryRepository<District>(
            new District("II", 2, "Grzegórzki", null, null, "BIP"),
            new District("I", 1, "Stare Miasto", null, null, "BIP"));
        var result = await new UrbanSpaceQueryService(repo).GetDistrictsAsync();
        Assert.Equal(new[] { "I", "II" }, result.Select(d => d.Id).ToArray());
    }

    [Fact]
    public async Task Air_quality_is_sorted_worst_first_with_band()
    {
        var air = new InMemoryRepository<AirQualityMeasurement>(
            new AirQualityMeasurement("clean", "A", null, Now, null, 8, null, "s"),
            new AirQualityMeasurement("dirty", "B", null, Now, null, 60, null, "s"));
        var svc = new EnvironmentQueryService(new InMemoryRepository<WeatherObservation>(), air);

        var result = await svc.GetAirQualityAsync();

        Assert.Equal("dirty", result[0].StationId);
        Assert.Equal("VeryPoor", result[0].Band);
    }

    [Fact]
    public void Catalog_covers_every_category_with_unique_keys()
    {
        var categories = new CatalogService().GetCategories();

        Assert.Equal(Enum.GetValues<DataCategory>().Length, categories.Count);
        Assert.All(categories, c => Assert.NotEmpty(c.Datasets));

        var keys = DataCatalog.Datasets.Select(d => d.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Wired_datasets_expose_api_routes_and_planned_ones_do_not()
    {
        foreach (var d in DataCatalog.Datasets)
        {
            if (d.Access == AccessMode.Planned) Assert.Null(d.ApiRoute);
            else Assert.StartsWith("/api/", d.ApiRoute);
        }
    }

    private static CityServiceCard Card(string id, string title, string[] keywords) =>
        new(id, title, "topic", "summary", [], null, null, null, null, null, "https://example.org", keywords);
}
