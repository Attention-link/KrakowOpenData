using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Domain.Mobility;

/// <summary>A line from GTFS routes.txt, e.g. tram 52 or bus 179.</summary>
public sealed record TransitRoute(
    string Id,
    string ShortName,
    string? LongName,
    TransportMode Mode,
    string? Color,
    string FeedKey) : IEntity;
