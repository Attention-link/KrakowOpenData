using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KrakowOpenData.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Infrastructure.Telegram;

/// <summary>Outcome of a send. <see cref="Error"/> is an error CLASS only (blocked, chat_not_found, bad_request, rate_limited, http_5xx, timeout, network).</summary>
public sealed record TelegramSendResult(
    bool Ok, long? MessageId = null, string? Error = null, bool Permanent = false, int? RetryAfter = null, bool Gone = false);

/// <summary>Raised by non-send calls. The message is an error class, never a URL (URLs contain the token).</summary>
public sealed class TelegramException(string errorClass) : Exception(errorClass);

/// <summary>
/// Thin Telegram Bot API client (port of SafeWalk's telegram.py). The token is part of every request URL, so neither URLs nor
/// HttpClient exceptions are ever logged or rethrown from here; callers only see error classes.
/// </summary>
public sealed class TelegramClient(IHttpClientFactory http, IOptions<TelegramOptions> options)
{
    public const string HttpClientName = "telegram";
    public const int PollSeconds = 25;
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);

    public bool Enabled => options.Value.IsConfigured;

    private string Url(string method) => $"{options.Value.ApiBaseUrl.TrimEnd('/')}/bot{options.Value.BotToken.Trim()}/{method}";

    /// <summary>Numeric ids go as numbers, "@channel" names as strings.</summary>
    internal static object ChatIdValue(string chatId) =>
        long.TryParse(chatId.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) ? n : chatId.Trim();

    public async Task<TelegramSendResult> SendMessageAsync(string chatId, string text, object? replyMarkup = null, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object>
        {
            ["chat_id"] = ChatIdValue(chatId),
            ["text"] = text,
            ["link_preview_options"] = new Dictionary<string, object> { ["is_disabled"] = true }
        };
        if (replyMarkup is not null) payload["reply_markup"] = replyMarkup;

        HttpResponseMessage response;
        try
        {
            response = await PostAsync("sendMessage", payload, CallTimeout, ct);
        }
        catch (TelegramException e)
        {
            return new TelegramSendResult(false, Error: e.Message);
        }

        using (response)
        {
            var body = await ReadJsonAsync(response, ct);
            if (response.StatusCode == HttpStatusCode.OK && IsOk(body))
            {
                long? id = body is { } b && b.TryGetProperty("result", out var r) && r.TryGetProperty("message_id", out var m) && m.TryGetInt64(out var mid) ? mid : null;
                return new TelegramSendResult(true, MessageId: id);
            }

            var description = body is { } d && d.TryGetProperty("description", out var desc) ? desc.ToString().ToLowerInvariant() : string.Empty;
            var status = (int)response.StatusCode;
            if (status == 403) return new TelegramSendResult(false, Error: "blocked", Permanent: true, Gone: true);
            if (status == 400 && description.Contains("chat not found")) return new TelegramSendResult(false, Error: "chat_not_found", Permanent: true, Gone: true);
            if (status == 429)
            {
                int? retryAfter = body is { } p && p.TryGetProperty("parameters", out var ps) && ps.TryGetProperty("retry_after", out var ra) && ra.TryGetInt32(out var sec) ? sec : null;
                return new TelegramSendResult(false, Error: "rate_limited", RetryAfter: retryAfter);
            }

            if (status >= 500) return new TelegramSendResult(false, Error: "http_5xx");
            return new TelegramSendResult(false, Error: "bad_request", Permanent: true);
        }
    }

    public async Task<IReadOnlyList<JsonElement>> GetUpdatesAsync(long? offset, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object>
        {
            ["timeout"] = PollSeconds,
            ["allowed_updates"] = new[] { "message", "callback_query", "my_chat_member" }
        };
        if (offset is { } o) payload["offset"] = o;
        var result = await CallAsync("getUpdates", payload, TimeSpan.FromSeconds(PollSeconds + 10), ct);
        return result is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray().Select(e => e.Clone()).ToList() : [];
    }

    public Task DeleteWebhookAsync(CancellationToken ct = default) =>
        CallAsync("deleteWebhook", new Dictionary<string, object> { ["drop_pending_updates"] = false }, CallTimeout, ct);

    public Task AnswerCallbackAsync(string callbackQueryId, string text, CancellationToken ct = default) =>
        CallAsync("answerCallbackQuery", new Dictionary<string, object> { ["callback_query_id"] = callbackQueryId, ["text"] = text }, CallTimeout, ct);

    /// <summary>Downloads a file the user sent (a voice message), at most <paramref name="maxBytes"/>. Throws TelegramException("too_large") when bigger.</summary>
    public async Task<byte[]> DownloadFileAsync(string fileId, int maxBytes, CancellationToken ct = default)
    {
        var file = await CallAsync("getFile", new Dictionary<string, object> { ["file_id"] = fileId }, CallTimeout, ct);
        if (file is not { } f || !f.TryGetProperty("file_path", out var pathEl) || pathEl.GetString() is not { Length: > 0 } path)
            throw new TelegramException("no_file");
        if (f.TryGetProperty("file_size", out var sizeEl) && sizeEl.TryGetInt64(out var size) && size > maxBytes)
            throw new TelegramException("too_large");

        var url = $"{options.Value.ApiBaseUrl.TrimEnd('/')}/file/bot{options.Value.BotToken.Trim()}/{path}";
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var response = await http.CreateClient(HttpClientName).GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode) throw new TelegramException($"http_{(int)response.StatusCode}");
            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, cts.Token)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > maxBytes) throw new TelegramException("too_large");
            }

            return buffer.ToArray();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TelegramException("timeout");
        }
        catch (HttpRequestException)
        {
            throw new TelegramException("network");
        }
    }

    /// <summary>Calls a method and returns its <c>result</c>; throws TelegramException(class) on any failure.</summary>
    private async Task<JsonElement?> CallAsync(string method, object payload, TimeSpan timeout, CancellationToken ct)
    {
        using var response = await PostAsync(method, payload, timeout, ct);
        var body = await ReadJsonAsync(response, ct);
        if (response.StatusCode != HttpStatusCode.OK || !IsOk(body)) throw new TelegramException($"http_{(int)response.StatusCode}");
        return body is { } b && b.TryGetProperty("result", out var r) ? r.Clone() : null;
    }

    private async Task<HttpResponseMessage> PostAsync(string method, object payload, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            var response = await http.CreateClient(HttpClientName).PostAsJsonAsync(Url(method), payload, cts.Token);
            await response.Content.LoadIntoBufferAsync();
            return response;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TelegramException("timeout");
        }
        catch (HttpRequestException)
        {
            throw new TelegramException("network");
        }
    }

    private static async Task<JsonElement?> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsOk(JsonElement? body) =>
        body is { ValueKind: JsonValueKind.Object } b && b.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
}
