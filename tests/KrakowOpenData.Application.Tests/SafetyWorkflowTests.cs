using KrakowOpenData.Application.Safety;
using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Safety;
using KrakowOpenData.Domain.UrbanSpace;

namespace KrakowOpenData.Application.Tests;

public class ReportServiceTests
{
    private static CreateReportRequest Request(string type = "LightOut", double? lat = null, double? lon = null, string device = "device-0001", string? note = null) =>
        new(type, lat ?? SafetyWorld.Centre.Latitude, lon ?? SafetyWorld.Centre.Longitude, note, device);

    [Fact]
    public async Task A_new_report_starts_open_with_one_supporter_and_no_note_in_the_public_view()
    {
        var created = await new SafetyWorld().Reports().CreateAsync(Request(note: "dark since Monday"));
        Assert.Equal("Open", created.Status);
        Assert.Equal(1, created.Supporters);
        Assert.Equal("Safety", created.Layer);
        Assert.Null(created.Note);   // notes are for planners only
    }

    [Fact]
    public async Task The_same_issue_in_the_same_cell_is_merged_as_a_confirmation_not_duplicated()
    {
        var world = new SafetyWorld();
        var first = await world.Reports().CreateAsync(Request(device: "device-0001"));
        var second = await world.Reports().CreateAsync(Request(device: "device-0002", lat: SafetyWorld.Centre.Latitude + 0.0002));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(2, second.Supporters);
        Assert.Single(await world.Store.ListReportsAsync());
    }

    [Fact]
    public async Task A_device_can_send_at_most_five_reports_an_hour()
    {
        var world = new SafetyWorld();
        for (var i = 0; i < ReportService.MaxReportsPerDevicePerHour; i++)
            await world.Reports().CreateAsync(Request(lat: SafetyWorld.Centre.Latitude + i * 0.01));   // a different cell each time

        await Assert.ThrowsAsync<SafetyRateLimitException>(() => world.Reports().CreateAsync(Request(lat: SafetyWorld.Centre.Latitude + 0.07)));

        world.Clock.UtcNow = world.Clock.UtcNow.AddHours(2);
        await world.Reports().CreateAsync(Request(lat: SafetyWorld.Centre.Latitude + 0.07));   // allowed again
    }

    [Theory]
    [InlineData("Nonsense", "device-0001", 50.06, 19.93, "type")]
    [InlineData("LightOut", "short", 50.06, 19.93, "deviceId")]
    [InlineData("LightOut", "bad device!!", 50.06, 19.93, "deviceId")]
    [InlineData("LightOut", "device-0001", 10.0, 19.93, "lat,lon")]
    [InlineData("LightOut", "device-0001", 52.23, 21.01, "lat,lon")]   // Warsaw
    public async Task Invalid_reports_are_rejected_with_the_offending_field(string type, string device, double lat, double lon, string field)
    {
        var ex = await Assert.ThrowsAsync<SafetyValidationException>(() => new SafetyWorld().Reports().CreateAsync(new CreateReportRequest(type, lat, lon, null, device)));
        Assert.Equal(field, ex.Field);
    }

    [Fact]
    public async Task Notes_are_trimmed_to_200_characters_and_stripped_of_control_characters()
    {
        var world = new SafetyWorld();
        var created = await world.Reports().CreateAsync(Request(note: "x\u0007" + new string('a', 400)));
        var stored = await world.Store.GetReportAsync(created.Id);
        Assert.Equal(ReportService.MaxNoteLength, stored!.Note!.Length);
        Assert.DoesNotContain('\u0007', stored.Note);
    }

    [Fact]
    public async Task Confirming_adds_a_supporter_once_and_unknown_reports_return_null()
    {
        var world = new SafetyWorld();
        var created = await world.Reports().CreateAsync(Request());

        Assert.Equal(2, (await world.Reports().ConfirmAsync(created.Id, "device-0002"))!.Supporters);
        Assert.Equal(2, (await world.Reports().ConfirmAsync(created.Id, "device-0002"))!.Supporters);
        Assert.Null(await world.Reports().ConfirmAsync("missing", "device-0002"));
    }

    [Fact]
    public async Task Planner_actions_verify_and_resolve_and_resolved_reports_cannot_be_confirmed()
    {
        var world = new SafetyWorld();
        var created = await world.Reports().CreateAsync(Request(note: "check this"));

        Assert.True((await world.Reports().VerifyAsync(created.Id))!.VerifiedByPlanner);
        var resolved = await world.Reports().ResolveAsync(created.Id, "fixed");
        Assert.Equal("Resolved", resolved!.Status);
        Assert.Equal("fixed", resolved.ResolutionNote);
        Assert.Null(await world.Reports().ConfirmAsync(created.Id, "device-0002"));
    }

