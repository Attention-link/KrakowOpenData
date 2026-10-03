using System.Text.Json;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.PublicServices;
using KrakowOpenData.Infrastructure.Imgw;

namespace KrakowOpenData.Infrastructure.Nfz;

public sealed record NfzPage(IReadOnlyList<WaitingListEntry> Entries, string? Next);

/// <summary>
/// Parses the NFZ "terminy leczenia" API (api.nfz.gov.pl/app-itl-api/queues, api-version 1.3).
/// Shape checked on 3 Oct 2026: data[].attributes with kebab-case keys, Y/N flags, and wait
/// statistics under statistics.provider-data. First-available dates were null in every response.
/// </summary>
public static class NfzJsonParser
{
    public const string SourceName = "NFZ";

    public static NfzPage ParseQueues(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var entries = new List<WaitingListEntry>();

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (ParseEntry(item) is { } entry) entries.Add(entry);
            }
        }

        string? next = null;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Object)
        {
            next = ImgwJsonParser.Str(links, "next");
        }

        return new NfzPage(entries, next);
    }

    private static WaitingListEntry? ParseEntry(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        var a = item.TryGetProperty("attributes", out var attributes) && attributes.ValueKind == JsonValueKind.Object ? attributes : item;

        var id = ImgwJsonParser.Str(item, "id");
        var benefit = ImgwJsonParser.Str(a, "benefit");
        var provider = ImgwJsonParser.Str(a, "provider");
        if (id is null || benefit is null || provider is null) return null;

        var lat = ImgwJsonParser.Dbl(a, "latitude");
        var lon = ImgwJsonParser.Dbl(a, "longitude");

        int? awaiting = null, averageDays = null;
        string? updated = null;
        if (a.TryGetProperty("statistics", out var stats) && stats.ValueKind == JsonValueKind.Object &&
            stats.TryGetProperty("provider-data", out var pd) && pd.ValueKind == JsonValueKind.Object)
        {
            awaiting = ImgwJsonParser.Dbl(pd, "awaiting") is { } w ? (int)w : null;
            averageDays = ImgwJsonParser.Dbl(pd, "average-period") is { } d ? (int)Math.Round(d) : null;
            updated = ImgwJsonParser.Str(pd, "update");
        }

        string? firstDate = null;
        if (a.TryGetProperty("dates", out var dates) && dates.ValueKind == JsonValueKind.Object)
        {
            firstDate = ImgwJsonParser.Str(dates, "date");
        }

        return new WaitingListEntry(
            id,
            benefit,
            provider,
            ImgwJsonParser.Str(a, "place"),
            ImgwJsonParser.Str(a, "address"),
            ImgwJsonParser.Str(a, "locality"),
            ImgwJsonParser.Str(a, "phone"),
            lat is not null && lon is not null ? new GeoPoint(lat.Value, lon.Value) : null,
            ImgwJsonParser.Dbl(a, "case") == 2,
            awaiting,
            averageDays,
            firstDate,
            updated,
            YesNo(a, "toilet"),
            YesNo(a, "ramp"),
            YesNo(a, "car-park"),
            YesNo(a, "elevator"),
            SourceName);
    }

    private static bool? YesNo(JsonElement e, string name) => ImgwJsonParser.Str(e, name)?.ToUpperInvariant() switch
    {
        "Y" or "T" or "TRUE" => true,
        "N" or "FALSE" => false,
        _ => null
    };
}
