using System.Globalization;
using System.Text.Json;
using KrakowOpenData.Application.Accessibility;
using KrakowOpenData.Domain.Accessibility;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Infrastructure.Options;

namespace KrakowOpenData.Infrastructure.OpenStreetMap;

/// <summary>
/// Builds the Overpass query for barriers and amenities that matter to wheelchair users, people with prams and people who walk
/// with difficulty, and maps its JSON (<c>out center tags meta</c> for points, <c>out geom tags meta</c> for paths) to
/// <see cref="AccessFeature"/> items. <c>meta</c> gives every element its last-edit timestamp, which becomes the item's date
/// and reliability level. Values are normalised by <see cref="AccessTagNormaliser"/>; nothing is guessed.
/// </summary>
public static class AccessFeaturesParser
{
    public const string SourceName = "OpenStreetMap";

    /// <summary>The query for a box (south, west, north, east). Paths with a known surface, slope or width come with their geometry.</summary>
    public static string BuildQuery(GeoBoxOptions box)
    {
        var b = string.Create(CultureInfo.InvariantCulture, $"{box.MinLatitude},{box.MinLongitude},{box.MaxLatitude},{box.MaxLongitude}");
        return "[out:json][timeout:180];(" +
               $"node[\"kerb\"]({b});" +
               $"nwr[\"highway\"=\"elevator\"]({b});" +
               $"node[\"entrance\"]({b});" +
               $"nwr[\"wheelchair\"][\"highway\"!~\".\"]({b});" +
               $"nwr[\"toilets:wheelchair\"]({b});" +
               $"nwr[\"amenity\"=\"toilets\"]({b});" +
               $"nwr[\"amenity\"=\"bench\"]({b});" +
               $"node[\"tactile_paving\"]({b});" +
               ");out center tags meta;(" +
               $"way[\"highway\"~\"^(footway|pedestrian|path|living_street|steps|cycleway)$\"]({b});" +
               $"way[\"highway\"][\"surface\"][\"highway\"!~\"^(motorway|motorway_link|trunk|trunk_link|construction|proposed|raceway|bus_guideway|platform|elevator)$\"]({b});" +
               $"way[\"highway\"][\"incline\"]({b});" +
               ");out geom tags meta;";
    }

    public static IReadOnlyList<AccessFeature> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var items = new List<AccessFeature>();
        if (!doc.RootElement.TryGetProperty("elements", out var elements) || elements.ValueKind != JsonValueKind.Array) return items;

        var seen = new HashSet<string>();
        foreach (var e in elements.EnumerateArray())
        {
            if (!e.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Object) continue;
            var type = e.TryGetProperty("type", out var t) ? t.GetString() : null;
            var osmId = e.TryGetProperty("id", out var i) ? i.GetRawText() : null;
            if (type is not ("node" or "way" or "relation") || osmId is null) continue;

            var id = $"{type}/{osmId}";
            if (!seen.Add(id)) continue;

            var item = Map(id, e, tags);
            if (item is not null) items.Add(item);
        }

