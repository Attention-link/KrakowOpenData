using System.Globalization;
using System.Text.Json;
using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Infrastructure.Imgw;

namespace KrakowOpenData.Infrastructure.Gios;

public sealed record GiosStation(int Id, string Name, GeoPoint? Location);

public sealed record GiosSensor(int Id, string Code);

/// <summary>
/// Maps the GIOŚ PJP API v1 (api.gios.gov.pl/pjp-api/v1/rest) to simple records. The API uses
/// Polish keys with spaces and diacritics; they were checked against live responses on 3 Oct 2026.
/// Timestamps are Polish local time.
/// </summary>
public static class GiosJsonParser
{
    public const string SourceName = "GIOŚ";

    /// <summary>station/findAll → stations in <paramref name="city"/>.</summary>
    public static IReadOnlyList<GiosStation> ParseStations(string json, string city)
    {
        using var doc = JsonDocument.Parse(json);
        var wanted = TextNormalizer.Normalize(city);

        return Items(doc.RootElement, "Lista stacji pomiarowych")
            .Where(e => TextNormalizer.Normalize(ImgwJsonParser.Str(e, "Nazwa miasta")) == wanted)
            .Select(e =>
            {
                var id = ImgwJsonParser.Dbl(e, "Identyfikator stacji");
                var name = ImgwJsonParser.Str(e, "Nazwa stacji");
                if (id is null || name is null) return null;

                var lat = ImgwJsonParser.Dbl(e, "WGS84 φ N");
                var lon = ImgwJsonParser.Dbl(e, "WGS84 λ E");
                return new GiosStation((int)id.Value, name, lat is not null && lon is not null ? new GeoPoint(lat.Value, lon.Value) : null);
            })
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }

    /// <summary>station/sensors/{id} → sensor id and pollutant code (PM10, PM2.5, NO2, …).</summary>
    public static IReadOnlyList<GiosSensor> ParseSensors(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Items(doc.RootElement, "Lista stanowisk pomiarowych dla podanej stacji")
            .Select(e => (Id: ImgwJsonParser.Dbl(e, "Identyfikator stanowiska"), Code: ImgwJsonParser.Str(e, "Wskaźnik - kod")))
            .Where(x => x.Id is not null && x.Code is not null)
            .Select(x => new GiosSensor((int)x.Id!.Value, x.Code!))
            .ToList();
    }

    /// <summary>data/getData/{sensorId} → the newest non-null value and its time.</summary>
    public static (DateTimeOffset MeasuredAt, double Value)? ParseLatest(string json)
    {
        using var doc = JsonDocument.Parse(json);
        (DateTimeOffset, double)? latest = null;

        foreach (var e in Items(doc.RootElement, "Lista danych pomiarowych"))
        {
            var value = ImgwJsonParser.Dbl(e, "Wartość");
            var at = ParseLocal(ImgwJsonParser.Str(e, "Data"));
            if (value is null || at is null) continue;
            if (latest is null || at.Value > latest.Value.Item1) latest = (at.Value, value.Value);
        }

        return latest;
    }

    /// <summary>aqindex/getIndex/{stationId} → index category name, e.g. "Dobry".</summary>
    public static string? ParseIndexName(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        var index = doc.RootElement.TryGetProperty("AqIndex", out var x) ? x : doc.RootElement;
        return index.ValueKind == JsonValueKind.Object ? ImgwJsonParser.Str(index, "Nazwa kategorii indeksu") : null;
    }

    /// <summary>"2026-10-03 10:00:00" in Polish local time → absolute moment.</summary>
    public static DateTimeOffset? ParseLocal(string? text)
    {
        if (text is null) return null;
        string[] formats = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm:ss"];
        if (!DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return null;

        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, KrakowTime.TimeZone.GetUtcOffset(local));
    }

    private static IEnumerable<JsonElement> Items(JsonElement root, string listKey)
    {
        var list = root.ValueKind == JsonValueKind.Object && root.TryGetProperty(listKey, out var l) ? l : root;
        if (list.ValueKind != JsonValueKind.Array) yield break;
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object) yield return item;
        }
    }
}
