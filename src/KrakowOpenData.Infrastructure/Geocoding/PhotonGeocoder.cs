using System.Globalization;
using System.Text.Json;
using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Safety;
using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Infrastructure.Common;
using KrakowOpenData.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Infrastructure.Geocoding;

/// <summary>
/// Address search and reverse lookup through Photon (komoot's OpenStreetMap geocoder, which supports search-as-you-type,
/// unlike the public Nominatim service). The app calls this API, not Photon, so the browser never talks to a third party.
/// Results are cached (searches 1 h, reverse lookups 24 h, each at most <see cref="CacheEntries"/>), at most
/// <see cref="MaxConcurrent"/> requests run at once, and a 429 or repeated failures pause calls for a while
/// (<see cref="UpstreamGuard"/>), to respect Photon's fair use. One search box keystroke (after the app's 300 ms debounce)
/// is at most one call; the same text from anyone in the next hour is none. Only the typed text and a coordinate leave the
/// system; nothing identifies the user.
/// </summary>
public sealed class PhotonGeocoder(
    IHttpClientFactory http, IClock clock, IOptions<KrakowDataOptions> options, ILogger<PhotonGeocoder> logger) : IGeocoder
{
    public const string HttpClientName = "geocoder";
    public const int MaxConcurrent = 4;
    public const int CacheEntries = 5000;

    private readonly UpstreamGuard _guard = new("The address search", MaxConcurrent, TimeSpan.FromSeconds(5), clock, logger);
    private readonly BoundedTtlCache<IReadOnlyList<GeocodeResultDto>> _searches = new(CacheEntries, TimeSpan.FromHours(1), clock);
    private readonly BoundedTtlCache<GeocodeResultDto?> _reverse = new(CacheEntries, TimeSpan.FromHours(24), clock);

    public async Task<IReadOnlyList<GeocodeResultDto>> SearchAsync(string query, GeoPoint? near, int limit, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 10);
        var text = Normalise(query);
        var key = $"{text.ToLowerInvariant()}:{near?.Latitude:0.00}:{near?.Longitude:0.00}:{limit}";
        if (_searches.TryGet(key, out var hit)) return hit;

        var url = $"api/?q={Uri.EscapeDataString(text)}&limit={limit * 2}&lang=default&bbox=19.7,49.9,20.3,50.2";
        if (near is { } p) url += $"&lat={Num(p.Latitude)}&lon={Num(p.Longitude)}";

        var results = PhotonParser.Parse(await GetAsync(url, ct))
            .Where(r => GridSpec.IsInArea(new GeoPoint(r.Latitude, r.Longitude)))
            .Take(limit)
            .ToList();
        _searches.Set(key, results);
        return results;
    }

    public async Task<GeocodeResultDto?> ReverseAsync(GeoPoint point, CancellationToken ct = default)
    {
        var key = $"{point.Latitude:0.0000}:{point.Longitude:0.0000}";
        if (_reverse.TryGet(key, out var hit)) return hit;

        var url = $"reverse?lat={Num(point.Latitude)}&lon={Num(point.Longitude)}&radius=0.3&limit=1&lang=default";
        var result = PhotonParser.Parse(await GetAsync(url, ct)).FirstOrDefault();
        _reverse.Set(key, result);
        return result;
    }

    /// <summary>"  Rynek   Główny " and "Rynek Główny" are the same search (and the same cache entry, whatever the case).</summary>
    public static string Normalise(string query) =>
        string.Join(' ', query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private async Task<string> GetAsync(string relativeUrl, CancellationToken ct)
    {
        var client = http.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(options.Value.GeocoderBaseUrl);
        try
        {
            return await _guard.GetStringAsync(client, relativeUrl, ct);
        }
        catch (UpstreamBusyException)
        {
            throw;   // already logged once when the pause started; one line per blocked search would flood the log
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Geocoder request failed");
            throw;
        }
    }

    private static string Num(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
}

/// <summary>Maps Photon's GeoJSON to <see cref="GeocodeResultDto"/> with a short, readable label.</summary>
public static class PhotonParser
{
    public static IReadOnlyList<GeocodeResultDto> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var results = new List<GeocodeResultDto>();
        var seen = new HashSet<string>();
        if (!doc.RootElement.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array) return results;

        foreach (var f in features.EnumerateArray())
        {
            if (!f.TryGetProperty("geometry", out var g) || !g.TryGetProperty("coordinates", out var c) || c.GetArrayLength() < 2) continue;
            var lon = c[0].GetDouble();
            var lat = c[1].GetDouble();
            if (!f.TryGetProperty("properties", out var p) || p.ValueKind != JsonValueKind.Object) continue;

            string? Prop(string k) => p.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString()!.Trim() : null;

            var name = Prop("name");
            var street = Prop("street");
            var house = Prop("housenumber");
            var district = Prop("district") ?? Prop("locality");
            var kind = Prop("type") ?? Prop("osm_value") ?? "place";

            // "Sukiennice, Rynek Główny 3, Stare Miasto": the place, its street and number, and the district.
            var streetLine = street is null ? null : house is null ? street : $"{street} {house}";
            var parts = new List<string>();
            if (name is not null && !string.Equals(name, street, StringComparison.OrdinalIgnoreCase)) parts.Add(name);
            if (streetLine is not null) parts.Add(streetLine);
            else if (name is not null && parts.Count == 0) parts.Add(name);
            if (district is not null && !parts.Contains(district, StringComparer.OrdinalIgnoreCase)) parts.Add(district);
            if (parts.Count == 0) continue;

            var label = string.Join(", ", parts);
            if (!seen.Add($"{label}|{lat:0.000}|{lon:0.000}")) continue;
            results.Add(new GeocodeResultDto(label, name, street, house, district, Prop("postcode"), Math.Round(lat, 6), Math.Round(lon, 6), kind));
        }

        return results;
    }
}
