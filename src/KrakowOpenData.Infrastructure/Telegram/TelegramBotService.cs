using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Safety;
using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.Safety;
using KrakowOpenData.Infrastructure.Ai;
using KrakowOpenData.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Infrastructure.Telegram;

/// <summary>
/// The bot's update loop (port of SafeWalk's bot.py): getUpdates long polling inside the API, private chats only. The offset is saved
/// after every update, so a crash replays at most one, and every handler is idempotent. Runs only when Telegram:Polling is on (the
/// default when a token is set): only one poller per token may run anywhere.
///
/// <para>Commands: <c>/start &lt;code&gt;</c> links the app that made the code to this chat; <c>/stop</c> unlinks every app.</para>
/// <para>Reporting (accessibility: for people who cannot type easily): a voice message is downloaded, transcribed by the Worker
/// (Whisper) and triaged; a text message is triaged. The bot replies with what it understood and the AI category and asks for the
/// location; the location creates an unverified report through <see cref="ReportService"/> as device "tg-" + sha256(chat id)[..16].
/// Voice is never stored: only the transcript, as the report's note (capped at 200 characters like every note).</para>
/// </summary>
public sealed class TelegramBotService(
    TelegramClient client,
    TelegramStore store,
    TelegramOutbox outbox,
    ReportService reports,
    ISafetyStore safetyStore,
    ISafetyEventSink events,
    IReportTriage triage,
    IVoiceTranscriber transcriber,
    VoiceQuota quota,
    IClock clock,
    IOptions<TelegramOptions> options,
    ILogger<TelegramBotService> logger) : BackgroundService
{
    public const int MaxVoiceSeconds = 60;
    private static readonly TimeSpan Backoff = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DraftLifetime = TimeSpan.FromMinutes(30);

    private sealed record Draft(string? Note, ReportType? Type, ReportTriage? Triage, DateTimeOffset At);

    private readonly ConcurrentDictionary<long, Draft> _drafts = new();

    public static string DeviceIdFor(long chatId) =>
        TelegramNotifier.BotDevicePrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(chatId.ToString(CultureInfo.InvariantCulture))))[..16].ToLowerInvariant();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.PollingEnabled)
        {
            if (options.Value.IsConfigured) logger.LogInformation("Telegram polling is off (Telegram:Polling=false); sending only.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await client.DeleteWebhookAsync(stoppingToken);
                break;
            }
            catch (TelegramException e)
            {
                logger.LogWarning("deleteWebhook failed: {Error}", e.Message);
                if (!await Delay(stoppingToken)) return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        var offset = store.Offset;
        while (!stoppingToken.IsCancellationRequested)
        {
            IReadOnlyList<JsonElement> updates;
            try
            {
                updates = await client.GetUpdatesAsync(offset, stoppingToken);
            }
            catch (TelegramException e)
            {
                // http_409: another process polls the same token (e.g. SafeWalk). Set Telegram__Polling=false on one of them.
                logger.LogWarning("getUpdates failed: {Error}", e.Message);
                if (!await Delay(stoppingToken)) return;
                continue;
            }
            catch (OperationCanceledException)
            {
                return;
            }

            foreach (var update in updates.OrderBy(u => u.TryGetProperty("update_id", out var id) ? id.GetInt64() : 0))
            {
                try
                {
                    await HandleUpdateAsync(update, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning("Bot update failed: {Error}", ex.GetType().Name);
                }

                if (update.TryGetProperty("update_id", out var uid))
                {
                    offset = uid.GetInt64() + 1;
                    store.SaveOffset(offset.Value);
                }
            }
        }
    }

    private static async Task<bool> Delay(CancellationToken ct)
    {
        try
        {
            await Task.Delay(Backoff, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Handles one update. Public for tests.</summary>
    public async Task HandleUpdateAsync(JsonElement update, CancellationToken ct = default)
    {
        if (update.TryGetProperty("message", out var msg))
        {
            if (!IsPrivate(msg) || IsBot(msg)) return;
            var chatId = msg.GetProperty("chat").GetProperty("id").GetInt64();
            var text = msg.TryGetProperty("text", out var t) ? (t.GetString() ?? string.Empty).Trim() : string.Empty;

            if (text.StartsWith('/'))
            {
                var space = text.IndexOf(' ');
                var name = (space < 0 ? text[1..] : text[1..space]).Split('@')[0].ToLowerInvariant();
                var arg = space < 0 ? string.Empty : text[(space + 1)..].Trim();
                switch (name)
                {
                    case "start": await OnStartAsync(chatId, arg, ct); return;
                    case "stop": await OnStopAsync(chatId, ct); return;
                    default: await ReplyAsync(chatId, TelegramMessages.About(transcriber.IsAvailable), ct); return;
                }
            }

            if (msg.TryGetProperty("voice", out var voice) || msg.TryGetProperty("audio", out voice))
            {
                await OnVoiceAsync(chatId, voice, ct);
                return;
            }

            if (msg.TryGetProperty("location", out var location))
            {
                await OnLocationAsync(chatId, location.GetProperty("latitude").GetDouble(), location.GetProperty("longitude").GetDouble(), ct);
                return;
            }

            if (text.Length >= 3)
            {
                await OnTextAsync(chatId, text, ct);
                return;
            }

            await ReplyAsync(chatId, TelegramMessages.About(transcriber.IsAvailable), ct);
        }
        else if (update.TryGetProperty("callback_query", out var q))
        {
            if (!q.TryGetProperty("message", out var qm) || !IsPrivate(qm) || IsBot(q)) return;
            var chatId = qm.GetProperty("chat").GetProperty("id").GetInt64();
            var data = q.TryGetProperty("data", out var d) ? d.GetString() ?? string.Empty : string.Empty;
            var id = q.GetProperty("id").GetString() ?? string.Empty;
            if (data.StartsWith("cat:", StringComparison.Ordinal) && ReportTypeNames.Parse(data[4..]) is { } type)
            {
                var draft = _drafts.GetValueOrDefault(chatId) ?? new Draft(null, null, null, clock.UtcNow);
                _drafts[chatId] = draft with { Type = type, At = clock.UtcNow };
                await AnswerAsync(id, "OK", ct);
                await ReplyAsync(chatId, TelegramMessages.AskLocation(type), ct);
            }
            else
            {
                await AnswerAsync(id, "Niedostępne", ct);
            }
        }
        else if (update.TryGetProperty("my_chat_member", out var member))
        {
            if (!IsPrivate(member)) return;
            var status = member.TryGetProperty("new_chat_member", out var n) && n.TryGetProperty("status", out var s) ? s.GetString() : null;
            if (status == "kicked") store.MarkNotReceiving(member.GetProperty("chat").GetProperty("id").GetInt64(), clock.UtcNow);
        }
    }

    // ── Linking ──────────────────────────────────────────────────────────────
    private async Task OnStartAsync(long chatId, string code, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(code))
        {
            await ReplyAsync(chatId, TelegramMessages.About(transcriber.IsAvailable), ct);
            return;
        }

        var (result, _) = store.AcceptCode(code, chatId, clock.UtcNow);
        switch (result)
        {
            case TelegramStore.AcceptResult.Ok:
                outbox.Enqueue(chatId, TelegramMessages.Kinds.Linked, TelegramMessages.Linked()); // as in SafeWalk: through the outbox
                break;
            case TelegramStore.AcceptResult.Used: await ReplyAsync(chatId, TelegramMessages.LinkUsed(), ct); break;
            case TelegramStore.AcceptResult.Expired: await ReplyAsync(chatId, TelegramMessages.LinkExpired(), ct); break;
            default: await ReplyAsync(chatId, TelegramMessages.LinkUnknown(), ct); break;
        }
    }

    private async Task OnStopAsync(long chatId, CancellationToken ct)
    {
        _drafts.TryRemove(chatId, out _);
        await ReplyAsync(chatId, store.RemoveChat(chatId) > 0 ? TelegramMessages.Unlinked() : TelegramMessages.NothingToStop(), ct);
    }

    // ── Reporting ────────────────────────────────────────────────────────────
    private async Task OnVoiceAsync(long chatId, JsonElement voice, CancellationToken ct)
    {
        if (!transcriber.IsAvailable)
        {
            await ReplyAsync(chatId, TelegramMessages.VoiceUnavailable(), ct);
            return;
        }

        if (voice.TryGetProperty("duration", out var dur) && dur.TryGetInt32(out var seconds) && seconds > MaxVoiceSeconds)
        {
            await ReplyAsync(chatId, TelegramMessages.VoiceTooLong(), ct);
            return;
        }

        // Same per-device limit as voice notes from the app, so one chat cannot use up the Workers AI free tier.
        if (!quota.TryTake(DeviceIdFor(chatId)))
        {
            await ReplyAsync(chatId, TelegramMessages.VoiceLimited(), ct);
            return;
        }

        var fileId = voice.TryGetProperty("file_id", out var f) ? f.GetString() : null;
        var mime = voice.TryGetProperty("mime_type", out var m) ? m.GetString() ?? "audio/ogg" : "audio/ogg";
        VoiceTranscript? transcript = null;
        if (fileId is not null)
        {
            try
            {
                // Held in memory only for the transcription; never written anywhere.
                var audio = await client.DownloadFileAsync(fileId, WorkersAiClient.MaxAudioBytes, ct);
                transcript = await transcriber.TranscribeAsync(audio, mime, ct);
            }
            catch (TelegramException e) when (e.Message == "too_large")
            {
                await ReplyAsync(chatId, TelegramMessages.VoiceTooLong(), ct);
                return;
            }
            catch (TelegramException e)
            {
                logger.LogWarning("Voice download failed: {Error}", e.Message);
            }
        }

        if (transcript is null)
        {
            await ReplyAsync(chatId, TelegramMessages.VoiceFailed(), ct);
            return;
        }

        await StartDraftAsync(chatId, transcript.Text, transcript.Triage, heard: true, ct);
    }

    private async Task OnTextAsync(long chatId, string text, CancellationToken ct)
    {
        // Over the quota the report still works: the resident just picks the category without an AI suggestion.
        var suggestion = triage.IsAvailable && quota.TryTakeTriage(DeviceIdFor(chatId)) ? await triage.TriageAsync("Unknown", text, [], ct) : null;
        await StartDraftAsync(chatId, text, suggestion, heard: false, ct);
    }

    private async Task StartDraftAsync(long chatId, string text, ReportTriage? suggestion, bool heard, CancellationToken ct)
    {
        var type = suggestion is { IsAbuse: false } ? ReportTypeNames.Parse(suggestion.SuggestedType) : null;
        _drafts[chatId] = new Draft(text, type, suggestion, clock.UtcNow);
        if (type is { } t)
        {
            await ReplyAsync(chatId, heard ? TelegramMessages.Transcript(text, t) : TelegramMessages.TextReceived(t), ct);
            return;
        }

        if (heard) await ReplyAsync(chatId, TelegramMessages.Heard(text), ct);
        await ReplyAsync(chatId, TelegramMessages.ChooseCategory(), ct);
    }

    private async Task OnLocationAsync(long chatId, double lat, double lon, CancellationToken ct)
    {
        if (!_drafts.TryGetValue(chatId, out var draft) || clock.UtcNow - draft.At > DraftLifetime)
        {
            _drafts.TryRemove(chatId, out _);
            await ReplyAsync(chatId, TelegramMessages.NothingPending(), ct);
            return;
        }

        if (draft.Type is not { } type)
        {
            await ReplyAsync(chatId, TelegramMessages.ChooseCategory(), ct);
            return;
        }

        var device = DeviceIdFor(chatId);
        ReportDto created;
        try
        {
            created = await reports.CreateAsync(new CreateReportRequest(type.ToString(), lat, lon, draft.Note, device), ct);
        }
        catch (SafetyValidationException e)
        {
            await ReplyAsync(chatId, e.Field == "lat,lon" ? TelegramMessages.OutsideArea() : TelegramMessages.ReportFailed(), ct);
            return;
        }
        catch (SafetyRateLimitException)
        {
            await ReplyAsync(chatId, TelegramMessages.RateLimited(), ct);
            return;
        }

        _drafts.TryRemove(chatId, out _);
        var isNew = created.Supporters == 1 && created.CreatedAt == created.LastActivityAt;
        if (isNew && draft.Triage is { } ai && await safetyStore.GetReportAsync(created.Id, ct) is { Triage: null } saved)
            await safetyStore.SaveReportAsync(saved with { Triage = ai }, ct); // already triaged: the pipeline will not ask again

        // Status changes of this report (verified / resolved) come back to this chat.
        if (store.LinkOf(device) is not { Status: LinkStatus.Connected })
            store.UpsertLink(new TelegramLink(device, chatId, null, clock.UtcNow, LinkStatus.Connected, clock.UtcNow));

        events.ReportFiled(created, device);
        var report = await safetyStore.GetReportAsync(created.Id, ct);
        if (report is not null) await ReplyAsync(chatId, TelegramMessages.BotReportCreated(report, !isNew), ct);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private async Task ReplyAsync(long chatId, TelegramMessages.Message message, CancellationToken ct)
    {
        var result = await client.SendMessageAsync(chatId.ToString(CultureInfo.InvariantCulture), message.Text, message.Markup, ct);
        if (!result.Ok) logger.LogWarning("Bot reply not sent: {Error}", result.Error);
        if (result.Gone) store.MarkNotReceiving(chatId, clock.UtcNow);
    }

    private async Task AnswerAsync(string callbackId, string text, CancellationToken ct)
    {
        try
        {
            await client.AnswerCallbackAsync(callbackId, text, ct);
        }
        catch (TelegramException e)
        {
            logger.LogInformation("answerCallbackQuery failed: {Error}", e.Message);
        }
    }

    private static bool IsPrivate(JsonElement container) =>
        container.TryGetProperty("chat", out var chat) && chat.TryGetProperty("type", out var type) && type.GetString() == "private";

    private static bool IsBot(JsonElement container) =>
        container.TryGetProperty("from", out var from) && from.TryGetProperty("is_bot", out var b) && b.ValueKind == JsonValueKind.True;
}
