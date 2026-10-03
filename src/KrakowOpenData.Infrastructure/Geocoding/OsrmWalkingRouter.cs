using System.Globalization;
using System.Text.Json;
using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Domain.Common;
using Microsoft.Extensions.Caching.Memory;

namespace KrakowOpenData.Infrastructure.Geocoding;

/// <summary>
/// Walking routes from an OSRM-compatible service (OpenStreetMap street data, foot profile). Only coordinates leave the system.
/// Answers are cached for 30 minutes and at most three requests run at once, to respect the public server's fair use.
/// </summary>
public sealed class OsrmWalkingRouter(IHttpClientFactory http, IMemoryCache cache) : IWalkingRouter
{
    public const string HttpClientName = "routing";
    private static readonly SemaphoreSlim Gate = new(3);

    public async Task<IReadOnlyList<RoutePath>> RouteAsync(IReadOnlyList<GeoPoint> waypoints, bool alternatives, CancellationToken ct = default)
    {
        var coords = string.Join(';', waypoints.Select(p => $"{Num(p.Longitude)},{Num(p.Latitude)}"));
        var url = $"route/v1/foot/{coords}?overview=full&geometries=geojson&steps=false&alternatives={(alternatives ? "true" : "false")}";
        if (cache.TryGetValue(url, out IReadOnlyList<RoutePath>? hit) && hit is not null) return hit;

        await Gate.WaitAsync(ct);
        try
        {
            using var response = await http.CreateClient(HttpClientName).GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            var routes = Parse(await response.Content.ReadAsStringAsync(ct));
            cache.Set(url, routes, TimeSpan.FromMinutes(30));
            return routes;
        }
        finally
        {
            Gate.Release();
        }
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

    private static string Num(double v) => v.ToString("0.000000", CultureInfo.InvariantCulture);
}
