using System.Collections.Concurrent;
using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Safety;

namespace KrakowOpenData.Application.Safety;

/// <summary>
/// Remembers, in memory only, where each anonymous device last asked for alerts, so a planner can see roughly how many
/// phones an alert will reach. It stores a random id, a coordinate and a time, nothing else; entries expire after
/// <see cref="Window"/> and are never written to disk.
/// </summary>
public sealed class PresenceTracker(IClock clock)
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    public const int MaxEntries = 50_000;

    private readonly ConcurrentDictionary<string, (GeoPoint Point, DateTimeOffset Seen)> _devices = new();

    public void Touch(string? deviceId, GeoPoint point)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 64) return;
        if (_devices.Count >= MaxEntries) Prune();
        // Still full of live entries (e.g. a flood of random ids): known phones are refreshed, new ones are not added.
        if (_devices.Count >= MaxEntries && !_devices.ContainsKey(deviceId)) return;
        _devices[deviceId] = (point, clock.UtcNow);
    }

    /// <summary>Devices seen inside the circle in the last 15 minutes.</summary>
    public int CountNear(GeoPoint center, double radiusMeters)
    {
        var since = clock.UtcNow - Window;
        return _devices.Values.Count(d => d.Seen >= since && GridSpec.Distance(center, d.Point) <= radiusMeters);
    }

    public int CountActive()
    {
        var since = clock.UtcNow - Window;
        return _devices.Values.Count(d => d.Seen >= since);
    }

    /// <summary>Where the device last asked for alerts, if that was within the last 15 minutes.</summary>
    public GeoPoint? LastSeen(string deviceId) =>
        _devices.TryGetValue(deviceId, out var d) && d.Seen >= clock.UtcNow - Window ? d.Point : null;

    private void Prune()
    {
        var since = clock.UtcNow - Window;
        foreach (var (key, value) in _devices)
            if (value.Seen < since) _devices.TryRemove(key, out _);
    }
}

/// <summary>
/// Planner alerts that target the people in an area. An alert is a circle (centre + radius), a severity, a message
/// and an expiry. Residents' apps ask "which alerts cover my position?" every minute while online; the server keeps no
/// recipient list, only the anonymous presence count from <see cref="PresenceTracker"/>.
/// </summary>
public sealed class AlertService(ISafetyStore store, PresenceTracker presence, IClock clock)
{
    public const double MinRadiusMeters = 100, MaxRadiusMeters = 5000;
    public const int MinMinutes = 5, MaxMinutes = 24 * 60;

    public async Task<PlannerAlertDto> CreateAsync(CreateAlertRequest request, CancellationToken ct = default)
    {
        if (!SafetyEnum.TryParseName<AlertSeverity>(request.Severity, out var severity))
            throw new SafetyValidationException("severity", $"Use one of: {string.Join(", ", Enum.GetNames<AlertSeverity>())}.");

        ScoreLayer? layer = null;
        if (!string.IsNullOrWhiteSpace(request.Layer))
        {
            if (!SafetyEnum.TryParseName<ScoreLayer>(request.Layer, out var parsed))
                throw new SafetyValidationException("layer", "Use Heat or Safety, or leave empty.");
            layer = parsed;
        }

        var title = (request.Title ?? string.Empty).Trim();
        var message = (request.Message ?? string.Empty).Trim();
        if (title.Length is < 3 or > 80) throw new SafetyValidationException("title", "The title must be 3–80 characters.");
        if (message.Length is < 3 or > 500) throw new SafetyValidationException("message", "The message must be 3–500 characters.");

        var center = new GeoPoint(request.Latitude, request.Longitude);
        if (!GridSpec.IsInArea(center)) throw new SafetyValidationException("lat,lon", "The centre must be in or around Kraków.");
        if (request.RadiusMeters is < MinRadiusMeters or > MaxRadiusMeters)
            throw new SafetyValidationException("radiusMeters", $"The radius must be {MinRadiusMeters}–{MaxRadiusMeters} m.");
        if (request.DurationMinutes is < MinMinutes or > MaxMinutes)
            throw new SafetyValidationException("durationMinutes", $"The duration must be {MinMinutes}–{MaxMinutes} minutes.");

        var translations = (request.Translations ?? new Dictionary<string, string>())
            .Where(kv => kv.Key is "pl" or "en" or "uk" && !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Trim().Length > 500 ? kv.Value.Trim()[..500] : kv.Value.Trim());

        var cellId = GridSpec.CleanCellId(request.CellId);
        var now = clock.UtcNow;
        var alert = new PlannerAlert(
            $"alt-{Guid.NewGuid():N}"[..16], layer, severity, title, message, translations, center, request.RadiusMeters,
            now, now.AddMinutes(request.DurationMinutes), cellId, AlertStatus.Active);
        await store.SaveAlertAsync(alert, ct);
        return alert.ToDto(presence.CountNear(center, alert.RadiusMeters));
    }

    public async Task<PlannerAlertDto?> CancelAsync(string id, CancellationToken ct = default)
    {
        if (await store.GetAlertAsync(id, ct) is not { } alert) return null;
        var cancelled = alert with { Status = AlertStatus.Cancelled };
        await store.SaveAlertAsync(cancelled, ct);
        return cancelled.ToDto(null);
    }

    /// <summary>Planner list: active first, then recent history.</summary>
    public async Task<IReadOnlyList<PlannerAlertDto>> ListAsync(bool includeInactive, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        return (await store.ListAlertsAsync(ct))
            .Where(a => includeInactive || a.IsActiveAt(now))
            .OrderByDescending(a => a.IsActiveAt(now)).ThenByDescending(a => a.CreatedAt)
            .Take(200)
            .Select(a => a.ToDto(a.IsActiveAt(now) ? presence.CountNear(a.Center, a.RadiusMeters) : null))
            .ToList();
    }

    /// <summary>Active alerts whose circle contains <paramref name="point"/>. Also records the anonymous presence.</summary>
    public async Task<IReadOnlyList<PlannerAlertDto>> ForPointAsync(GeoPoint point, string? deviceId, CancellationToken ct = default)
    {
        presence.Touch(deviceId, point);
        var now = clock.UtcNow;
        return (await store.ListAlertsAsync(ct))
            .Where(a => a.IsActiveAt(now) && a.Covers(point))
            .OrderByDescending(a => a.Severity).ThenByDescending(a => a.CreatedAt)
            .Select(a => a.ToDto(null))
            .ToList();
    }

    public int ActiveCount(IEnumerable<PlannerAlert> alerts) => alerts.Count(a => a.IsActiveAt(clock.UtcNow));
}
