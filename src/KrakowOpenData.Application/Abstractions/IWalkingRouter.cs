using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Application.Abstractions;

/// <summary>A walking route that follows real streets and paths: its geometry and its length along the way.</summary>
public sealed record RoutePath(IReadOnlyList<GeoPoint> Points, double DistanceMeters);

/// <summary>
/// Finds walking routes along the street network (OpenStreetMap). The first path returned is the router's fastest one;
/// with <c>alternatives</c> it may add others. Throws when the routing service cannot be reached.
/// </summary>
public interface IWalkingRouter
{
    /// <summary>Routes through <paramref name="waypoints"/> in order (start, optional via points, destination).</summary>
    Task<IReadOnlyList<RoutePath>> RouteAsync(IReadOnlyList<GeoPoint> waypoints, bool alternatives, CancellationToken ct = default);
}
