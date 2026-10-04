using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using KrakowOpenData.Api.Tests.Fakes;
using KrakowOpenData.Application.Safety;
using Microsoft.Extensions.DependencyInjection;

namespace KrakowOpenData.Api.Tests;

public class NotificationEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private CapturingSafetyEventSink Sink => factory.Services.GetRequiredService<CapturingSafetyEventSink>();

    [Fact]
    public async Task A_new_report_is_published_once_and_a_merged_repeat_is_not_published_as_new()
    {
        var first = await _client.PostAsJsonAsync("/api/safety/reports", new CreateReportRequest("DustCloud", 50.0301, 19.9502, "pył z budowy", "notify-device-a"));
        var created = (await first.Content.ReadFromJsonAsync<ReportDto>())!;
        var second = await _client.PostAsJsonAsync("/api/safety/reports", new CreateReportRequest("DustCloud", 50.0302, 19.9503, null, "notify-device-b"));
        var merged = (await second.Content.ReadFromJsonAsync<ReportDto>())!;

        Assert.Equal(created.Id, merged.Id);
        var mine = Sink.Events.Where(e => e.Id == created.Id).ToList();
        Assert.Single(mine, e => e.Kind == SafetyEventKind.ReportCreated);
        Assert.Single(mine, e => e.Kind == SafetyEventKind.ReportMerged && e.DeviceId == "notify-device-b");
        Assert.Null(created.Triage); // residents never see the AI suggestion
    }

    [Fact]
    public async Task Planner_actions_publish_status_changes_and_alerts()
    {
        var report = (await (await _client.PostAsJsonAsync("/api/safety/reports",
            new CreateReportRequest("StrongFumes", 50.0451, 19.9101, null, "notify-device-c"))).Content.ReadFromJsonAsync<ReportDto>())!;
        var verify = new HttpRequestMessage(HttpMethod.Post, $"/api/safety/planner/reports/{report.Id}/verify");
        verify.Headers.Add("X-Planner-Key", "demo-planner");
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(verify)).StatusCode);

        var alert = new HttpRequestMessage(HttpMethod.Post, "/api/safety/planner/alerts")
        {
            Content = JsonContent.Create(new CreateAlertRequest("Air", "Warning", "Opary", "Zamknij okna", null, 50.0451, 19.9101, 500, 60, null))
        };
        alert.Headers.Add("X-Planner-Key", "demo-planner");
        var created = (await (await _client.SendAsync(alert)).Content.ReadFromJsonAsync<PlannerAlertDto>())!;

        Assert.Contains(Sink.Events, e => e.Kind == SafetyEventKind.ReportVerified && e.Id == report.Id);
        Assert.Contains(Sink.Events, e => e.Kind == SafetyEventKind.AlertCreated && e.Id == created.Id);
    }

    [Fact]
    public async Task Without_a_bot_token_telegram_linking_is_unavailable()
    {
        var status = await _client.GetFromJsonAsync<TelegramStatusDto>("/api/safety/telegram/link?deviceId=notify-device-d");
        Assert.Equal(new TelegramStatusDto(false, false), status);
        var link = await _client.PostAsJsonAsync("/api/safety/telegram/link", new TelegramLinkRequest("notify-device-d", null, null));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, link.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/safety/telegram/link?deviceId=x")).StatusCode);
    }

    [Fact]
    public async Task Device_ids_reserved_for_the_bot_are_refused_on_every_public_endpoint()
    {
        const string bot = "tg-0123456789abcdef";   // what TelegramBotService.DeviceIdFor makes for a chat
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/safety/telegram/link?deviceId={bot}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/safety/telegram/link", new TelegramLinkRequest(bot, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.DeleteAsync($"/api/safety/telegram/link?deviceId=TG-0123456789abcdef")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync("/api/safety/reports", new CreateReportRequest("DustCloud", 50.0301, 19.9502, null, bot))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PostAsJsonAsync("/api/safety/reports/rep-x/confirm", new ConfirmReportRequest(bot))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/safety/alerts?lat=50.06&lon=19.94&deviceId={bot}")).StatusCode);
    }

    [Fact]
    public async Task Without_a_worker_voice_input_is_unavailable()
    {
        var audio = new ByteArrayContent([1, 2, 3]);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await _client.PostAsync("/api/safety/voice?deviceId=notify-device-e", audio)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/safety/voice?deviceId=bad!", audio)).StatusCode);
    }
}
