using System.Security.Cryptography;
using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Safety;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Infrastructure.Telegram;

/// <summary>
/// Links a resident's app to their Telegram chat (port of SafeWalk's invitations): the app asks for a one-time code (valid 15 minutes,
/// stored only as a SHA-256), opens https://t.me/&lt;bot&gt;?start=&lt;code&gt;, and the bot's /start links the chat. The optional
/// "my area" point is rounded to about 100 m before it is stored.
/// </summary>
public sealed class TelegramLinkService(TelegramStore store, TelegramOutbox outbox, IClock clock, IOptions<TelegramOptions> options)
{
    public sealed record LinkCode(string Link, DateTimeOffset ExpiresAt);

    public bool Available => options.Value.CanLink;

    public bool IsLinked(string deviceId) => store.LinkOf(CleanDevice(deviceId)) is { Status: LinkStatus.Connected };

    public LinkCode? CreateCode(string deviceId, double? latitude, double? longitude)
    {
        var device = CleanDevice(deviceId);   // bad input is a 400 whether or not Telegram is configured
        if (!Available) return null;
        GeoPoint? area = null;
        if (latitude is { } lat && longitude is { } lon && GridSpec.IsInArea(new GeoPoint(lat, lon)))
            area = new GeoPoint(Math.Round(lat, 3), Math.Round(lon, 3));

        var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_'); // 32 characters: A–Z a–z 0–9 - _
        var now = clock.UtcNow;
        var expires = now.AddMinutes(Math.Clamp(options.Value.LinkTtlMinutes, 1, 60));
        store.AddCode(new TelegramLinkCode(TelegramStore.Hash(code), device, area, now, expires, null));
        return new LinkCode($"https://t.me/{options.Value.BotUsername.Trim().TrimStart('@')}?start={code}", expires);
    }

    /// <summary>Unlinks the app; the chat is told once. Returns false when it was not linked.</summary>
    public bool Unlink(string deviceId)
    {
        if (store.RemoveLink(CleanDevice(deviceId)) is not { } link) return false;
        if (link.Status == LinkStatus.Connected) outbox.Enqueue(link.ChatId, TelegramMessages.Kinds.Unlinked, TelegramMessages.Unlinked());
        return true;
    }

    /// <summary>Same rule as report device ids: 8–64 letters, digits or dashes, and never a bot id (only public endpoints call this).</summary>
    public static string CleanDevice(string? deviceId)
    {
        ReportService.RejectBotDevice(deviceId);
        var value = deviceId?.Trim() ?? string.Empty;
        if (value.Length is < 8 or > 64 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new SafetyValidationException("deviceId", "deviceId must be 8–64 letters, digits or dashes.");
        return value;
    }
}
