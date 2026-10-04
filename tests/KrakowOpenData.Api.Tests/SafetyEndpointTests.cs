using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using KrakowOpenData.Api.Endpoints;
using KrakowOpenData.Contracts;
using KrakowOpenData.Infrastructure.Options;

namespace KrakowOpenData.Api.Tests;

public class SafetyEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Key = "demo-planner";
    private const double Lat = 50.0617, Lon = 19.9373;
    private readonly HttpClient _client = factory.CreateClient();

    private HttpRequestMessage Planner(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add("X-Planner-Key", Key);
        return request;
    }

    private async Task<T> Send<T>(HttpRequestMessage request, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await _client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    [Fact]
    public async Task Conditions_describe_heat_air_rivers_and_daylight()
    {
        var c = await _client.GetFromJsonAsync<ConditionsDto>("/api/safety/conditions");
        Assert.NotNull(c);
        Assert.Equal("None", c.Heat.Pressure);
        Assert.Contains(c.SuggestedMode, new[] { "safety", "heat", "both", "flood", "air" });
        Assert.NotNull(c.SunsetLocal);
    }

    [Fact]
    public async Task The_grid_is_compact_and_carries_the_band_thresholds()
    {
        var grid = await _client.GetFromJsonAsync<GridDto>("/api/safety/grid?event=heat");
        Assert.NotNull(grid);
        Assert.Equal("heat", grid.Event);
        Assert.NotEmpty(grid.Cells);
        Assert.All(grid.Cells, c => Assert.Equal(grid.Columns.Count, c.Length));
        Assert.Equal(250, grid.Grid.CellSizeMeters);
        Assert.Equal((75, 55, 35), (grid.Grid.GoodFrom, grid.Grid.FairFrom, grid.Grid.WeakFrom));
    }

    [Fact]
    public async Task A_place_has_both_scores_with_their_factors_and_nearby_help()
    {
        var place = await _client.GetFromJsonAsync<PlaceScoreDto>($"/api/safety/place?lat={Lat}&lon={Lon}&event=both");
        Assert.NotNull(place);
        Assert.Equal(5, place.Heat.Factors.Count);
        Assert.Equal(4, place.Safety.Factors.Count);
        Assert.Contains(place.Nearest, n => n.Key == "aed" && n.DistanceMeters < 50);
        Assert.Contains(place.Nearest, n => n.Key == "openPlaces");
        Assert.InRange(place.Combined, 0, 100);
        Assert.NotNull(place.Label);   // named after the nearest stop
    }

    [Theory]
    [InlineData("/api/safety/place?lat=52.23&lon=21.01")]
    [InlineData("/api/safety/corridor?from=50.06,19.93&to=52.2,21.0")]
    [InlineData("/api/safety/corridor?from=nonsense&to=50.06,19.93")]
    [InlineData("/api/safety/alerts?lat=0&lon=0")]
    public async Task Places_outside_Krakow_are_a_validation_error(string url) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync(url)).StatusCode);

    [Fact]
    public async Task A_cell_can_be_read_by_id_and_unknown_ids_are_404()
    {
        var grid = await _client.GetFromJsonAsync<GridDto>("/api/safety/grid");
        var cell = grid!.Cells[0];
        var detail = await _client.GetFromJsonAsync<PlaceScoreDto>($"/api/safety/cells/{cell[0]}-{cell[1]}");
        Assert.Equal($"{cell[0]}-{cell[1]}", detail!.CellId);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/safety/cells/not-a-cell")).StatusCode);
    }

    [Fact]
    public async Task The_corridor_endpoint_scores_a_walk()
    {
        var c = await _client.GetFromJsonAsync<CorridorDto>("/api/safety/corridor?from=50.0617,19.9373&to=50.0647,19.9450");
        Assert.NotNull(c);
        Assert.InRange(c.LengthMeters, 550, 750);
        Assert.True(c.Samples.Count > 5);
        Assert.InRange(c.MinSafety, 0, 100);
    }

    [Fact]
    public async Task The_route_endpoint_returns_a_street_route_with_scores()
    {
        var r = await _client.GetFromJsonAsync<RoutesDto>("/api/safety/route?from=50.0617,19.9373&to=50.0647,19.9450&mode=night");
        Assert.NotNull(r);
        Assert.Equal("street", r.Source);
        Assert.Equal("night", r.Mode);
        Assert.Equal("fastest", r.Fastest.Kind);
        Assert.True(r.Fastest.Path.Count >= 2);
        Assert.True(r.Fastest.Samples.Count > 5);

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/safety/route?from=nope&to=50.0647,19.9450")).StatusCode);
    }

    [Fact]
    public async Task The_method_endpoint_explains_the_scores()
    {
        var m = await _client.GetFromJsonAsync<MethodDto>("/api/safety/method");
        Assert.NotNull(m);
        Assert.Equal(4, m.Layers.Count);
        Assert.All(m.Layers, l => Assert.Equal(100, l.Factors.Sum(f => f.Weight)));
        Assert.StartsWith("Higher = more heat relief", m.Layers.Single(l => l.Layer == "Heat").Direction);
        Assert.Contains(m.Kpis, k => k.Key == "noWater500");
    }

    [Fact]
    public async Task Features_and_report_types_are_listed()
    {
        var features = await _client.GetFromJsonAsync<List<FeatureDto>>("/api/safety/features");
        Assert.Contains(features!, f => f.Key == "water");
        Assert.Contains(features!, f => f.Key == "green" && f.RadiusMeters == 200);

        var types = await _client.GetFromJsonAsync<List<ReportTypeDto>>("/api/safety/report-types");
        Assert.Equal(12, types!.Count);
        Assert.Contains(types, t => t.Type == "NoShade" && t.Layer == "Heat");
    }

    // ── Reports ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_resident_can_report_and_the_score_of_that_place_drops()
    {
        var before = await _client.GetFromJsonAsync<PlaceScoreDto>($"/api/safety/place?lat={Lat}&lon={Lon}");
        var created = await _client.PostAsJsonAsync("/api/safety/reports", new CreateReportRequest("LightOut", Lat, Lon, "secret note", "device-api-0001"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var report = (await created.Content.ReadFromJsonAsync<ReportDto>())!;
        Assert.Null(report.Note);

        // The second device confirms it, so it counts in full.
        var confirmed = await _client.PostAsJsonAsync($"/api/safety/reports/{report.Id}/confirm", new ConfirmReportRequest("device-api-0002"));
        Assert.Equal(2, (await confirmed.Content.ReadFromJsonAsync<ReportDto>())!.Supporters);

        var after = await _client.GetFromJsonAsync<PlaceScoreDto>($"/api/safety/place?lat={Lat}&lon={Lon}");
        Assert.True(after!.Safety.Score < before!.Safety.Score);
        Assert.True(after.Safety.ReportPenalty > 0);
        Assert.Contains(after.Reports, r => r.Id == report.Id && r.Note is null);
    }

    [Fact]
    public async Task Report_notes_are_visible_only_to_planners()
    {
        var created = await (await _client.PostAsJsonAsync("/api/safety/reports", new CreateReportRequest("NoShade", Lat + 0.03, Lon, "bench is broken too", "device-api-0003")))
            .Content.ReadFromJsonAsync<ReportDto>();

        var publicList = await _client.GetFromJsonAsync<List<ReportDto>>("/api/safety/reports");
        Assert.All(publicList!, r => Assert.Null(r.Note));

        var plannerList = await Send<List<ReportDto>>(Planner(HttpMethod.Get, "/api/safety/reports"));
        Assert.Equal("bench is broken too", plannerList.Single(r => r.Id == created!.Id).Note);
    }

    [Fact]
    public async Task Invalid_reports_get_a_400_with_the_field_and_a_flood_gets_a_429()
    {
        var bad = await _client.PostAsJsonAsync("/api/safety/reports", new CreateReportRequest("Nope", Lat, Lon, null, "device-api-0004"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("type", await bad.Content.ReadAsStringAsync());

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/safety/reports", new CreateReportRequest("HeatSpot", Lat + 0.02 + i * 0.01, Lon, null, "device-api-flood"))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await _client.PostAsJsonAsync("/api/safety/reports", new CreateReportRequest("HeatSpot", Lat + 0.09, Lon, null, "device-api-flood"))).StatusCode);
    }

    [Theory]
    [InlineData("99")]
    [InlineData("1")]
    public async Task A_numeric_report_type_is_a_400_and_never_reaches_the_lists(string type)
    {
        var bad = await _client.PostAsJsonAsync("/api/safety/reports", new CreateReportRequest(type, Lat, Lon, null, "device-api-numeric"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/safety/reports")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/safety/grid")).StatusCode);
    }

    [Fact]
    public async Task Writes_are_limited_per_client_address_and_reads_are_not()
    {
        using var limited = factory.WithWebHostBuilder(b => b.UseSetting("Safety:WriteRequestsPerMinute", "3"));
        var client = limited.CreateClient();
        HttpRequestMessage Post(string ip)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/safety/reports") { Content = JsonContent.Create(new CreateReportRequest("Nope", Lat, Lon, null, "device-api-limit")) };
            request.Headers.Add("CF-Connecting-IP", ip);
            return request;
        }

        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(Post("203.0.113.7"))).StatusCode);
        var rejected = await client.SendAsync(Post("203.0.113.7"));
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Contains("Too many requests", await rejected.Content.ReadAsStringAsync());
        var confirm = new HttpRequestMessage(HttpMethod.Post, "/api/safety/reports/rep-x/confirm") { Content = JsonContent.Create(new ConfirmReportRequest("device-api-limit")) };
        confirm.Headers.Add("CF-Connecting-IP", "203.0.113.7");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(confirm)).StatusCode);   // "still true" shares the window

        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(Post("198.51.100.1"))).StatusCode);   // another address has its own window
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/safety/report-types")).StatusCode);

        // Same (full) address, as at a venue behind one NAT: the planner with its key and the read-only path assessment still work.
        HttpRequestMessage Planner(string key)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/safety/planner/reports/rep-x/verify");
            request.Headers.Add("CF-Connecting-IP", "203.0.113.7");
            request.Headers.Add(SafetyEndpoints.PlannerKeyHeader, key);
            return request;
        }

        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Planner("demo-planner"))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(Planner("wrong-key"))).StatusCode);   // guessing keys stays limited
        var assess = new HttpRequestMessage(HttpMethod.Post, "/api/safety/access/route")
        {
            Content = JsonContent.Create(new { profile = "wheelchair", path = new[] { new[] { Lat, Lon }, new[] { Lat + 0.001, Lon } } })
        };
        assess.Headers.Add("CF-Connecting-IP", "203.0.113.7");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(assess)).StatusCode);
    }

    [Fact]
    public void The_default_write_limit_leaves_room_for_a_venue_behind_one_address() =>
        Assert.True(new SafetyOptions().WriteRequestsPerMinute >= 120);

    [Fact]
    public async Task Every_answer_carries_the_security_headers()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.StartsWith("max-age=", response.Headers.GetValues("Strict-Transport-Security").Single());   // "Testing" is not Development
    }

    // ── Planner ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/safety/planner/ping")]
    [InlineData("/api/safety/planner/summary")]
    [InlineData("/api/safety/planner/alerts")]
    [InlineData("/api/safety/planner/agencies")]
    [InlineData("/api/safety/planner/dispatches")]
    [InlineData("/api/safety/planner/weights")]
    public async Task Planner_endpoints_need_the_key(string url)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync(url)).StatusCode);

        var wrong = new HttpRequestMessage(HttpMethod.Get, url);
        wrong.Headers.Add("X-Planner-Key", "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(wrong)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(Planner(HttpMethod.Get, url))).StatusCode);
    }


    [Fact]
    public async Task A_planner_can_change_and_reset_factor_weights()
    {
        var unauthorised = await _client.PutAsJsonAsync("/api/safety/planner/weights", new SetWeightsRequest(new Dictionary<string, double> { ["lighting"] = 10 }));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorised.StatusCode);

        var set = await Send<WeightsDto>(Planner(HttpMethod.Put, "/api/safety/planner/weights",
            new SetWeightsRequest(new Dictionary<string, double> { ["lighting"] = 1, ["nightTransit"] = 1, ["openPlaces"] = 1, ["aed"] = 1 })));
        Assert.True(set!.Customized);
        Assert.All(set.Layers, l => Assert.Equal(100, l.Factors.Sum(f => f.Weight)));
        Assert.Equal(25, set.Layers.Single(l => l.Layer == "Safety").Factors.Single(f => f.Key == "aed").Weight);

        var method = await _client.GetFromJsonAsync<MethodDto>("/api/safety/method");
        Assert.Equal(25, method!.Layers.Single(l => l.Layer == "Safety").Factors.Single(f => f.Key == "lighting").Weight);

        var bad = await _client.SendAsync(Planner(HttpMethod.Put, "/api/safety/planner/weights", new SetWeightsRequest(new Dictionary<string, double> { ["nope"] = 1 })));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var reset = await Send<WeightsDto>(Planner(HttpMethod.Delete, "/api/safety/planner/weights"));
        Assert.False(reset!.Customized);
    }

    [Fact]
    public async Task Report_lists_can_be_filtered_and_bad_filters_are_rejected()
    {
        await _client.PostAsJsonAsync("/api/safety/reports", new CreateReportRequest("FloodedStreet", Lat, Lon, "secret flood note", "device-filter-001"));
        await _client.PostAsJsonAsync("/api/safety/reports", new CreateReportRequest("NoShade", Lat + 0.01, Lon, null, "device-filter-002"));

        var flood = await _client.GetFromJsonAsync<List<ReportDto>>("/api/safety/reports?layer=flood");
        Assert.All(flood!, r => Assert.Equal("Flood", r.Layer));
        Assert.Contains(flood!, r => r.Type == "FloodedStreet");
        Assert.All(flood!, r => Assert.Null(r.Note));   // residents never get notes

        var byType = await _client.GetFromJsonAsync<List<ReportDto>>("/api/safety/reports?type=noshade");
        Assert.All(byType!, r => Assert.Equal("NoShade", r.Type));

        // Free text only works for planners: a resident cannot search the notes.
        Assert.Empty((await _client.GetFromJsonAsync<List<ReportDto>>("/api/safety/reports?q=secret"))!);
        var planner = await Send<List<ReportDto>>(Planner(HttpMethod.Get, "/api/safety/reports?q=secret"));
        Assert.Contains(planner!, r => r.Note == "secret flood note");

        foreach (var bad in new[] { "layer=nope", "type=nope", "status=nope", "layer=99", "type=99", "status=99" })
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/safety/reports?" + bad)).StatusCode);
    }
    [Fact]
    public async Task Writes_are_protected_too()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/safety/planner/alerts",
            new CreateAlertRequest(null, "Info", "T", "Message", null, Lat, Lon, 500, 60, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/safety/planner/dispatches",
            new DispatchRequest("zdmk", "Subject", "Body", null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsync("/api/safety/planner/reports/x/resolve", JsonContent.Create(new ResolveReportRequest(null)))).StatusCode);
    }

    [Fact]
    public async Task The_summary_has_kpis_gaps_and_ranked_places()
    {
        var s = await Send<PlannerSummaryDto>(Planner(HttpMethod.Get, "/api/safety/planner/summary?event=night&top=3"));
        Assert.Equal("night", s.Event);
        Assert.InRange(s.TopPriority.Count, 1, 3);
        Assert.Equal(10, s.Histogram.Count);
        Assert.All(s.FactorGaps, g => Assert.Equal("Safety", g.Layer));
        Assert.Contains(s.Kpis, k => k.Key == "poorlyLit");
        Assert.Contains("not crime statistics", string.Join(' ', s.Notes));
    }

    [Fact]
    public async Task A_planner_verifies_and_resolves_a_report()
    {
        var created = await (await _client.PostAsJsonAsync("/api/safety/reports", new CreateReportRequest("PathHazard", Lat - 0.04, Lon, null, "device-api-0005")))
            .Content.ReadFromJsonAsync<ReportDto>();

        var verified = await Send<ReportDto>(Planner(HttpMethod.Post, $"/api/safety/planner/reports/{created!.Id}/verify"));
        Assert.True(verified.VerifiedByPlanner);

        var resolved = await Send<ReportDto>(Planner(HttpMethod.Post, $"/api/safety/planner/reports/{created.Id}/resolve", new ResolveReportRequest("cleared")));
        Assert.Equal("Resolved", resolved.Status);
        Assert.Equal("cleared", resolved.ResolutionNote);

        Assert.Equal(HttpStatusCode.NotFound, (await _client.SendAsync(Planner(HttpMethod.Post, "/api/safety/planner/reports/missing/verify"))).StatusCode);
    }

    [Fact]
    public async Task An_alert_reaches_residents_in_its_circle_until_it_is_cancelled()
    {
        // A phone asks for alerts first, so the planner can see it in the area.
        await _client.GetAsync($"/api/safety/alerts?lat={Lat}&lon={Lon}&deviceId=phone-api-0001");
        var reach = await Send<ReachDto>(Planner(HttpMethod.Get, $"/api/safety/planner/reach?lat={Lat}&lon={Lon}&radius=500"));
        Assert.True(reach.DevicesInArea >= 1);

        var alert = await Send<PlannerAlertDto>(
            Planner(HttpMethod.Post, "/api/safety/planner/alerts", new CreateAlertRequest("Heat", "Warning", "Heat warning", "Drink water.", new Dictionary<string, string> { ["pl"] = "Pij wodę." }, Lat, Lon, 500, 60, null)),
            HttpStatusCode.Created);
        Assert.True(alert.DevicesInArea >= 1);

        var inside = await _client.GetFromJsonAsync<List<PlannerAlertDto>>($"/api/safety/alerts?lat={Lat}&lon={Lon}");
        Assert.Contains(inside!, a => a.Id == alert.Id && a.Translations["pl"] == "Pij wodę.");
        var outside = await _client.GetFromJsonAsync<List<PlannerAlertDto>>($"/api/safety/alerts?lat={Lat + 0.05}&lon={Lon}");
        Assert.DoesNotContain(outside!, a => a.Id == alert.Id);

        await Send<PlannerAlertDto>(Planner(HttpMethod.Delete, $"/api/safety/planner/alerts/{alert.Id}"));
        var after = await _client.GetFromJsonAsync<List<PlannerAlertDto>>($"/api/safety/alerts?lat={Lat}&lon={Lon}");
        Assert.DoesNotContain(after!, a => a.Id == alert.Id);
    }

    [Fact]
    public async Task Invalid_alerts_are_rejected_with_a_400()
    {
        var response = await _client.SendAsync(Planner(HttpMethod.Post, "/api/safety/planner/alerts",
            new CreateAlertRequest(null, "Warning", "Valid title", "Message here", null, Lat, Lon, 99999, 60, null)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("radiusMeters", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Contacting_an_agency_returns_a_simulated_reference_and_is_logged()
    {
        var agencies = await Send<List<AgencyDto>>(Planner(HttpMethod.Get, "/api/safety/planner/agencies"));
        Assert.Contains(agencies, a => a.Id == "zdmk" && !a.ContactVerified);

        var dispatch = await Send<DispatchDto>(
            Planner(HttpMethod.Post, "/api/safety/planner/dispatches", new DispatchRequest("zdmk", "Lamp out", "Please inspect the lamp.", Lat, Lon, "1-1")),
            HttpStatusCode.Created);
        Assert.StartsWith("SIM-", dispatch.Reference);
        Assert.Equal("simulated", dispatch.Delivery);

        var log = await Send<List<DispatchDto>>(Planner(HttpMethod.Get, "/api/safety/planner/dispatches"));
        Assert.Contains(log, d => d.Id == dispatch.Id);

        var bad = await _client.SendAsync(Planner(HttpMethod.Post, "/api/safety/planner/dispatches", new DispatchRequest("nobody", "Subject", "Body", null, null, null)));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Demo_data_is_added_once()
    {
        var first = await Send<Dictionary<string, int>>(Planner(HttpMethod.Post, "/api/safety/planner/demo-data"));
        var second = await Send<Dictionary<string, int>>(Planner(HttpMethod.Post, "/api/safety/planner/demo-data"));
        Assert.True(first["created"] >= 1);
        Assert.Equal(0, second["created"]);
    }

    [Fact]
    public async Task The_planner_key_is_accepted_by_CORS_preflight_for_the_web_app()
    {
        var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/safety/planner/summary");
        preflight.Headers.Add("Origin", "http://localhost:5090");
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        preflight.Headers.Add("Access-Control-Request-Headers", "x-planner-key");

        var response = await _client.SendAsync(preflight);
        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("x-planner-key", response.Headers.GetValues("Access-Control-Allow-Headers").Single(), StringComparison.OrdinalIgnoreCase);
    }
}

public class GeoEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Search_finds_places_by_part_of_the_name()
    {
        var results = await _client.GetFromJsonAsync<List<GeocodeResultDto>>("/api/geo/search?q=rynek");
        Assert.Equal("Rynek Główny, Stare Miasto", results!.Single().Label);
    }

    [Theory]
    [InlineData("/api/geo/search?q=r")]
    [InlineData("/api/geo/reverse?lat=52.2&lon=21.0")]
    public async Task Too_short_text_and_places_outside_Krakow_are_a_400(string url) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync(url)).StatusCode);

    [Fact]
    public async Task Reverse_names_the_place_or_says_there_is_none()
    {
        var found = await _client.GetFromJsonAsync<GeocodeResultDto>("/api/geo/reverse?lat=50.0616&lon=19.9372");
        Assert.Equal("Rynek Główny, Stare Miasto", found!.Label);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/geo/reverse?lat=50.12&lon=20.1")).StatusCode);
    }

    private sealed class PausedGeocoder : KrakowOpenData.Application.Abstractions.IGeocoder
    {
        public Task<IReadOnlyList<GeocodeResultDto>> SearchAsync(string query, KrakowOpenData.Domain.Common.GeoPoint? near, int limit, CancellationToken ct = default) =>
            throw new KrakowOpenData.Infrastructure.Common.UpstreamBusyException("The address search", TimeSpan.FromSeconds(12.2));

        public Task<GeocodeResultDto?> ReverseAsync(KrakowOpenData.Domain.Common.GeoPoint point, CancellationToken ct = default) =>
            throw new KrakowOpenData.Infrastructure.Common.UpstreamBusyException("The address search", TimeSpan.FromSeconds(12.2));
    }

    [Fact]
    public async Task A_paused_address_search_is_a_503_that_says_when_to_retry()
    {
        using var paused = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddSingleton<KrakowOpenData.Application.Abstractions.IGeocoder, PausedGeocoder>()));
        var response = await paused.CreateClient().GetAsync("/api/geo/search?q=rynek");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("13", response.Headers.GetValues("Retry-After").Single());
        Assert.Contains("Try again", await response.Content.ReadAsStringAsync());
    }
}
