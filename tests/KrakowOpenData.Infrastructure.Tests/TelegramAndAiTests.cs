using System.Net;
using System.Text;
using System.Text.Json;
using KrakowOpenData.Application.Safety;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Safety;
using KrakowOpenData.Infrastructure.Ai;
using KrakowOpenData.Infrastructure.Options;
using KrakowOpenData.Infrastructure.Safety;
using KrakowOpenData.Infrastructure.Telegram;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Infrastructure.Tests;

/// <summary>Records every request and answers with <see cref="Respond"/> (default: Telegram's {"ok":true}).</summary>
public sealed class FakeHttp : HttpMessageHandler, IHttpClientFactory
{
    public List<(HttpMethod Method, string Url, string Body, string? UserAgent)> Requests { get; } = [];

    public Func<HttpRequestMessage, string, HttpResponseMessage> Respond { get; set; } =
        (_, _) => Json(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":7}}""");

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!.ToString(), body, request.Headers.UserAgent.ToString()));
        return Respond(request, body);
    }
}

public sealed class FakeTriage(ReportTriage? answer) : IReportTriage
{
    public int Calls { get; private set; }
    public bool IsAvailable => true;

    public Task<ReportTriage?> TriageAsync(string type, string? note, IReadOnlyList<NearbyReport> nearby, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(answer);
    }
}

public sealed class FakeTranscriber(VoiceTranscript? answer) : IVoiceTranscriber
{
    public bool IsAvailable => answer is not null;

    public Task<VoiceTranscript?> TranscribeAsync(byte[] audio, string contentType, CancellationToken ct = default) => Task.FromResult(answer);
}

public sealed class CapturingSink : ISafetyEventSink
{
    public List<SafetyEvent> Events { get; } = [];

    public void Publish(SafetyEvent safetyEvent) => Events.Add(safetyEvent);
}

