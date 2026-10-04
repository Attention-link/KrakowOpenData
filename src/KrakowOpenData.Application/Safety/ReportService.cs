using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Safety;

namespace KrakowOpenData.Application.Safety;

/// <summary>
/// Citizen reports: create, confirm ("still true"), verify and resolve. How a report moves a score is in <see cref="ReportRules"/>.
///
/// <para><b>Abuse protection</b> (the app is anonymous): a device may file at most 5 reports an hour; a new report of the
/// same type in the same grid cell within 24 h of an open one is merged into it as a confirmation instead of creating a
/// duplicate; a single unconfirmed report counts only a quarter; text notes are plain text, capped at 200 characters
/// and only visible to planners.</para>
/// </summary>
public sealed class ReportService(ISafetyStore store, IClock clock)
{
    public const int MaxReportsPerDevicePerHour = 5;
    public const int MaxNoteLength = 200;

    public static IReadOnlyList<ReportTypeDto> Types { get; } = ReportRules.All
        .Select(r => new ReportTypeDto(r.Type.ToString(), r.Rule.Layer.ToString(), r.Rule.Weight, r.Rule.HalfLifeHours, r.Rule.Label))
        .ToList();

    public async Task<ReportDto> CreateAsync(CreateReportRequest request, CancellationToken ct = default)
    {
        if (!SafetyEnum.TryParseName<ReportType>(request.Type, out var type))
            throw new SafetyValidationException("type", $"Use one of: {string.Join(", ", Enum.GetNames<ReportType>())}.");

        var point = new GeoPoint(request.Latitude, request.Longitude);
        if (!GridSpec.IsInArea(point))
            throw new SafetyValidationException("lat,lon", "The location must be in or around Kraków.");

        var device = CleanDevice(request.DeviceId);
        var now = clock.UtcNow;
        var all = await store.ListReportsAsync(ct);

        var recentByDevice = all.Count(r => r.CreatedAt > now.AddHours(-1) && r.DeviceIds.Count > 0 && r.DeviceIds[0] == device);
        if (recentByDevice >= MaxReportsPerDevicePerHour)
            throw new SafetyRateLimitException($"You can send {MaxReportsPerDevicePerHour} reports per hour. Please try again later.");

        // Same type, same cell, still fresh: add this device as a supporter instead of a duplicate.
        var cell = GridSpec.IdOf(point);
        var existing = all.FirstOrDefault(r =>
            r.Status == ReportStatus.Open && r.Type == type && r.LastActivityAt > now.AddHours(-24) && GridSpec.IdOf(r.Location) == cell);
        if (existing is not null)
        {
            var merged = WithSupporter(existing, device, now);
            await store.SaveReportAsync(merged, ct);
            return merged.ToDto(includeNote: false);
        }

        var report = new CitizenReport(
            $"rep-{Guid.NewGuid():N}"[..16], type, point, CleanNote(request.Note), now, now, [device], false, ReportStatus.Open, null, null);
        await store.SaveReportAsync(report, ct);
        return report.ToDto(includeNote: false);
    }

    public async Task<ReportDto?> ConfirmAsync(string id, string deviceId, CancellationToken ct = default)
    {
        var device = CleanDevice(deviceId);
        if (await store.GetReportAsync(id, ct) is not { Status: ReportStatus.Open } report) return null;
        var updated = WithSupporter(report, device, clock.UtcNow);
        await store.SaveReportAsync(updated, ct);
        return updated.ToDto(includeNote: false);
    }

    /// <summary>Planner: marks a report as checked, so it counts in full even if only one resident reported it.</summary>
    public async Task<ReportDto?> VerifyAsync(string id, CancellationToken ct = default)
    {
        if (await store.GetReportAsync(id, ct) is not { } report) return null;
        var updated = report with { VerifiedByPlanner = true, LastActivityAt = clock.UtcNow };
        await store.SaveReportAsync(updated, ct);
        return updated.ToDto(includeNote: true);
    }

    /// <summary>Planner: the problem was fixed or dismissed; the report stops affecting the score.</summary>
    public async Task<ReportDto?> ResolveAsync(string id, string? note, CancellationToken ct = default)
    {
        if (await store.GetReportAsync(id, ct) is not { } report) return null;
        var updated = report with { Status = ReportStatus.Resolved, ResolvedAt = clock.UtcNow, ResolutionNote = CleanNote(note) };
        await store.SaveReportAsync(updated, ct);
        return updated.ToDto(includeNote: true);
    }

