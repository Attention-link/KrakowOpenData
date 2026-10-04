using System.Net;
using System.Net.Http.Json;
using KrakowOpenData.Contracts;

namespace KrakowOpenData.Api.Tests;

public class AccessEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Near = "/api/safety/access?lat=50.0617&lon=19.9373";
    private const string Route = "/api/safety/access/route?from=50.0610,19.9373&to=50.0640,19.9373";
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Nearby_items_are_sorted_by_distance_and_each_has_source_date_and_reliability()
    {
        var r = await _client.GetFromJsonAsync<AccessNearbyDto>($"{Near}&radius=500");
        Assert.NotNull(r);
        Assert.Equal("wheelchair", r.Profile);
        Assert.NotEmpty(r.Items);
        Assert.Equal(r.Items.OrderBy(i => i.DistanceMeters).Select(i => i.Id), r.Items.Select(i => i.Id));
        Assert.All(r.Items, i =>
        {
            Assert.Equal("OpenStreetMap", i.Source);
            Assert.Contains(i.Reliability, new[] { "osm_recent", "osm_old", "unknown" });
            Assert.StartsWith("https://www.openstreetmap.org/", i.OsmUrl);
            Assert.Contains("/edit?", i.EditUrl);
            Assert.NotEmpty(i.Facts);
            Assert.InRange(i.DistanceMeters ?? -1, 0, 500);
        });
        Assert.True(r.InDataArea);
        Assert.Contains("ODbL", r.Licence);
        Assert.False(r.SampleData);
    }

    [Fact]
    public async Task Missing_data_is_shown_as_unknown_never_as_accessible()
    {
        var r = await _client.GetFromJsonAsync<AccessNearbyDto>($"{Near}&radius=500");
        var lift = r!.Items.Single(i => i.Id == "way/106");
        Assert.Equal("unknown", lift.Status);
        Assert.Equal("unknown", lift.Reliability);
        Assert.Contains(lift.Facts, f => f.Key == "wheelchair" && f.Value == "unknown" && f.Text == "brak danych");

        var kerb = r.Items.Single(i => i.Id == "node/3");
        Assert.Equal("unknown", kerb.Status);
        Assert.False(kerb.Barrier);
    }

    [Fact]
    public async Task Facts_are_concrete_and_the_last_edit_decides_the_reliability()
    {
        var r = await _client.GetFromJsonAsync<AccessNearbyDto>($"{Near}&radius=500");
        var entrance = r!.Items.Single(i => i.Id == "node/5");
        Assert.Equal("no", entrance.Status);
        Assert.Equal("osm_old", entrance.Reliability);       // last edited 2023
        Assert.Contains(entrance.Facts, f => f.Key == "step_count" && f.Value == "2");
        Assert.Contains(entrance.Facts, f => f.Key == "width" && f.Value == "0.85");

        var toilet = r.Items.Single(i => i.Id == "node/7");
        Assert.Equal("yes", toilet.Status);
        Assert.Equal("osm_recent", toilet.Reliability);
        Assert.Contains(toilet.Facts, f => f.Key == "changing_table" && f.Value == "yes");
    }

    [Fact]
    public async Task Coverage_counts_entrances_without_data_without_listing_them()
    {
        var r = await _client.GetFromJsonAsync<AccessNearbyDto>($"{Near}&radius=500");
        Assert.DoesNotContain(r!.Items, i => i.Id == "node/6");
        var entrances = r.Coverage.Single(c => c.Key == "entrances");
        Assert.Equal((1, 2), (entrances.Known, entrances.Total));
        var paths = r.Coverage.Single(c => c.Key == "paths");
        Assert.True(paths.Total >= 3);
        Assert.Contains(r.Counts, c => c.Kind == "kerb" && c.No == 1 && c.Yes == 1 && c.Unknown == 1);
    }

    [Fact]
    public async Task Kinds_filter_and_profile_change_what_is_listed()
    {
        var steps = await _client.GetFromJsonAsync<AccessNearbyDto>($"{Near}&radius=800&kinds=steps");
        Assert.All(steps!.Items, i => Assert.Equal("steps", i.Kind));
        Assert.Equal(2, steps.Items.Count);

        var mobility = await _client.GetFromJsonAsync<AccessNearbyDto>($"{Near}&radius=800&kinds=steps&profile=mobility");
        Assert.Equal("limited", mobility!.Items.Single(i => i.Id == "way/102").Status);
        var wheelchair = steps.Items.Single(i => i.Id == "way/102");
        Assert.Equal("no", wheelchair.Status);
        Assert.True(wheelchair.Barrier);
    }

    [Fact]
    public async Task Outside_the_data_area_the_note_says_so()
    {
        var r = await _client.GetFromJsonAsync<AccessNearbyDto>("/api/safety/access?lat=50.10&lon=20.05&radius=300");
        Assert.False(r!.InDataArea);
        Assert.Equal("outside_area", r.DataNoteCode);
        Assert.Empty(r.Items);
    }

    [Theory]
    [InlineData(Near + "&radius=2000")]
    [InlineData(Near + "&profile=diagnosis")]
    [InlineData(Near + "&kinds=stairs")]
    [InlineData("/api/safety/access?lat=52.2&lon=21.0")]
    [InlineData("/api/safety/access/route?from=50.06,19.93&to=nonsense")]
    [InlineData(Route + "&profile=x")]
    public async Task Bad_input_is_a_validation_error(string url) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync(url)).StatusCode);

    [Fact]
    public async Task A_route_summary_counts_the_barriers_for_a_wheelchair()
    {
        var r = await _client.GetFromJsonAsync<AccessRouteDto>($"{Route}&profile=wheelchair");
        Assert.NotNull(r);
        Assert.InRange(r.LengthMeters, 300, 360);
        Assert.Equal(1, r.StepsNoRamp);
        Assert.Equal(1, r.RaisedKerbs);
        Assert.Equal(1, r.LoweredKerbs);
        Assert.Equal(1, r.UnknownKerbs);
        Assert.InRange(r.RoughMeters, 140, 180);     // the sett footway
        Assert.InRange(r.SteepMeters, 140, 180);     // 8 % is over the 6 % wheelchair limit
        Assert.InRange(r.UnknownShare, 0, 0.1);
        Assert.Equal(1, r.Benches);
        Assert.Contains(r.Summary, l => l.Code == "steps_no_ramp" && l.Count == 1 && l.Severity == "high" && l.Text == "1 schody bez rampy");
        Assert.Contains(r.Summary, l => l.Code == "kerb_raised" && l.Text == "1 wysoki krawężnik");
        Assert.Contains(r.Summary, l => l.Code == "rough" && l.Text.Contains("kostka brukowa"));
        Assert.Contains(r.Summary, l => l.Code == "unknown_share");
        Assert.Contains(r.Barriers, b => b.Id == "way/102" && b.AlongRouteMeters is > 150);
        Assert.Equal(r.Barriers.OrderBy(b => b.AlongRouteMeters).Select(b => b.Id), r.Barriers.Select(b => b.Id));
        Assert.Contains("Brak danych", r.Note);
    }

    [Fact]
    public async Task The_profile_only_changes_what_counts()
    {
        var pram = await _client.GetFromJsonAsync<AccessRouteDto>($"{Route}&profile=pram");
        Assert.Equal(0, pram!.SteepMeters);                  // 8 % is within the pram limit
        Assert.Equal(1, pram.StepsNoRamp);
        Assert.DoesNotContain(pram.Summary, l => l.Code == "steep");

        var mobility = await _client.GetFromJsonAsync<AccessRouteDto>($"{Route}&profile=mobility");
        Assert.Contains(mobility!.Summary, l => l.Code == "steps_no_ramp" && l.Severity == "medium");
    }

    [Fact]
    public async Task A_path_can_be_posted_and_unmapped_stretches_count_as_unknown()
    {
        var body = new AccessRouteRequest([[50.0610, 19.9373], [50.0640, 19.9373], [50.0660, 19.9373]], "wheelchair");
        var response = await _client.PostAsJsonAsync("/api/safety/access/route", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var r = await response.Content.ReadFromJsonAsync<AccessRouteDto>();
        Assert.InRange(r!.UnknownShare, 0.3, 0.5);           // the last 220 m have no surface data
        Assert.Contains(r.Summary, l => l.Code == "unknown_share" && l.Severity == "medium");

        var bad = await _client.PostAsJsonAsync("/api/safety/access/route", new AccessRouteRequest([[50.06, 19.93]], null));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }
}
