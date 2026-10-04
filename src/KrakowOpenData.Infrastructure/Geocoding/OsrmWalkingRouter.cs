using System.Globalization;
using System.Text.Json;
using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Infrastructure.Common;
using Microsoft.Extensions.Logging;

namespace KrakowOpenData.Infrastructure.Geocoding;

/// <summary>
/// Walking routes from an OSRM-compatible service (OpenStreetMap street data, foot profile). Only coordinates leave the system.
/// One route search in the app makes 1 to 11 calls here (the direct route, 4 via points, and 6 more for the wide search, see
/// RouteService), so to respect the public server's fair use: coordinates are rounded to 5 decimals (about 1 m) and answers
/// cached for 30 minutes (at most <see cref="CacheEntries"/>), at most <see cref="MaxConcurrent"/> requests run at once, and a
/// 429 or repeated failures pause calls for a while (<see cref="UpstreamGuard"/>). While paused, the app gets its
/// straight-line "try again shortly" answer.
/// </summary>
public sealed class OsrmWalkingRouter(IHttpClientFactory http, IClock clock, ILogger<OsrmWalkingRouter> logger) : IWalkingRouter
{
    public const string HttpClientName = "routing";
    public const int MaxConcurrent = 4;
    public const int CacheEntries = 500;   // a full 5 km route is tens of kB, so this stays well inside the container's memory
    public static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(30);

    private readonly UpstreamGuard _guard = new("The street router", MaxConcurrent, TimeSpan.FromSeconds(5), clock, logger);
    private readonly BoundedTtlCache<IReadOnlyList<RoutePath>> _cache = new(CacheEntries, CacheTime, clock);

    public async Task<IReadOnlyList<RoutePath>> RouteAsync(IReadOnlyList<GeoPoint> waypoints, bool alternatives, CancellationToken ct = default)
    {
        var coords = string.Join(';', waypoints.Select(p => $"{Num(p.Longitude)},{Num(p.Latitude)}"));
        var url = $"route/v1/foot/{coords}?overview=full&geometries=geojson&steps=false&alternatives={(alternatives ? "true" : "false")}";
        if (_cache.TryGet(url, out var hit)) return hit;

        var routes = Parse(await _guard.GetStringAsync(http.CreateClient(HttpClientName), url, ct));
        _cache.Set(url, routes);
        return routes;
    }

    /// <summary>Reads an OSRM route response (GeoJSON geometries: [lon, lat] pairs).</summary>
    public static IReadOnlyList<RoutePath> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("routes", out var routes) || routes.ValueKind != JsonValueKind.Array) return [];

        var result = new List<RoutePath>();
        foreach (var route in routes.EnumerateArray())
        {
            if (!route.TryGetProperty("geometry", out var geometry) || !geometry.TryGetProperty("coordinates", out var coordinates)) continue;
            var points = new List<GeoPoint>();
            foreach (var c in coordinates.EnumerateArray())
                points.Add(new GeoPoint(c[1].GetDouble(), c[0].GetDouble()));
            if (points.Count < 2) continue;
            var distance = route.TryGetProperty("distance", out var d) ? d.GetDouble() : 0;
            result.Add(new RoutePath(points, distance));
        }

        return result;
    }

    /// <summary>5 decimals is about 1 m: plenty for a walking route, and the same trip asked twice hits the cache.</summary>
    private static string Num(double v) => v.ToString("0.00000", CultureInfo.InvariantCulture);
}
