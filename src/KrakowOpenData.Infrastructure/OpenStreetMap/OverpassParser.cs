using System.Globalization;
using System.Text.Json;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Mobility;
using KrakowOpenData.Domain.UrbanSpace;

namespace KrakowOpenData.Infrastructure.OpenStreetMap;

public sealed record OsmSnapshot(IReadOnlyList<ParkAndRideFacility> ParkAndRide, IReadOnlyList<Amenity> Amenities);

/// <summary>
/// Maps an Overpass API JSON response (<c>out center tags</c>) to P+R car parks and amenities.
/// Ways and relations carry a <c>center</c> instead of lat/lon.
/// </summary>
public static class OverpassParser
{
    public const string SourceName = "OpenStreetMap";

    /// <summary>One query for everything, so Overpass is called once (it rate-limits back-to-back calls).</summary>
    public static string BuildQuery(string areaName) =>
        "[out:json][timeout:90];" +
        $"area[\"name\"=\"{areaName}\"][\"admin_level\"=\"8\"]->.a;(" +
        "nwr[\"park_ride\"][\"park_ride\"!=\"no\"](area.a);" +
        "nwr[\"emergency\"=\"defibrillator\"](area.a);" +
        "nwr[\"amenity\"~\"^(toilets|charging_station|bicycle_parking)$\"](area.a);" +
        // Drinking water is mapped in several ways in OpenStreetMap; asking for only amenity=drinking_water misses many.
        // Anything tagged drinking_water=no is excluded. Taps and springs count only when tagged as drinkable.
        "nwr[\"amenity\"=\"drinking_water\"][\"drinking_water\"!=\"no\"](area.a);" +
        "nwr[\"amenity\"=\"water_point\"][\"drinking_water\"!=\"no\"](area.a);" +
        "nwr[\"man_made\"=\"water_tap\"][\"drinking_water\"=\"yes\"](area.a);" +
        "nwr[\"amenity\"=\"fountain\"][\"drinking_water\"=\"yes\"](area.a);" +
        "nwr[\"natural\"=\"spring\"][\"drinking_water\"=\"yes\"](area.a);" +
        ");out center tags;";

    /// <summary>
    /// True for anything a person could drink from: a drinking-water point, a water point, or a tap, fountain or spring that is
    /// explicitly tagged drinkable. <c>drinking_water=no</c> always wins.
    /// </summary>
    internal static bool IsDrinkingWater(JsonElement tags)
    {
        var drinkable = Tag(tags, "drinking_water");
        if (drinkable == "no") return false;
        return (Tag(tags, "amenity"), Tag(tags, "man_made"), Tag(tags, "natural")) switch
        {
            ("drinking_water", _, _) or ("water_point", _, _) => true,
            ("fountain", _, _) or (_, "water_tap", _) or (_, _, "spring") => drinkable == "yes",
            _ => false
        };
    }

    private static readonly string[] DetailKeys =
        ["opening_hours", "operator", "access", "fee", "capacity", "indoor", "level", "location", "description", "socket:type2", "socket:chademo", "socket:type2_combo", "wheelchair", "toilets:wheelchair", "changing_table"];

    public static OsmSnapshot Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var parkAndRide = new List<ParkAndRideFacility>();
        var amenities = new List<Amenity>();

        if (!doc.RootElement.TryGetProperty("elements", out var elements) || elements.ValueKind != JsonValueKind.Array)
            return new OsmSnapshot(parkAndRide, amenities);

        foreach (var e in elements.EnumerateArray())
        {
            var type = e.TryGetProperty("type", out var t) ? t.GetString() : null;
            var osmId = e.TryGetProperty("id", out var i) ? i.GetRawText() : null;
            if (type is null || osmId is null || !e.TryGetProperty("tags", out var tags)) continue;

            var location = Location(e);
            if (location is null) continue;
            var id = $"osm-{type[0]}{osmId}";

            if (Tag(tags, "park_ride") is { } pr && pr != "no")
            {
                // Only named car parks: unnamed park_ride areas are usually small kerbside spaces.
                if (Tag(tags, "name") is { } name && Tag(tags, "amenity") == "parking")
                {
                    parkAndRide.Add(new ParkAndRideFacility(
                        id, name, Address(tags), location, Int(tags, "capacity"), Int(tags, "capacity:charging"), null,
                        Tag(tags, "opening_hours"), Notes(tags, pr), SourceName));
                }

                continue;
            }

            AmenityKind? kind = (Tag(tags, "emergency"), Tag(tags, "amenity")) switch
            {
                ("defibrillator", _) => AmenityKind.Defibrillator,
                _ when IsDrinkingWater(tags) => AmenityKind.DrinkingWater,
                (_, "toilets") => AmenityKind.Toilets,
                (_, "charging_station") => AmenityKind.EvCharger,
                (_, "bicycle_parking") => AmenityKind.BikeParking,
                _ => null
            };
            if (kind is null) continue;

            var details = string.Join("; ", DetailKeys
                .Select(k => (Key: k, Value: Tag(tags, k)))
                .Where(x => x.Value is not null)
                .Select(x => $"{x.Key}: {x.Value}"));

            amenities.Add(new Amenity(id, kind.Value, Tag(tags, "name"), location.Value, details.Length == 0 ? null : details, SourceName));
        }

        return new OsmSnapshot(parkAndRide, amenities);
    }

    private static GeoPoint? Location(JsonElement e)
    {
        var source = e.TryGetProperty("center", out var c) ? c : e;
        if (source.TryGetProperty("lat", out var lat) && source.TryGetProperty("lon", out var lon) &&
            lat.ValueKind == JsonValueKind.Number && lon.ValueKind == JsonValueKind.Number)
        {
            return new GeoPoint(lat.GetDouble(), lon.GetDouble());
        }

        return null;
    }

    private static string? Tag(JsonElement tags, string key) =>
        tags.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;

    private static int? Int(JsonElement tags, string key) =>
        int.TryParse(Tag(tags, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static string? Address(JsonElement tags)
    {
        var street = Tag(tags, "addr:street");
        if (street is null) return null;
        var line = string.Join(" ", new[] { street, Tag(tags, "addr:housenumber") }.Where(s => s is not null));
        var city = string.Join(" ", new[] { Tag(tags, "addr:postcode"), Tag(tags, "addr:city") }.Where(s => s is not null));
        return city.Length == 0 ? line : $"{line}, {city}";
    }

    private static string? Notes(JsonElement tags, string parkRide)
    {
        var parts = new List<string>();
        if (Tag(tags, "fee") is { } fee) parts.Add($"fee: {fee}");
        if (Int(tags, "capacity:disabled") is { } disabled) parts.Add($"disabled spaces: {disabled}");
        if (parkRide is "train" or "bus" or "tram") parts.Add($"{parkRide} P+R");
        if (Tag(tags, "website") is { } web) parts.Add(web);
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }
}
