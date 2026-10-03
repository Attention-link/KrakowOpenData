using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Safety;
using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Api.Tests.Fakes;

/// <summary>Street routing without the network: the path goes through the waypoints with a bend, so a via point makes it longer.</summary>
public sealed class FakeWalkingRouter : IWalkingRouter
{
    public Task<IReadOnlyList<RoutePath>> RouteAsync(IReadOnlyList<GeoPoint> waypoints, bool alternatives, CancellationToken ct = default)
    {
        IReadOnlyList<RoutePath> result = [new RoutePath(waypoints, PathSampler.Length(waypoints))];
        return Task.FromResult(result);
    }
}