    /// <summary>Open reports within a circle (or citywide). Notes are included for planners only.</summary>
    public Task<IReadOnlyList<ReportDto>> ListAsync(
        GeoPoint? near, double radiusMeters, bool includeResolved, bool includeNotes, int limit, CancellationToken ct = default) =>
        ListAsync(new ReportFilter(near, radiusMeters, includeResolved ? ReportStatusFilter.All : ReportStatusFilter.Open), includeNotes, limit, ct);

    /// <summary>
    /// Reports that match <paramref name="filter"/>, newest activity first, at most <paramref name="limit"/> (1–1000). Every filter is applied
    /// BEFORE the limit, so a filtered list is never cut short by reports that would have been filtered out anyway.
    /// </summary>
    public async Task<IReadOnlyList<ReportDto>> ListAsync(ReportFilter filter, bool includeNotes, int limit, CancellationToken ct = default)
    {
        var cutoff = clock.UtcNow - ReportRules.ListAge;
        var text = filter.Text?.Trim();
        return (await store.ListReportsAsync(ct))
            .Where(r => r.LastActivityAt >= cutoff && ReportRules.IsKnown(r.Type))
            .Where(r => filter.Status switch
            {
                ReportStatusFilter.Open => r.Status == ReportStatus.Open,
                ReportStatusFilter.Resolved => r.Status == ReportStatus.Resolved,
                _ => true
            })
            .Where(r => filter.Layer is not { } layer || ReportRules.For(r.Type).Layer == layer)
            .Where(r => filter.Type is not { } type || r.Type == type)
            .Where(r => filter.Verified is not { } verified || r.VerifiedByPlanner == verified)
            .Where(r => filter.Near is not { } p || GridSpec.Distance(p, r.Location) <= filter.RadiusMeters)
            // Free text only looks at what the caller may read: notes are visible to planners alone.
            .Where(r => string.IsNullOrEmpty(text) || Matches(r, text, includeNotes))
            .OrderByDescending(r => r.LastActivityAt)
            .Take(Math.Clamp(limit, 1, 1000))
            .Select(r => r.ToDto(includeNotes))
            .ToList();
    }

    private static bool Matches(CitizenReport r, string text, bool includeNotes) =>
        Contains(ReportRules.For(r.Type).Label, text) || Contains(r.Type.ToString(), text) || Contains(GridSpec.IdOf(r.Location), text) ||
        (includeNotes && (Contains(r.Note, text) || Contains(r.ResolutionNote, text)));

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static CitizenReport WithSupporter(CitizenReport report, string device, DateTimeOffset now) =>
        report with
        {
            DeviceIds = report.DeviceIds.Contains(device) ? report.DeviceIds : [.. report.DeviceIds, device],
            LastActivityAt = now
        };

    /// <summary>
    /// The device id is a random value made by the app. It is only used to count independent supporters and to
    /// rate-limit; it is shortened and never linked to a person.
    /// </summary>
    private static string CleanDevice(string? deviceId)
    {
        var value = deviceId?.Trim() ?? string.Empty;
        if (value.Length is < 8 or > 64 || value.Any(c => !char.IsLetterOrDigit(c) && c != '-'))
            throw new SafetyValidationException("deviceId", "deviceId must be 8–64 letters, digits or dashes.");
        return value;
    }

    private static string? CleanNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note)) return null;
        var cleaned = new string(note.Where(c => !char.IsControl(c) || c == '\n').ToArray()).Trim();
        return cleaned.Length > MaxNoteLength ? cleaned[..MaxNoteLength] : cleaned;
    }
}

public enum ReportStatusFilter
{
    Open,
    Resolved,
    All
}

/// <summary>What a report list is filtered by. Everything is optional; null means "do not filter on this".</summary>
public sealed record ReportFilter(
    GeoPoint? Near = null,
    double RadiusMeters = 1000,
    ReportStatusFilter Status = ReportStatusFilter.Open,
    ScoreLayer? Layer = null,
    ReportType? Type = null,
    bool? Verified = null,
    string? Text = null);
