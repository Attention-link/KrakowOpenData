using System.Net;
using System.Net.Http.Json;
using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Catalog;
using KrakowOpenData.Contracts;

namespace KrakowOpenData.Api.Tests;

public class EndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Health_is_ok()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Catalog_lists_all_six_categories()
    {
        var categories = await _client.GetFromJsonAsync<List<CategoryDto>>("/api/catalog");
        Assert.NotNull(categories);
        Assert.Equal(6, categories.Count);
        Assert.Contains(categories, c => c.Key == "Mobility" && c.Datasets.Any(d => d.Key == "vehicle-positions"));
    }

    [Fact]
    public async Task Unknown_dataset_is_404()
    {
        var response = await _client.GetAsync("/api/catalog/nope");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Stop_search_finds_sample_stop_ignoring_diacritics()
    {
        var result = await _client.GetFromJsonAsync<PagedResult<StopDto>>("/api/mobility/stops?q=dworzec%20glowny");
        Assert.NotNull(result);
        Assert.Contains(result.Items, s => s.Id == "S:3");
    }

    [Fact]
    public async Task Nearby_stops_validates_coordinates()
    {
        var bad = await _client.GetAsync("/api/mobility/stops/nearby?lat=999&lon=19.9");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var ok = await _client.GetFromJsonAsync<List<StopDto>>("/api/mobility/stops/nearby?lat=50.0662&lon=19.9455&radius=300");
        Assert.NotNull(ok);
        Assert.Equal("S:3", ok[0].Id);
    }

    [Fact]
    public async Task Departures_return_200_for_known_stop_and_404_for_unknown()
    {
        var ok = await _client.GetAsync("/api/mobility/stops/S:3/departures?limit=5");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var missing = await _client.GetAsync("/api/mobility/stops/S:999/departures");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Routes_reject_unknown_mode()
    {
        var response = await _client.GetAsync("/api/mobility/routes?mode=hovercraft");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var trams = await _client.GetFromJsonAsync<List<RouteDto>>("/api/mobility/routes?mode=tram");
        Assert.NotNull(trams);
        Assert.All(trams, r => Assert.Equal("Tram", r.Mode));
    }

    [Theory]
    [InlineData("/api/mobility/vehicles")]
    [InlineData("/api/mobility/trip-updates")]
    [InlineData("/api/mobility/alerts")]
    [InlineData("/api/mobility/park-and-ride")]
    [InlineData("/api/environment/weather")]
    [InlineData("/api/environment/air-quality")]
    [InlineData("/api/crisis/river-gauges")]
    [InlineData("/api/crisis/warnings")]
    [InlineData("/api/urban/districts")]
    [InlineData("/api/services/cards")]
    public async Task Every_category_endpoint_responds(string url)
    {
        var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Districts_returns_18()
    {
        var districts = await _client.GetFromJsonAsync<List<DistrictDto>>("/api/urban/districts");
        Assert.Equal(18, districts?.Count);
    }

    [Fact]
    public async Task Elevated_river_gauges_are_filtered()
    {
        var gauges = await _client.GetFromJsonAsync<List<RiverGaugeDto>>("/api/crisis/river-gauges?elevatedOnly=true");
        Assert.NotNull(gauges);
        Assert.NotEmpty(gauges);
        Assert.All(gauges, g => Assert.Contains(g.State, new[] { "AboveWarning", "AboveAlarm" }));
    }

    [Fact]
    public async Task Only_warnings_for_Krakow_are_returned()
    {
        var warnings = await _client.GetFromJsonAsync<List<WarningDto>>("/api/crisis/warnings");
        Assert.NotNull(warnings);
        Assert.Single(warnings);
    }

    [Fact]
    public async Task Service_card_search_finds_msip_card()
    {
        var cards = await _client.GetFromJsonAsync<List<ServiceCardDto>>("/api/services/cards?q=msip");
        Assert.NotNull(cards);
        Assert.Equal("GD-35", cards[0].Id);
    }

    [Fact]
    public async Task Open_data_tables_are_listed_and_readable()
    {
        var tables = await _client.GetFromJsonAsync<List<OpenDataTableDto>>("/api/open-data/tables");
        Assert.NotNull(tables);
        Assert.Equal(OpenDataTables.All.Count, tables.Count);

        var rows = await _client.GetFromJsonAsync<OpenDataRowsDto>("/api/open-data/tables/nurseries-public?limit=2");
        Assert.NotNull(rows);
        Assert.Equal(2, rows.Rows.Count);

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/open-data/tables/nope")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/open-data/tables?category=nope")).StatusCode);
    }

    [Fact]
    public async Task Waiting_lists_need_a_search_term_and_sort_by_wait()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/services/waiting-lists?benefit=x")).StatusCode);

        var result = await _client.GetFromJsonAsync<List<WaitingListDto>>("/api/services/waiting-lists?benefit=ortoped");
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.True(result[0].AverageWaitDays <= result[1].AverageWaitDays);
    }

    [Fact]
    public async Task OpenApi_spec_documents_every_category_with_typed_responses()
    {
        var spec = await _client.GetStringAsync("/swagger/v1/swagger.json");

        foreach (var path in new[]
                 {
                     "/api/mobility/stops", "/api/environment/air-quality", "/api/crisis/river-gauges",
                     "/api/urban/amenities", "/api/urban/street-lights", "/api/urban/street-lights/summary", "/api/services/waiting-lists", "/api/open-data/tables/{key}"
                 })
        {
            Assert.Contains($"\"{path}\"", spec);
        }

        Assert.Contains("AirQualityDto", spec);
        Assert.Contains("GetDepartures", spec);
    }

    [Fact]
    public async Task Root_redirects_to_swagger_ui()
    {
        var response = await factory.CreateClient(new() { AllowAutoRedirect = false }).GetAsync("/");
        Assert.Equal("/swagger", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Street_lights_near_a_point_and_summary()
    {
        var near = await _client.GetFromJsonAsync<List<StreetLightDto>>("/api/urban/street-lights?lat=50.0617&lon=19.9373&radius=200");
        Assert.NotNull(near);
        Assert.Equal(new[] { "osm-n10", "osm-n11" }, near.Select(l => l.Id).ToArray());

        var summary = await _client.GetFromJsonAsync<StreetLightSummaryDto>("/api/urban/street-lights/summary");
        Assert.NotNull(summary);
        Assert.Equal(3, summary.Total);
        Assert.Equal(2, summary.WithKnownTechnology);

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/urban/street-lights?technology=candle")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/urban/street-lights?lat=50.06")).StatusCode);
    }
}