    [Fact]
    public async Task Listing_hides_resolved_and_old_reports_and_filters_by_distance()
    {
        var world = new SafetyWorld();
        var near = await world.Reports().CreateAsync(Request(device: "device-0001"));
        await world.Reports().CreateAsync(Request(device: "device-0001", lat: SafetyWorld.Centre.Latitude + 0.05));
        var done = await world.Reports().CreateAsync(Request(type: "NoShade", device: "device-0003"));
        await world.Reports().ResolveAsync(done.Id, null);

        var open = await world.Reports().ListAsync(null, 0, includeResolved: false, includeNotes: false, 100);
        Assert.Equal(2, open.Count);
        Assert.Equal(3, (await world.Reports().ListAsync(null, 0, includeResolved: true, includeNotes: false, 100)).Count);
        Assert.Equal(new[] { near.Id }, (await world.Reports().ListAsync(SafetyWorld.Centre, 500, false, false, 100)).Select(r => r.Id).Where(id => id == near.Id).ToArray());

        world.Clock.UtcNow = world.Clock.UtcNow.AddDays(40);
        Assert.Empty(await world.Reports().ListAsync(null, 0, includeResolved: true, includeNotes: false, 100));
    }
}

public class AlertServiceTests
{
    private static CreateAlertRequest Alert(double? lat = null, double? lon = null, double radius = 800, int minutes = 120, string severity = "Warning", string? layer = "Heat") =>
        new(layer, severity, "Heat warning", "Drink water and find shade.", new Dictionary<string, string> { ["pl"] = "Pij wodę.", ["xx"] = "ignored" },
            lat ?? SafetyWorld.Centre.Latitude, lon ?? SafetyWorld.Centre.Longitude, radius, minutes, null);

    private static AlertService Service(SafetyWorld world, out PresenceTracker presence)
    {
        presence = new PresenceTracker(world.Clock);
        return new AlertService(world.Store, presence, world.Clock);
    }

    [Fact]
    public async Task An_alert_reaches_the_phones_inside_its_circle_only()
    {
        var world = new SafetyWorld();
        var svc = Service(world, out var presence);
        presence.Touch("phone-inside-1", SafetyWorld.Offset(SafetyWorld.Centre, 100, 0));
        presence.Touch("phone-inside-2", SafetyWorld.Offset(SafetyWorld.Centre, 0, 300));
        presence.Touch("phone-outside", SafetyWorld.Offset(SafetyWorld.Centre, 3000, 0));

        var created = await svc.CreateAsync(Alert());
        Assert.Equal(2, created.DevicesInArea);

        Assert.Single(await svc.ForPointAsync(SafetyWorld.Offset(SafetyWorld.Centre, 500, 0), "phone-x"));
        Assert.Empty(await svc.ForPointAsync(SafetyWorld.Offset(SafetyWorld.Centre, 2000, 0), "phone-y"));
    }

    [Fact]
    public async Task Translations_keep_only_supported_languages()
    {
        var world = new SafetyWorld();
        var created = await Service(world, out _).CreateAsync(Alert());
        Assert.Equal(["pl"], created.Translations.Keys);
    }

    [Fact]
    public async Task Alerts_expire_and_can_be_cancelled()
    {
        var world = new SafetyWorld();
        var svc = Service(world, out _);
        var created = await svc.CreateAsync(Alert(minutes: 60));
        var other = await svc.CreateAsync(Alert(minutes: 600));

        world.Clock.UtcNow = world.Clock.UtcNow.AddMinutes(90);
        Assert.Equal(new[] { other.Id }, (await svc.ForPointAsync(SafetyWorld.Centre, null)).Select(a => a.Id).ToArray());

        Assert.Equal("Cancelled", (await svc.CancelAsync(other.Id))!.Status);
        Assert.Empty(await svc.ForPointAsync(SafetyWorld.Centre, null));
        Assert.Null(await svc.CancelAsync("missing"));
        Assert.Equal(2, (await svc.ListAsync(includeInactive: true)).Count);
        Assert.Empty(await svc.ListAsync(includeInactive: false));
        Assert.NotNull(created);
    }

