using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Domain.Mobility;

/// <summary>A disruption or detour notice from GTFS-Realtime ServiceAlerts.</summary>
public sealed record ServiceAlert(
    string Id,
    string Header,
    string? Description,
    string Cause,
    string Effect,
    IReadOnlyList<ActivePeriod> ActivePeriods,
    IReadOnlyList<string> RouteIds,
    IReadOnlyList<string> StopIds,
    string? Url,
    string FeedKey) : IEntity
{
    /// <summary>An alert with no active periods is active until removed from the feed.</summary>
    public bool IsActiveAt(DateTimeOffset moment) =>
        ActivePeriods.Count == 0 || ActivePeriods.Any(p => p.Contains(moment));
}

public sealed record ActivePeriod(DateTimeOffset? Start, DateTimeOffset? End)
{
    public bool Contains(DateTimeOffset moment) =>
        (Start is null || moment >= Start) && (End is null || moment <= End);
}
