using System.Text.Json;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Safety;

namespace KrakowOpenData.Infrastructure.OpenStreetMap;

/// <summary>
/// Builds the Overpass query for the places used by the heat and night-safety scores (parks, libraries, pharmacies,
/// hospitals, police stations) and maps its JSON to <see cref="SafetyPlace"/> records.
/// <c>out bb tags</c> gives every way and relation a bounding box (a centre is taken as its middle), so large parks and hospital
/// campuses can be treated as areas: <see cref="SafetyPlace.EquivalentRadiusMeters"/> is half the side of a square
/// with the same bounding-box area, capped (600 m for parks, 150 m for hospitals).
/// </summary>
public static class SafetyPlacesParser
{
    public const string SourceName = "OpenStreetMap";

    private const double MetersPerDegreeLatitude = 111_320;
    private const double ParkRadiusCap = 600;
    private const double HospitalRadiusCap = 150;

    public static string BuildQuery(string areaName) =>
        "[out:json][timeout:120];" +
        $"area[\"name\"=\"{areaName}\"][\"admin_level\"=\"8\"]->.a;(" +
        "nwr[\"amenity\"~\"^(police|hospital|pharmacy|library)$\"](area.a);" +
        "nwr[\"leisure\"=\"park\"](area.a);" +
        ");out bb tags;";

    public static IReadOnlyList<SafetyPlace> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var places = new List<SafetyPlace>();
        if (!doc.RootElement.TryGetProperty("elements", out var elements) || elements.ValueKind != JsonValueKind.Array) return places;

        foreach (var e in elements.EnumerateArray())
        {
            if (!e.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Object) continue;
            var type = e.TryGetProperty("type", out var t) ? t.GetString() : null;
            var osmId = e.TryGetProperty("id", out var i) ? i.GetRawText() : null;
            if (type is null || osmId is null) continue;

            var kind = (Tag(tags, "amenity"), Tag(tags, "leisure")) switch
            {
                ("police", _) => (SafetyPlaceKind?)SafetyPlaceKind.Police,
                ("hospital", _) => SafetyPlaceKind.Hospital,
                ("pharmacy", _) => SafetyPlaceKind.Pharmacy,
                ("library", _) => SafetyPlaceKind.Library,
                (_, "park") => SafetyPlaceKind.Park,
                _ => null
            };
            if (kind is null || Location(e) is not { } location) continue;

            var radius = kind switch
            {
                SafetyPlaceKind.Park => Math.Min(ParkRadiusCap, EquivalentRadius(e)),
                SafetyPlaceKind.Hospital => Math.Min(HospitalRadiusCap, EquivalentRadius(e)),
                _ => 0
            };

            places.Add(new SafetyPlace(
                $"osm-{type[0]}{osmId}", kind.Value, Tag(tags, "name"), location, Tag(tags, "opening_hours"), Math.Round(radius), SourceName));
        }

        return places;
    }

    /// <summary>A node's own position; otherwise the middle of the bounding box (<c>out bb</c> gives no separate centre).</summary>
    private static GeoPoint? Location(JsonElement e)
    {
        var source = e.TryGetProperty("center", out var c) ? c : e;
        if (source.TryGetProperty("lat", out var lat) && source.TryGetProperty("lon", out var lon) &&
            lat.ValueKind == JsonValueKind.Number && lon.ValueKind == JsonValueKind.Number)
        {
            return new GeoPoint(lat.GetDouble(), lon.GetDouble());
        }

        if (e.TryGetProperty("bounds", out var b) && b.ValueKind == JsonValueKind.Object &&
            b.TryGetProperty("minlat", out var minLat) && b.TryGetProperty("maxlat", out var maxLat) &&
            b.TryGetProperty("minlon", out var minLon) && b.TryGetProperty("maxlon", out var maxLon))
        {
            return new GeoPoint((minLat.GetDouble() + maxLat.GetDouble()) / 2, (minLon.GetDouble() + maxLon.GetDouble()) / 2);
        }

        return null;
    }

    /// <summary>Half the side of a square with the bounding box's area, in metres; 0 when there is no box (a node).</summary>
    private static double EquivalentRadius(JsonElement e)
    {
        if (!e.TryGetProperty("bounds", out var b) || b.ValueKind != JsonValueKind.Object) return 0;
        if (!b.TryGetProperty("minlat", out var minLat) || !b.TryGetProperty("maxlat", out var maxLat) ||
            !b.TryGetProperty("minlon", out var minLon) || !b.TryGetProperty("maxlon", out var maxLon))
            return 0;

        var midLat = (minLat.GetDouble() + maxLat.GetDouble()) / 2;
        var height = (maxLat.GetDouble() - minLat.GetDouble()) * MetersPerDegreeLatitude;
        var width = (maxLon.GetDouble() - minLon.GetDouble()) * MetersPerDegreeLatitude * Math.Cos(midLat * Math.PI / 180);
        return Math.Max(0, 0.5 * Math.Sqrt(Math.Max(0, width * height)));
    }

    private static string? Tag(JsonElement tags, string key) =>
        tags.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;
}