    [Fact]
    public async Task More_severe_alerts_come_first()
    {
        var world = new SafetyWorld();
        var svc = Service(world, out _);
        await svc.CreateAsync(Alert(severity: "Info"));
        await svc.CreateAsync(Alert(severity: "Critical"));
        Assert.Equal("Critical", (await svc.ForPointAsync(SafetyWorld.Centre, null))[0].Severity);
    }

    [Theory]
    [InlineData("Severe", 800, 60, "severity")]
    [InlineData("Warning", 20, 60, "radiusMeters")]
    [InlineData("Warning", 9000, 60, "radiusMeters")]
    [InlineData("Warning", 800, 1, "durationMinutes")]
    [InlineData("Warning", 800, 5000, "durationMinutes")]
    public async Task Invalid_alerts_are_rejected(string severity, double radius, int minutes, string field)
    {
        var ex = await Assert.ThrowsAsync<SafetyValidationException>(() => Service(new SafetyWorld(), out _).CreateAsync(Alert(radius: radius, minutes: minutes, severity: severity)));
        Assert.Equal(field, ex.Field);
    }

    [Fact]
    public async Task The_centre_must_be_in_Krakow_and_the_text_must_not_be_empty()
    {
        var svc = Service(new SafetyWorld(), out _);
        Assert.Equal("lat,lon", (await Assert.ThrowsAsync<SafetyValidationException>(() => svc.CreateAsync(Alert(lat: 52.2, lon: 21.0)))).Field);
        Assert.Equal("title", (await Assert.ThrowsAsync<SafetyValidationException>(() => svc.CreateAsync(Alert() with { Title = " " }))).Field);
        Assert.Equal("message", (await Assert.ThrowsAsync<SafetyValidationException>(() => svc.CreateAsync(Alert() with { Message = "" }))).Field);
    }

    [Fact]
    public void Presence_forgets_phones_after_15_minutes_and_ignores_bad_ids()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var presence = new PresenceTracker(clock);
        presence.Touch("phone-1", SafetyWorld.Centre);
        presence.Touch("", SafetyWorld.Centre);
        presence.Touch(new string('x', 200), SafetyWorld.Centre);

        Assert.Equal(1, presence.CountActive());
        clock.UtcNow = clock.UtcNow.AddMinutes(16);
        Assert.Equal(0, presence.CountActive());
    }
}

public class AgencyServiceTests
{
    private static AgencyService Service(SafetyWorld world, FakeGateway gateway) => new(world.Store, gateway, world.Clock);

    [Fact]
    public async Task Contacting_an_agency_is_recorded_with_a_reference_and_goes_through_the_gateway()
    {
        var world = new SafetyWorld();
        var gateway = new FakeGateway();
        var d = await Service(world, gateway).DispatchAsync(new DispatchRequest("zdmk", "Lamp out in 54-93", "Please inspect.", 50.06, 19.93, "54-93"));

        Assert.Equal("SIM-TEST-0001", d.Reference);
        Assert.Equal("simulated", d.Delivery);
        Assert.Single(gateway.Sent);
        Assert.Equal("zdmk", gateway.Sent[0].Agency.Id);
        Assert.Single(await Service(world, gateway).ListDispatchesAsync());
    }

    [Fact]
    public async Task Unknown_agencies_and_empty_messages_are_rejected()
    {
        var svc = Service(new SafetyWorld(), new FakeGateway());
        Assert.Equal("agencyId", (await Assert.ThrowsAsync<SafetyValidationException>(() => svc.DispatchAsync(new DispatchRequest("nobody", "Subject", "Body text", null, null, null)))).Field);
        Assert.Equal("subject", (await Assert.ThrowsAsync<SafetyValidationException>(() => svc.DispatchAsync(new DispatchRequest("zdmk", "", "Body text", null, null, null)))).Field);
        Assert.Equal("lat,lon", (await Assert.ThrowsAsync<SafetyValidationException>(() => svc.DispatchAsync(new DispatchRequest("zdmk", "Subject", "Body", 10, 10, null)))).Field);
    }

    [Fact]
    public void Numbers_taken_from_press_coverage_are_marked_unverified()
    {
        var withPhone = AgencyService.Agencies.Where(a => a.Phone is not null).ToList();
        Assert.NotEmpty(withPhone);
        Assert.All(withPhone, a => Assert.False(a.ContactVerified));
        Assert.Contains(AgencyService.Agencies, a => a.Id == "portal" && a.Url == "https://kontakt.krakow.pl");
    }
}

