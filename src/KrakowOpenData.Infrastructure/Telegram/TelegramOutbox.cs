using System.Globalization;
using System.Text.Json;
using KrakowOpenData.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KrakowOpenData.Infrastructure.Telegram;

/// <summary>
/// Every message the API sends on its own (alerts, report status, staff digest, "linked") is a row in the outbox, sent by this one loop
/// (port of SafeWalk's alerts.py). A failed send is retried after 5, 10, 15 and 30 s (or Telegram's retry_after), then given up.
/// A permanent failure is not retried; when the chat blocked the bot or does not exist, its links are marked "not receiving".
/// Without a bot token nothing is queued at all.
/// </summary>
public sealed class TelegramOutbox(TelegramStore store, TelegramClient client, IClock clock, ILogger<TelegramOutbox> logger) : BackgroundService
{
    public static readonly int[] RetrySeconds = [5, 10, 15, 30];
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan HousekeepingEvery = TimeSpan.FromHours(1);

    private readonly SemaphoreSlim _wake = new(0, 1);
    private DateTimeOffset _lastHousekeeping = DateTimeOffset.MinValue;

    public void Enqueue(string chatId, string kind, TelegramMessages.Message message)
    {
        if (!client.Enabled || string.IsNullOrWhiteSpace(chatId)) return;
        store.Enqueue(chatId.Trim(), kind, message.Text, message.Markup is null ? null : JsonSerializer.Serialize(message.Markup), clock.UtcNow);
        Wake();
    }

    public void Enqueue(long chatId, string kind, TelegramMessages.Message message) =>
        Enqueue(chatId.ToString(CultureInfo.InvariantCulture), kind, message);

    /// <summary>Ask the loop to send now instead of at the next tick.</summary>
    public void Wake()
    {
        try { _wake.Release(); }
        catch (SemaphoreFullException) { /* already woken */ }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!client.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (clock.UtcNow - _lastHousekeeping >= HousekeepingEvery)
                {
                    _lastHousekeeping = clock.UtcNow;
                    store.Housekeeping(clock.UtcNow);
                }

                await SendDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Telegram outbox tick failed: {Error}", ex.GetType().Name);
            }

            try
            {
                await _wake.WaitAsync(Tick, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Sends every due row once and records the outcome. Public for tests.</summary>
    public async Task SendDueAsync(CancellationToken ct = default)
    {
        foreach (var m in store.Due(clock.UtcNow))
        {
            if (!store.IsPending(m.Id)) continue; // dropped meanwhile (the chat blocked the bot, or the resident unlinked)
            object? markup = m.ReplyMarkupJson is null ? null : JsonSerializer.Deserialize<JsonElement>(m.ReplyMarkupJson);
            var result = await client.SendMessageAsync(m.ChatId, m.Text, markup, ct);
            if (!result.Ok) logger.LogWarning("Telegram message {Id} ({Kind}) not sent: {Error}", m.Id, m.Kind, result.Error);
            Apply(m, result);
        }
    }

    internal void Apply(OutboxMessage m, TelegramSendResult result)
    {
        var now = clock.UtcNow;
        var attempts = m.Attempts + 1;
        if (result.Ok)
        {
            store.Update(m with { State = OutboxState.Sent, Attempts = attempts, LastError = null, EndedAt = now });
            return;
        }

        if (result.Permanent || attempts > RetrySeconds.Length)
        {
            store.Update(m with { State = OutboxState.Failed, Attempts = attempts, LastError = result.Error, EndedAt = now });
            if (result.Gone && long.TryParse(m.ChatId, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var chat))
                store.MarkNotReceiving(chat, now);
            return;
        }

        var wait = result.RetryAfter is > 0 and <= 120 ? result.RetryAfter.Value : RetrySeconds[attempts - 1];
        store.Update(m with { Attempts = attempts, LastError = result.Error, NextAt = now.AddSeconds(wait) });
    }
}
