using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Safety;
using KrakowOpenData.Domain.Safety;
using KrakowOpenData.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Infrastructure.Ai;

/// <summary>
/// Calls the Cloudflare Worker in workers/ai: POST {Ai:BaseUrl}/triage (a suggestion for a report text) and
/// POST {Ai:BaseUrl}/transcribe (voice to text, then triage). The shared key goes in X-Ai-Key and never reaches a browser.
/// When Ai:BaseUrl is empty, or the Worker fails or is slow, both return null and the app carries on without AI.
/// Everything the model returns is checked here again: the type must be a <see cref="ReportType"/> NAME (numbers are rejected),
/// severity is clamped to 1–3, the summary to 120 characters, and a duplicate must be one of the reports we sent.
/// </summary>
public sealed class WorkersAiClient(IHttpClientFactory http, IOptions<AiOptions> options, IClock clock, ILogger<WorkersAiClient> logger)
    : IReportTriage, IVoiceTranscriber
{
    public const string HttpClientName = "workers-ai";
    public const int MaxAudioBytes = 1024 * 1024;
    public const int MaxSummaryLength = 120;

    public bool IsAvailable => options.Value.IsConfigured;

    public async Task<ReportTriage?> TriageAsync(string type, string? note, IReadOnlyList<NearbyReport> nearby, CancellationToken ct = default)
    {
        if (!IsAvailable) return null;
        var body = new
        {
            type,
            note = note ?? string.Empty,
            lang = "pl",
            nearby = nearby.Select(n => new { id = n.Id, type = n.Type, note = n.Note ?? string.Empty })
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("triage")) { Content = JsonContent.Create(body) };
        var json = await SendAsync(request, options.Value.TimeoutSeconds, "triage", ct);
        return json is { } j ? ParseTriage(j, nearby.Select(n => n.Id).ToHashSet(StringComparer.Ordinal), clock.UtcNow) : null;
    }

    public async Task<VoiceTranscript?> TranscribeAsync(byte[] audio, string contentType, CancellationToken ct = default)
    {
        if (!IsAvailable || audio.Length == 0 || audio.Length > MaxAudioBytes) return null;
        using var content = new ByteArrayContent(audio);
        content.Headers.ContentType = MediaTypeHeaderValue.TryParse(contentType, out var mt) ? mt : new MediaTypeHeaderValue("audio/ogg");
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("transcribe")) { Content = content };
        if (await SendAsync(request, options.Value.TranscribeTimeoutSeconds, "transcribe", ct) is not { } json) return null;

        var text = Clean(Str(json, "text"), 1000);
        if (string.IsNullOrWhiteSpace(text)) return null;
        var triage = json.TryGetProperty("triage", out var t) ? ParseTriage(t, new HashSet<string>(), clock.UtcNow) : null;
        return new VoiceTranscript(text, Clean(Str(json, "language"), 8), triage);
    }

    /// <summary>Validates and coerces the model's answer. Returns null when it is not an object.</summary>
    public static ReportTriage? ParseTriage(JsonElement j, IReadOnlySet<string> nearbyIds, DateTimeOffset now)
    {
        if (j.ValueKind != JsonValueKind.Object) return null;
        var severity = j.TryGetProperty("severity", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetDouble(out var sv)
            ? (int)Math.Clamp(Math.Round(sv), 1, 3) : 2;
        var confidence = j.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetDouble(out var cv)
            ? Math.Round(Math.Clamp(cv, 0, 1), 2) : 0;
        var duplicate = Str(j, "duplicateOf");
        return new ReportTriage(
            ReportTypeNames.Parse(Str(j, "suggestedType"))?.ToString(),
            severity,
            Clean(Str(j, "language"), 8),
            Clean(Str(j, "summaryPl"), MaxSummaryLength),
            Bool(j, "isAbuse") ?? false,
            // A missing flag counts as "may contain personal data", so the summary is never shared by accident.
            Bool(j, "containsPersonalData") ?? true,
            duplicate is not null && nearbyIds.Contains(duplicate) ? duplicate : null,
            confidence,
            now);
    }

    private string Url(string path) => $"{options.Value.BaseUrl.TrimEnd('/')}/{path}";

    private async Task<JsonElement?> SendAsync(HttpRequestMessage request, int timeoutSeconds, string what, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(options.Value.Key)) request.Headers.Add("X-Ai-Key", options.Value.Key);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 60)));
        try
        {
            using var response = await http.CreateClient(HttpClientName).SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Workers AI {What} answered {Status}", what, (int)response.StatusCode);
                return null;
            }

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cts.Token), cancellationToken: cts.Token);
            return doc.RootElement.Clone();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning("Workers AI {What} failed: {Error}", what, ex is OperationCanceledException ? "timeout" : ex.GetType().Name);
            return null;
        }
    }

    private static string? Str(JsonElement j, string name) =>
        j.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool? Bool(JsonElement j, string name) =>
        j.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = new string(value.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        return cleaned.Length == 0 ? null : cleaned.Length > max ? cleaned[..max].TrimEnd() : cleaned;
    }
}
