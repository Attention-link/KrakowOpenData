using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Domain.Safety;

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}

public enum AlertStatus
{
    Active,
    Cancelled
}

/// <summary>
/// A message a planner sends to everyone currently inside a circle. Apps poll for alerts covering their
/// position; the server keeps no list of recipients.
/// </summary>
public sealed record PlannerAlert(
    string Id,
    ScoreLayer? Layer,
    AlertSeverity Severity,
    string Title,
    string Message,
    IReadOnlyDictionary<string, string> MessageTranslations,
    GeoPoint Center,
    double RadiusMeters,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string? CellId,
    AlertStatus Status) : IEntity
{
    public bool IsActiveAt(DateTimeOffset moment) => Status == AlertStatus.Active && moment < ExpiresAt;

    public bool Covers(GeoPoint point) => Center.DistanceTo(point) <= RadiusMeters;
}