        return items;
    }

    private static AccessFeature? Map(string id, JsonElement e, JsonElement tags)
    {
        string? Tag(string key) => tags.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;

        var line = Geometry(e);
        var location = Center(e, line);
        if (location is null) return null;

        var highway = Tag("highway");
        var amenity = Tag("amenity");
        var (ramp, rampNone) = AccessTagNormaliser.Ramp(Tag);
        var attrs = new AccessAttributes(
            Wheelchair: AccessTagNormaliser.Wheelchair(Tag("wheelchair")),
            Kerb: AccessTagNormaliser.Kerb(Tag("kerb")),
            Ramp: ramp,
            RampNone: rampNone,
            StepCount: AccessTagNormaliser.Count(Tag("step_count")),
            Handrail: AccessTagNormaliser.YesNo(Tag("handrail")) ?? AnyYes(Tag("handrail:left"), Tag("handrail:right"), Tag("handrail:center")),
            SurfaceClass: AccessTagNormaliser.SurfaceClass(Tag("surface"), Tag("smoothness")),
            SurfaceRaw: Tag("surface")?.ToLowerInvariant(),
            Smoothness: AccessTagNormaliser.Smoothness(Tag("smoothness")),
            InclinePercent: AccessTagNormaliser.InclinePercent(Tag("incline")),
            WidthMeters: AccessTagNormaliser.WidthMeters(Tag("width") ?? Tag("door:width")),
            Tactile: AccessTagNormaliser.Tactile(Tag("tactile_paving")),
            ToiletsWheelchair: AccessTagNormaliser.Wheelchair(Tag("toilets:wheelchair")),
            ChangingTable: AccessTagNormaliser.YesNo(Tag("changing_table")),
            Door: Tag("door") ?? Tag("automatic_door") switch { "yes" => "automatic", _ => null },
            Backrest: AccessTagNormaliser.YesNo(Tag("backrest")),
            Highway: highway,
            EntranceType: Tag("entrance"),
            Description: Tag("wheelchair:description:pl") ?? Tag("wheelchair:description"),
            Category: amenity ?? Tag("shop") ?? Tag("tourism") ?? Tag("leisure") ?? Tag("office") ?? Tag("healthcare") ?? Tag("historic") ?? Tag("building"));

        AccessKind? kind = null;
        if (highway == "steps") kind = AccessKind.Steps;
        else if (highway == "elevator") kind = AccessKind.Elevator;
        else if (e.GetProperty("type").GetString() == "node" && Tag("kerb") is not null) kind = AccessKind.Kerb;
        else if (Tag("entrance") is not null && e.GetProperty("type").GetString() == "node") kind = AccessKind.Entrance;
        else if (amenity == "toilets") kind = AccessKind.Toilets;
        else if (amenity == "bench") kind = AccessKind.Bench;
        else if (highway is not null && line is not null) kind = AccessKind.Path;
        else if (Tag("tactile_paving") is not null && e.GetProperty("type").GetString() == "node") kind = AccessKind.TactilePaving;
        else if (attrs.Wheelchair is not null || attrs.ToiletsWheelchair is not null) kind = AccessKind.Place;
        if (kind is null) return null;

        // Places without a name and without a recognisable kind say too little to show.
        if (kind == AccessKind.Place && Tag("name") is null && attrs.Category is null) return null;

        var status = AccessRules.Status(kind.Value, attrs, AccessProfile.Wheelchair);
        return new AccessFeature(
            id,
            kind.Value,
            location.Value,
            Tag("name") ?? Tag("ref"),
            status,
            attrs,
            Timestamp(e),
            SourceName,
            kind is AccessKind.Steps or AccessKind.Path ? line : null);
    }

    private static bool? AnyYes(params string?[] values) =>
        values.Any(v => v == "yes") ? true : values.All(v => v is null) ? null : values.Any(v => v == "no") ? false : null;

    private static DateTimeOffset? Timestamp(JsonElement e) =>
        e.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String &&
        DateTimeOffset.TryParse(ts.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when)
            ? when
            : null;

    /// <summary>The way's points from <c>out geom</c> (an array of {lat, lon}); null for points or when missing.</summary>
    private static List<GeoPoint>? Geometry(JsonElement e)
    {
        if (!e.TryGetProperty("geometry", out var g) || g.ValueKind != JsonValueKind.Array) return null;
        var points = new List<GeoPoint>();
        foreach (var n in g.EnumerateArray())
        {
            if (n.ValueKind == JsonValueKind.Object && n.TryGetProperty("lat", out var lat) && n.TryGetProperty("lon", out var lon) &&
                lat.ValueKind == JsonValueKind.Number && lon.ValueKind == JsonValueKind.Number)
                points.Add(new GeoPoint(Math.Round(lat.GetDouble(), 6), Math.Round(lon.GetDouble(), 6)));
        }

        return points.Count >= 2 ? points : null;
    }

    /// <summary>A node's position, a way's <c>center</c>, or the middle vertex of its geometry.</summary>
    private static GeoPoint? Center(JsonElement e, List<GeoPoint>? line)
    {
        var source = e.TryGetProperty("center", out var c) ? c : e;
        if (source.TryGetProperty("lat", out var lat) && source.TryGetProperty("lon", out var lon) &&
            lat.ValueKind == JsonValueKind.Number && lon.ValueKind == JsonValueKind.Number)
            return new GeoPoint(lat.GetDouble(), lon.GetDouble());

        if (line is { Count: > 0 })
        {
            if (line.Count == 2) return new GeoPoint((line[0].Latitude + line[1].Latitude) / 2, (line[0].Longitude + line[1].Longitude) / 2);
            return line[line.Count / 2];
        }

        return null;
    }
}