public class PlannerServiceTests
{
    private static PlannerService Planner(SafetyWorld world) =>
        new(world.Scores(), world.Store, new PresenceTracker(world.Clock));

    [Fact]
    public async Task The_summary_ranks_the_worst_served_busy_places_first_and_explains_each()
    {
        var world = new SafetyWorld();
        // A busy but unserved patch away from the served block: many lamps (so many people), no water, park, police or stop.
        for (var i = 0; i < 10; i++)
        for (var j = 0; j < 15; j++)
            world.Lights.Items.Add(new StreetLight($"far{i}-{j}", SafetyWorld.Offset(SafetyWorld.Centre, 900 + i * 15, 900 + j * 15), StreetLightTechnology.Unknown, null, null, null, null, null, "T"));

        var summary = await Planner(world).GetSummaryAsync(PlanningEvent.Both, 5);

        Assert.True(summary.Cells > 1);
        Assert.Equal(summary.TopPriority.OrderByDescending(c => c.Priority).Select(c => c.CellId), summary.TopPriority.Select(c => c.CellId));
        var worst = summary.TopPriority[0];
        Assert.True(worst.Latitude > SafetyWorld.Centre.Latitude + 0.005, "the unserved patch should rank first");
        Assert.True(worst.Priority > 0);
        Assert.NotEmpty(worst.WeakFactors);
        Assert.NotEmpty(worst.Actions);
        Assert.Equal(10, summary.Histogram.Count);
        Assert.Equal(summary.Cells, summary.Histogram.Sum(b => b.Cells));
        Assert.Equal("both", summary.Event);
    }

    [Fact]
    public async Task Gaps_and_kpis_cover_only_the_layers_of_the_event()
    {
        var world = new SafetyWorld();
        var heat = await Planner(world).GetSummaryAsync(PlanningEvent.Heat, 3);
        var night = await Planner(world).GetSummaryAsync(PlanningEvent.Night, 3);

        Assert.All(heat.FactorGaps, g => Assert.Equal("Heat", g.Layer));
        Assert.All(night.FactorGaps, g => Assert.Equal("Safety", g.Layer));
        Assert.Contains(heat.Kpis, k => k.Key == "noWater500");
        Assert.DoesNotContain(heat.Kpis, k => k.Key == "poorlyLit");
        Assert.Contains(night.Kpis, k => k.Key == "poorlyLit");
    }

    [Fact]
    public async Task Report_counts_alerts_and_phones_show_up_in_the_summary()
    {
        var world = new SafetyWorld();
        var presence = new PresenceTracker(world.Clock);
        presence.Touch("phone-1", SafetyWorld.Centre);
        var alerts = new AlertService(world.Store, presence, world.Clock);
        await alerts.CreateAsync(new CreateAlertRequest(null, "Info", "Hello", "World", null, SafetyWorld.Centre.Latitude, SafetyWorld.Centre.Longitude, 500, 60, null));
        var created = await world.Reports().CreateAsync(new CreateReportRequest("NoShade", SafetyWorld.Centre.Latitude, SafetyWorld.Centre.Longitude, null, "device-0001"));
        await world.Reports().VerifyAsync(created.Id);

        var summary = await new PlannerService(world.Scores(), world.Store, presence).GetSummaryAsync(PlanningEvent.Heat, 3);

        Assert.Equal(1, summary.ActiveAlerts);
        Assert.Equal(1, summary.DevicesActive);
        var shade = summary.Reports.Single(r => r.Type == "NoShade");
        Assert.Equal((1, 1, 1), (shade.Open, shade.Last24Hours, shade.Verified));
        Assert.Equal(1, summary.Kpis.Single(k => k.Key == "openReports").Value);
    }

    [Fact]
    public async Task Demo_data_puts_reports_where_the_scores_are_lowest_and_runs_only_once()
    {
        var world = new SafetyWorld();
        for (var i = 0; i < 12; i++)
            world.Lights.Items.Add(new StreetLight($"dim{i}", SafetyWorld.Offset(SafetyWorld.Centre, 1200 + i, 1200 + i), StreetLightTechnology.Unknown, null, null, null, null, null, "T"));

        var demo = new DemoDataService(world.Scores(), world.Store, world.Clock);
        var created = await demo.SeedAsync();
        Assert.InRange(created, 1, 11);
        Assert.Equal(0, await demo.SeedAsync());
        Assert.All(await world.Store.ListReportsAsync(), r => Assert.StartsWith("DEMO", r.Note));
    }
}