public class TelegramAndAiTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 18, 0, 0, TimeSpan.Zero);
    private static readonly GeoPoint Rynek = new(50.061734, 19.937380);

    private readonly FakeHttp _http = new();
    private readonly FakeClock _clock = new(Now);

    private sealed record Rig(
        TelegramClient Client, TelegramStore Store, TelegramOutbox Outbox, TelegramNotifier Notifier, TelegramLinkService Links,
        PresenceTracker Presence, IOptions<TelegramOptions> Options);

    private Rig Build(string token = "123:SECRET", string staff = "-100500")
    {
        var options = Microsoft.Extensions.Options.Options.Create(new TelegramOptions
        {
            BotToken = token, BotUsername = "kompas_bot", StaffChatId = staff, PublicBaseUrl = "https://opendata.al.mt", ApiBaseUrl = "https://tg.test"
        });
        var safety = Microsoft.Extensions.Options.Options.Create(new SafetyOptions { Persist = false });
        var client = new TelegramClient(_http, options);
        var store = new TelegramStore(options, safety, NullLogger<TelegramStore>.Instance);
        var outbox = new TelegramOutbox(store, client, _clock, NullLogger<TelegramOutbox>.Instance);
        var presence = new PresenceTracker(_clock);
        var notifier = new TelegramNotifier(store, outbox, presence, _clock, options);
        var links = new TelegramLinkService(store, outbox, _clock, options);
        return new Rig(client, store, outbox, notifier, links, presence, options);
    }

    private static CitizenReport Report(string? note = "Pan Jan Kowalski z bloku 5 zostawił śmieci", ReportTriage? triage = null, string device = "device-0001") =>
        new("rep-abc123def456", ReportType.LightOut, Rynek, note, Now, Now, [device], false, ReportStatus.Open, null, null, triage);

    private static ReportTriage Triage(bool personal = false, bool abuse = false, string summary = "Nie świeci <lampa> & ciemno") =>
        new("LightOut", 3, "pl", summary, abuse, personal, null, 0.9, Now);

    private static string MessageText(string body) => JsonDocument.Parse(body).RootElement.GetProperty("text").GetString()!;

    // ── Nothing happens without a token ──────────────────────────────────────
    [Fact]
    public async Task Without_a_token_nothing_is_queued_or_sent()
    {
        var rig = Build(token: "");
        rig.Notifier.ReportCreated(Report(triage: Triage()));
        rig.Outbox.Enqueue("-100500", "staff", new TelegramMessages.Message("x"));
        await rig.Outbox.SendDueAsync();

        Assert.Empty(rig.Store.Outbox());
        Assert.Empty(_http.Requests);
        Assert.Null(rig.Links.CreateCode("device-0001", null, null));
    }

    // ── Staff digest: privacy and format ─────────────────────────────────────
    [Fact]
    public void The_staff_digest_rounds_the_location_and_never_carries_the_note_or_ids()
    {
        var text = TelegramMessages.StaffDigest(Report(triage: Triage()), "https://opendata.al.mt").Text;

        Assert.StartsWith("🟠 Nowe zgłoszenie — Lampa nie świeci lub jest zbyt ciemno", text);
        Assert.Contains("Status: niezweryfikowane (zgłoszenie mieszkańca)", text);
        Assert.Contains("AI: ważność 3/3 · „Nie świeci <lampa> & ciemno” (sugestia AI)", text);
        Assert.Contains("https://www.openstreetmap.org/?mlat=50.062&mlon=19.937#map=17/50.062/19.937", text);
        Assert.Contains("Kompas Krakowa · https://opendata.al.mt/safety/", text);
        Assert.DoesNotContain("Kowalski", text);
        Assert.DoesNotContain("rep-abc", text);
        Assert.DoesNotContain("device-0001", text);
        Assert.DoesNotContain("50.0617", text);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void The_ai_summary_is_withheld_when_it_may_hold_personal_data_or_is_abuse(bool personal, bool abuse)
    {
        var text = TelegramMessages.StaffDigest(Report(triage: Triage(personal, abuse)), null).Text;
        Assert.DoesNotContain("Nie świeci", text);
        Assert.DoesNotContain("Kowalski", text);
        Assert.Contains("Lampa nie świeci", text); // the category is still there
    }

    [Fact]
    public void Without_triage_only_the_category_is_posted()
    {
        var text = TelegramMessages.StaffDigest(Report(), null).Text;
        Assert.DoesNotContain("AI:", text);
        Assert.DoesNotContain("Kowalski", text);
        Assert.EndsWith("Kompas Krakowa", text);
    }

    // ── Sending: plain text, error classes, no token in logs ─────────────────
    [Fact]
    public async Task A_new_report_queues_the_staff_digest_and_the_residents_confirmation()
    {
        var rig = Build();
        rig.Store.UpsertLink(new TelegramLink("device-0001", 42, null, Now, LinkStatus.Connected, Now));
        rig.Notifier.ReportCreated(Report(triage: Triage()));
        await rig.Outbox.SendDueAsync();

        Assert.Equal(2, _http.Requests.Count);
        Assert.All(_http.Requests, r => Assert.Equal("https://tg.test/bot123:SECRET/sendMessage", r.Url));
        Assert.Contains("\"chat_id\":-100500", _http.Requests[0].Body);
        Assert.DoesNotContain("parse_mode", _http.Requests[0].Body);
        Assert.Contains("\"chat_id\":42", _http.Requests[1].Body);
        Assert.StartsWith("Otrzymaliśmy Twoje zgłoszenie", MessageText(_http.Requests[1].Body));
        Assert.All(rig.Store.Outbox(), m => Assert.Equal(OutboxState.Sent, m.State));
    }

    [Theory]
    [InlineData(403, """{"ok":false,"description":"Forbidden: bot was blocked by the user"}""", "blocked", true, true)]
    [InlineData(400, """{"ok":false,"description":"Bad Request: chat not found"}""", "chat_not_found", true, true)]
    [InlineData(400, """{"ok":false,"description":"Bad Request: message is too long"}""", "bad_request", true, false)]
    [InlineData(500, """{"ok":false}""", "http_5xx", false, false)]
    public async Task Send_errors_are_classified(int status, string json, string error, bool permanent, bool gone)
    {
        var rig = Build();
        _http.Respond = (_, _) => FakeHttp.Json((HttpStatusCode)status, json);
        var result = await rig.Client.SendMessageAsync("42", "hi");
        Assert.Equal((false, error, permanent, gone), (result.Ok, result.Error, result.Permanent, result.Gone));
    }

    [Fact]
    public async Task Rate_limits_honour_retry_after()
    {
        var rig = Build();
        _http.Respond = (_, _) => FakeHttp.Json((HttpStatusCode)429, """{"ok":false,"parameters":{"retry_after":17}}""");
        var result = await rig.Client.SendMessageAsync("42", "hi");
        Assert.Equal(("rate_limited", 17), (result.Error, result.RetryAfter));
    }

    [Fact]
    public async Task Failed_sends_are_retried_after_5_10_15_30_seconds_then_given_up()
    {
        var rig = Build();
        _http.Respond = (_, _) => FakeHttp.Json(HttpStatusCode.BadGateway, """{"ok":false}""");
        rig.Outbox.Enqueue(42, "alert", new TelegramMessages.Message("hi"));

        foreach (var wait in TelegramOutbox.RetrySeconds)
        {
            await rig.Outbox.SendDueAsync();
            var m = Assert.Single(rig.Store.Outbox());
            Assert.Equal(OutboxState.Pending, m.State);
            Assert.Equal(_clock.UtcNow.AddSeconds(wait), m.NextAt);
            await rig.Outbox.SendDueAsync(); // not due yet: nothing is sent
            _clock.Advance(TimeSpan.FromSeconds(wait));
        }

        await rig.Outbox.SendDueAsync();
        Assert.Equal(OutboxState.Failed, Assert.Single(rig.Store.Outbox()).State);
        Assert.Equal(5, _http.Requests.Count);
    }

    [Fact]
    public async Task A_chat_that_blocked_the_bot_stops_receiving()
    {
        var rig = Build();
        rig.Store.UpsertLink(new TelegramLink("device-0001", 42, null, Now, LinkStatus.Connected, Now));
        _http.Respond = (_, _) => FakeHttp.Json(HttpStatusCode.Forbidden, """{"ok":false}""");
        rig.Outbox.Enqueue(42, "alert", new TelegramMessages.Message("one"));
        rig.Outbox.Enqueue(42, "alert", new TelegramMessages.Message("two"));
        await rig.Outbox.SendDueAsync();

        Assert.Equal(LinkStatus.NotReceiving, rig.Store.LinkOf("device-0001")!.Status);
        Assert.Single(_http.Requests);                         // the second message was dropped, not sent
        Assert.Equal(OutboxState.Failed, Assert.Single(rig.Store.Outbox()).State);
    }

    // ── Linking ──────────────────────────────────────────────────────────────
    [Fact]
    public void A_link_code_is_one_time_expires_after_15_minutes_and_is_stored_only_hashed()
    {
        var rig = Build();
        var code = rig.Links.CreateCode("device-0001", 50.06178, 19.93741)!;
        var raw = code.Link.Split("start=")[1];
        Assert.StartsWith("https://t.me/kompas_bot?start=", code.Link);
        Assert.Matches("^[A-Za-z0-9_-]{32}$", raw);
        Assert.Equal(Now.AddMinutes(15), code.ExpiresAt);

        var (result, link) = rig.Store.AcceptCode(raw, 42, Now.AddMinutes(1));
        Assert.Equal(TelegramStore.AcceptResult.Ok, result);
        Assert.Equal(new GeoPoint(50.062, 19.937), link!.Area);   // "my area", rounded
        Assert.Equal(TelegramStore.AcceptResult.Used, rig.Store.AcceptCode(raw, 42, Now.AddMinutes(2)).Result);
        Assert.Equal(TelegramStore.AcceptResult.Unknown, rig.Store.AcceptCode(TelegramStore.Hash(raw), 42, Now).Result);

        var late = rig.Links.CreateCode("device-0002", null, null)!.Link.Split("start=")[1];
        Assert.Equal(TelegramStore.AcceptResult.Expired, rig.Store.AcceptCode(late, 43, Now.AddMinutes(16)).Result);
        Assert.True(rig.Links.IsLinked("device-0001"));
    }

    [Fact]
    public void Unlinking_from_the_app_tells_the_chat_once()
    {
        var rig = Build();
        rig.Store.UpsertLink(new TelegramLink("device-0001", 42, null, Now, LinkStatus.Connected, Now));
        Assert.True(rig.Links.Unlink("device-0001"));
        Assert.False(rig.Links.Unlink("device-0001"));
        Assert.Equal(TelegramMessages.Kinds.Unlinked, Assert.Single(rig.Store.Outbox()).Kind);
        Assert.Throws<SafetyValidationException>(() => rig.Links.Unlink("bad id!"));
    }

    // ── Who gets an alert ────────────────────────────────────────────────────
    [Fact]
    public void An_alert_reaches_residents_whose_area_or_last_position_is_inside_it()
    {
        var rig = Build();
        rig.Store.UpsertLink(new TelegramLink("in-area-01", 1, new GeoPoint(50.062, 19.937), Now, LinkStatus.Connected, Now));
        rig.Store.UpsertLink(new TelegramLink("far-away-01", 2, new GeoPoint(50.10, 20.05), Now, LinkStatus.Connected, Now));
        rig.Store.UpsertLink(new TelegramLink("seen-here-1", 3, null, Now, LinkStatus.Connected, Now));
        rig.Store.UpsertLink(new TelegramLink("blocked-001", 4, new GeoPoint(50.062, 19.937), Now, LinkStatus.NotReceiving, Now));
        rig.Presence.Touch("seen-here-1", new GeoPoint(50.0625, 19.9385));

        var alert = new PlannerAlert("alt-1", ScoreLayer.Heat, AlertSeverity.Warning, "Upał", "Pij wodę", new Dictionary<string, string>(),
            Rynek, 500, Now, Now.AddHours(2), null, AlertStatus.Active);
        rig.Notifier.AlertCreated(alert);

        Assert.Equal(new[] { "1", "3" }, rig.Store.Outbox().Select(m => m.ChatId).Order().ToArray());
        var text = rig.Store.Outbox()[0].Text;
        Assert.StartsWith("🔴 Ostrzeżenie: Upał\nPij wodę\nObszar: 500 m", text);
        Assert.Contains("Ważny do: 22:00, 04.10.2026 (czas Krakowa)", text);
    }

    // ── The bot ──────────────────────────────────────────────────────────────
    private (TelegramBotService Bot, JsonFileSafetyStore Safety, CapturingSink Sink) Bot(Rig rig, VoiceTranscript? voice)
    {
        var safety = new JsonFileSafetyStore(Microsoft.Extensions.Options.Options.Create(new SafetyOptions { Persist = false }), NullLogger<JsonFileSafetyStore>.Instance);
        var sink = new CapturingSink();
        var bot = new TelegramBotService(rig.Client, rig.Store, rig.Outbox, new ReportService(safety, _clock), safety, sink,
            new FakeTriage(null), new FakeTranscriber(voice), new VoiceQuota(_clock), _clock, rig.Options, NullLogger<TelegramBotService>.Instance);
        return (bot, safety, sink);
    }

    private static JsonElement Update(string messageFields) =>
        JsonDocument.Parse("{\"update_id\":1,\"message\":{\"chat\":{\"id\":42,\"type\":\"private\"},\"from\":{\"id\":42,\"is_bot\":false}," + messageFields + "}}").RootElement;

    [Fact]
    public async Task Start_with_a_code_links_the_chat_and_confirms_through_the_outbox()
    {
        var rig = Build();
        var (bot, _, _) = Bot(rig, null);
        var code = rig.Links.CreateCode("device-0001", null, null)!.Link.Split("start=")[1];

        await bot.HandleUpdateAsync(Update("\"text\":\"/start " + code + "\""));
        await bot.HandleUpdateAsync(Update("\"text\":\"/start " + code + "\"")); // replayed update: the code is used, nothing breaks

        Assert.True(rig.Links.IsLinked("device-0001"));
        Assert.Equal(TelegramMessages.Kinds.Linked, Assert.Single(rig.Store.Outbox()).Kind);
        Assert.Contains("już użyty", MessageText(_http.Requests.Single().Body));
    }

    [Fact]
    public async Task A_voice_message_becomes_an_unverified_report_after_the_location()
    {
        var rig = Build();
        var (bot, safety, sink) = Bot(rig, new VoiceTranscript("Na Plantach nie świeci lampa", "pl", Triage()));
        _http.Respond = (req, _) => req.RequestUri!.AbsolutePath.EndsWith("/getFile")
            ? FakeHttp.Json(HttpStatusCode.OK, """{"ok":true,"result":{"file_path":"voice/1.oga","file_size":4}}""")
            : req.RequestUri.AbsolutePath.Contains("/file/")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) }
                : FakeHttp.Json(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":1}}""");

        await bot.HandleUpdateAsync(Update("\"voice\":{\"file_id\":\"F1\",\"duration\":5,\"mime_type\":\"audio/ogg\"}"));
        Assert.Contains("Na Plantach nie świeci lampa", MessageText(_http.Requests.Last().Body));
        Assert.Contains("request_location", _http.Requests.Last().Body);

        await bot.HandleUpdateAsync(Update("\"location\":{\"latitude\":50.0617,\"longitude\":19.9373}"));
        var report = Assert.Single(await safety.ListReportsAsync());
        Assert.Equal((ReportType.LightOut, false, "Na Plantach nie świeci lampa"), (report.Type, report.VerifiedByPlanner, report.Note));
        Assert.Equal(TelegramBotService.DeviceIdFor(42), report.DeviceIds.Single());
        Assert.StartsWith("tg-", report.DeviceIds.Single());
        Assert.NotNull(report.Triage);
        Assert.Equal(SafetyEventKind.ReportCreated, Assert.Single(sink.Events).Kind);
        Assert.StartsWith("Zgłoszenie przyjęte", MessageText(_http.Requests.Last().Body));
    }

    [Fact]
    public async Task A_chat_over_the_voice_quota_is_asked_to_type_instead()
    {
        var rig = Build();
        var (bot, _, _) = Bot(rig, new VoiceTranscript("Na Plantach nie świeci lampa", "pl", Triage()));
        _http.Respond = (req, _) => req.RequestUri!.AbsolutePath.EndsWith("/getFile")
            ? FakeHttp.Json(HttpStatusCode.OK, """{"ok":true,"result":{"file_path":"voice/1.oga","file_size":4}}""")
            : req.RequestUri.AbsolutePath.Contains("/file/")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) }
                : FakeHttp.Json(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":1}}""");

        for (var i = 0; i < VoiceQuota.PerDevicePerHour; i++)
            await bot.HandleUpdateAsync(Update("\"voice\":{\"file_id\":\"F1\",\"duration\":5}"));
        var downloads = _http.Requests.Count(r => r.Url.Contains("/file/"));

        await bot.HandleUpdateAsync(Update("\"voice\":{\"file_id\":\"F1\",\"duration\":5}"));
        Assert.Equal(downloads, _http.Requests.Count(r => r.Url.Contains("/file/")));   // nothing downloaded or transcribed
        Assert.Contains("Opisz problem tekstem", MessageText(_http.Requests.Last().Body));
    }

    [Fact]
    public void Text_triage_has_its_own_hourly_limit_per_chat()
    {
        var quota = new VoiceQuota(_clock);
        for (var i = 0; i < VoiceQuota.TriagePerDevicePerHour; i++) Assert.True(quota.TryTakeTriage("tg-chat-a"));
        Assert.False(quota.TryTakeTriage("tg-chat-a"));
        Assert.True(quota.TryTakeTriage("tg-chat-b"));
        Assert.True(quota.TryTake("tg-chat-a"));   // voice is counted separately
    }

    [Fact]
    public async Task Without_ai_a_voice_message_gets_a_polite_refusal()
    {
        var rig = Build();
        var (bot, _, _) = Bot(rig, null);
        await bot.HandleUpdateAsync(Update("\"voice\":{\"file_id\":\"F1\",\"duration\":5}"));
        Assert.Contains("niedostępne", MessageText(_http.Requests.Single().Body));
    }

    // ── Pipeline: triage before notifying ────────────────────────────────────
    [Fact]
    public async Task The_pipeline_saves_the_ai_suggestion_on_a_new_report_before_notifying()
    {
        var rig = Build();
        var store = new JsonFileSafetyStore(Microsoft.Extensions.Options.Options.Create(new SafetyOptions { Persist = false }), NullLogger<JsonFileSafetyStore>.Instance);
        await store.SaveReportAsync(Report());
        var triage = new FakeTriage(Triage());
        var pipeline = new SafetyEventPipeline(store, triage, rig.Notifier, _clock, NullLogger<SafetyEventPipeline>.Instance);

        await pipeline.HandleAsync(new SafetyEvent(SafetyEventKind.ReportCreated, "rep-abc123def456"));
        await pipeline.HandleAsync(new SafetyEvent(SafetyEventKind.ReportCreated, "rep-abc123def456")); // already triaged: no second call

        Assert.Equal(1, triage.Calls);
        Assert.Equal(3, (await store.GetReportAsync("rep-abc123def456"))!.Triage!.Severity);
        Assert.Contains(rig.Store.Outbox(), m => m.Kind == TelegramMessages.Kinds.Staff && m.Text.Contains("ważność 3/3"));
    }

    [Fact]
    public void Every_server_side_client_sends_a_user_agent_cloudflare_accepts()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSafetyNotifications(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        using var sp = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var factory = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IHttpClientFactory>(sp);
        foreach (var name in new[] { TelegramClient.HttpClientName, WorkersAiClient.HttpClientName })
            Assert.Equal(NotificationsRegistration.UserAgent, factory.CreateClient(name).DefaultRequestHeaders.UserAgent.ToString());
    }

    // ── Workers AI client ────────────────────────────────────────────────────
    [Fact]
    public void Model_output_is_validated_and_coerced()
    {
        var json = JsonDocument.Parse("""
            {"suggestedType":"3","severity":7,"language":"pl","summaryPl":"%s","isAbuse":false,"duplicateOf":"rep-elsewhere","confidence":1.7}
            """.Replace("%s", new string('a', 300))).RootElement;
        var t = WorkersAiClient.ParseTriage(json, new HashSet<string> { "rep-near" }, Now)!;

        Assert.Null(t.SuggestedType);              // numbers are not type names
        Assert.Equal(3, t.Severity);
        Assert.Equal(120, t.SummaryPl!.Length);
        Assert.Null(t.DuplicateOf);                // not one of the reports we sent
        Assert.Equal(1, t.Confidence);
        Assert.True(t.ContainsPersonalData);       // missing flag = withhold the summary
        Assert.False(t.SummaryIsShareable);

        var ok = WorkersAiClient.ParseTriage(JsonDocument.Parse("""{"suggestedType":"heatspot","duplicateOf":"rep-near","containsPersonalData":false}""").RootElement,
            new HashSet<string> { "rep-near" }, Now)!;
        Assert.Equal(("HeatSpot", "rep-near", 2), (ok.SuggestedType, ok.DuplicateOf, ok.Severity));
        Assert.Null(WorkersAiClient.ParseTriage(JsonDocument.Parse("[1]").RootElement, new HashSet<string>(), Now));
    }

    [Fact]
    public async Task Without_a_worker_url_ai_is_off_and_nothing_is_called()
    {
        var ai = new WorkersAiClient(_http, Microsoft.Extensions.Options.Options.Create(new AiOptions()), _clock, NullLogger<WorkersAiClient>.Instance);
        Assert.False(ai.IsAvailable);
        Assert.Null(await ai.TriageAsync("LightOut", "ciemno", []));
        Assert.Null(await ai.TranscribeAsync([1, 2, 3], "audio/ogg"));
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task Triage_posts_to_the_worker_with_the_key_and_a_failure_returns_null()
    {
        var ai = new WorkersAiClient(_http, Microsoft.Extensions.Options.Options.Create(new AiOptions { BaseUrl = "https://krakow-ai.example.workers.dev/", Key = "k" }),
            _clock, NullLogger<WorkersAiClient>.Instance);
        _http.Respond = (req, _) =>
        {
            Assert.Equal("k", req.Headers.GetValues("X-Ai-Key").Single());
            return FakeHttp.Json(HttpStatusCode.OK, """{"suggestedType":"LightOut","severity":2,"summaryPl":"Ciemno","isAbuse":false,"containsPersonalData":false}""");
        };
        var t = await ai.TriageAsync("LightOut", "ciemno", [new NearbyReport("rep-1", "LightOut", null)]);
        Assert.Equal("https://krakow-ai.example.workers.dev/triage", _http.Requests.Single().Url);
        Assert.True(t!.SummaryIsShareable);

        _http.Respond = (_, _) => FakeHttp.Json(HttpStatusCode.BadGateway, """{"error":"model failed"}""");
        Assert.Null(await ai.TriageAsync("LightOut", "ciemno", []));
    }
}
