using System.Globalization;
using System.Text.Json;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.UrbanSpace;

namespace KrakowOpenData.Infrastructure.OpenStreetMap;

/// <summary>Builds the street-lamp Overpass query and maps its JSON to <see cref="StreetLight"/> records.</summary>
public static class StreetLightParser
{
    /// <summary>
    /// Nodes only (~27,000 in Kraków). <c>out body</c> returns tags and coordinates;
    /// <c>out tags</c> would drop the coordinates.
    /// </summary>
    public static string BuildQuery(string areaName) =>
        "[out:json][timeout:120];" +
        $"area[\"name\"=\"{areaName}\"][\"admin_level\"=\"8\"]->.a;" +
        "node[\"highway\"=\"street_lamp\"](area.a);out body;";

    public static IReadOnlyList<StreetLight> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var lights = new List<StreetLight>();
        if (!doc.RootElement.TryGetProperty("elements", out var elements) || elements.ValueKind != JsonValueKind.Array) return lights;
        var received = 0;

        foreach (var e in elements.EnumerateArray())
        {
            received++;
            if (!e.TryGetProperty("id", out var id) ||
                !e.TryGetProperty("lat", out var lat) || lat.ValueKind != JsonValueKind.Number ||
                !e.TryGetProperty("lon", out var lon) || lon.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            var tags = e.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Object ? t : default;
            string? Tag(string key) =>
                tags.ValueKind == JsonValueKind.Object && tags.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(v.GetString())
                    ? v.GetString()!.Trim()
                    : null;

            lights.Add(new StreetLight(
                $"osm-n{id.GetRawText()}",
                new GeoPoint(lat.GetDouble(), lon.GetDouble()),
                StreetLight.TechnologyFrom(Tag("lamp_type"), Tag("light:method")),
                Tag("lamp_mount") ?? Tag("support"),
                int.TryParse(Tag("light:count"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? count : null,
                ParseHeight(Tag("height")),
                Tag("operator"),
                Tag("ref") ?? Tag("lamp_ref"),
                OverpassParser.SourceName));
        }

        // Lamps came back but none had a position: the query asked for the wrong output mode.
        // Fail instead of caching an empty list for a day.
        if (received > 0 && lights.Count == 0)
            throw new InvalidDataException($"Overpass returned {received} street lamps without coordinates; use 'out body'.");

        return lights;
    }

    /// <summary>"8", "8 m", "8.5" → metres.</summary>
    private static double? ParseHeight(string? value)
    {
        if (value is null) return null;
        var number = value.Replace("m", "", StringComparison.OrdinalIgnoreCase).Replace(',', '.').Trim();
        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var h) && h > 0 ? h : null;
    }
}
