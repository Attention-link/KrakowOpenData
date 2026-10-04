using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Safety;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Safety;
using KrakowOpenData.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Infrastructure.Telegram;

/// <summary>
/// Decides who hears about a safety event and queues the messages in the <see cref="TelegramOutbox"/>:
/// <list type="bullet">
/// <item>a new report: the staff chat gets a digest (if Telegram:StaffChatId is set) and the resident a "received" confirmation;</item>
/// <item>a repeat merged into an open report: the resident who sent it gets a thank-you;</item>
/// <item>a planner verifies or resolves a report: everyone who reported or confirmed it and has linked Telegram;</item>
/// <item>a planner alert: every linked resident whose last known position (the app asked for alerts in the last 15 min) or
/// "my area" (saved when linking) is inside the alert's circle.</item>
/// </list>
/// Reports filed through the bot itself ("tg-" devices) are confirmed by the bot directly, so no second confirmation is queued.
/// </summary>
public sealed class TelegramNotifier(
    TelegramStore store, TelegramOutbox outbox, PresenceTracker presence, IClock clock, IOptions<TelegramOptions> options)
{
    public const string BotDevicePrefix = ReportService.BotDevicePrefix;

    public bool Enabled => options.Value.IsConfigured;

    public void ReportCreated(CitizenReport report)
    {
        if (!Enabled) return;
        if (!string.IsNullOrWhiteSpace(options.Value.StaffChatId))
            outbox.Enqueue(options.Value.StaffChatId, TelegramMessages.Kinds.Staff, TelegramMessages.StaffDigest(report, options.Value.PublicBaseUrl));
        if (report.DeviceIds.Count > 0 && ConnectedChat(report.DeviceIds[0], includeBot: false) is { } chat)
            outbox.Enqueue(chat, TelegramMessages.Kinds.Received, TelegramMessages.Received(report));
    }

    public void ReportMerged(CitizenReport report, string? deviceId)
    {
        if (!Enabled || deviceId is null) return;
        if (ConnectedChat(deviceId, includeBot: false) is { } chat)
            outbox.Enqueue(chat, TelegramMessages.Kinds.Merged, TelegramMessages.Merged(report));
    }

    public void StatusChanged(CitizenReport report, SafetyEventKind kind)
    {
        if (!Enabled) return;
        var message = kind == SafetyEventKind.ReportVerified ? TelegramMessages.Verified(report) : TelegramMessages.Resolved(report);
        var messageKind = kind == SafetyEventKind.ReportVerified ? TelegramMessages.Kinds.Verified : TelegramMessages.Kinds.Resolved;
        foreach (var chat in report.DeviceIds.Distinct().Select(d => ConnectedChat(d, includeBot: true)).OfType<long>().Distinct())
            outbox.Enqueue(chat, messageKind, message);
    }

    public void AlertCreated(PlannerAlert alert)
    {
        if (!Enabled || !alert.IsActiveAt(clock.UtcNow)) return;
        var message = TelegramMessages.Alert(alert, options.Value.PublicBaseUrl);
        var chats = store.ConnectedLinks()
            .Where(l => Covers(alert, presence.LastSeen(l.DeviceId)) || Covers(alert, l.Area))
            .Select(l => l.ChatId)
            .Distinct();
        foreach (var chat in chats) outbox.Enqueue(chat, TelegramMessages.Kinds.Alert, message);
    }

    private static bool Covers(PlannerAlert alert, GeoPoint? point) => point is { } p && alert.Covers(p);

    private long? ConnectedChat(string deviceId, bool includeBot) =>
        !includeBot && deviceId.StartsWith(BotDevicePrefix, StringComparison.Ordinal)
            ? null
            : store.LinkOf(deviceId) is { Status: LinkStatus.Connected } link ? link.ChatId : null;
}
