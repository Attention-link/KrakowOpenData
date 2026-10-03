using System.Globalization;
using System.Text.Json;
using KrakowOpenData.Domain.ClimateAndCrisis;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Environment;

namespace KrakowOpenData.Infrastructure.Imgw;

/// <summary>
/// Maps IMGW public API JSON (danepubliczne.imgw.pl) to domain records. IMGW returns most numbers
/// as strings and uses Polish field names, so parsing is tolerant: missing or malformed values
/// become null instead of failing the whole response.
/// Field names verified against the public client libraries; re-check them against a live response
/// on day one, as the API is undocumented.
/// </summary>
public static class ImgwJsonParser
{
    public const string SourceName = "IMGW-PIB";

    /// <summary>/synop/station/{name} returns one object; /synop returns an array.</summary>
    public static IReadOnlyList<WeatherObservation> ParseSynop(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Elements(doc.RootElement)
            .Select(ParseSynopElement)
            .Where(o => o is not null)
            .Select(o => o!)
            .ToList();
    }

    /// <summary>
    /// /hydro returns every station in Poland. A station is kept when its id is listed, its name
    /// matches a filter, or its coordinates fall inside <paramref name="area"/>.
    /// </summary>
    public static IReadOnlyList<HydroObservation> ParseHydro(
        string json,
        IReadOnlyCollection<string> nameFilters,
        IReadOnlyCollection<string> stationIds,
        Func<double, double, bool>? area = null)
    {
        using var doc = JsonDocument.Parse(json);
        var results = new List<HydroObservation>();

        foreach (var e in Elements(doc.RootElement))
        {
            var id = Str(e, "id_stacji");
            var name = Str(e, "stacja");
            if (id is null || name is null) continue;

            var lat = Dbl(e, "lat");
            var lon = Dbl(e, "lon");

            var keep = stationIds.Contains(id) ||
                       nameFilters.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)) ||
                       (area is not null && lat is not null && lon is not null && area(lat.Value, lon.Value));
            if (!keep) continue;

            results.Add(new HydroObservation(
                id,
                name,
                Str(e, "rzeka") ?? "?",
                lat is not null && lon is not null ? new GeoPoint(lat.Value, lon.Value) : null,
                Dbl(e, "stan_wody"),
                Date(e, "stan_wody_data_pomiaru"),
                Dbl(e, "stan_ostrzegawczy"),
                Dbl(e, "stan_alarmowy"),
                null,
                SourceName));
        }

        return results;
    }

    public static IReadOnlyList<WeatherWarning> ParseWarnings(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var results = new List<WeatherWarning>();

        foreach (var e in Elements(doc.RootElement))
        {
            var id = Str(e, "id");
            if (id is null) continue;

            var teryt = new List<string>();
            if (e.TryGetProperty("teryt", out var t) && t.ValueKind == JsonValueKind.Array)
            {
                teryt.AddRange(t.EnumerateArray().Select(x => x.ToString()).Where(x => x.Length > 0));
            }

            var content = string.Join(" ", new[] { Str(e, "tresc"), Str(e, "komentarz") }.Where(s => !string.IsNullOrWhiteSpace(s)));

            results.Add(new WeatherWarning(
                id,
                Str(e, "nazwa_zdarzenia") ?? "Ostrzeżenie",
                (int)(Dbl(e, "stopien") ?? 0),
                Dbl(e, "prawdopodobienstwo") is { } p ? (int)p : null,
                Date(e, "obowiazuje_od"),
                Date(e, "obowiazuje_do"),
                content.Length == 0 ? null : content,
                teryt,
                SourceName));
        }

        return results;
    }

    /// <summary>
    /// /warningshydro uses its own shape: no TERYT codes, but a list of areas with a voivodeship
    /// name. Warnings for <paramref name="voivodeship"/> are kept and tagged with its TERYT code,
    /// so the usual <see cref="WeatherWarning.AppliesTo"/> filter works.
    /// </summary>
    public static IReadOnlyList<WeatherWarning> ParseHydroWarnings(
        string json, string voivodeship = "małopolskie", string voivodeshipTeryt = "12")
    {
        using var doc = JsonDocument.Parse(json);
        var results = new List<WeatherWarning>();
        var wanted = TextNormalizer.Normalize(voivodeship);

        foreach (var e in Elements(doc.RootElement))
        {
            var number = Str(e, "numer");
            if (number is null) continue;

            var areas = new List<string>();
            var matches = false;
            if (e.TryGetProperty("obszary", out var obszary) && obszary.ValueKind == JsonValueKind.Array)
            {
                foreach (var area in obszary.EnumerateArray())
                {
                    if (TextNormalizer.Normalize(Str(area, "wojewodztwo")) != wanted) continue;
                    matches = true;
                    if (Str(area, "opis") is { } description) areas.Add(description);
                }
            }

            if (!matches) continue;

            var validTo = Date(e, "data_do");
            if (validTo is { Year: >= 9999 }) validTo = null; // "until further notice"

            var text = new[] { Str(e, "przebieg"), Str(e, "komentarz"), areas.Count > 0 ? string.Join("; ", areas.Distinct()) : null }
                .Where(s => !string.IsNullOrWhiteSpace(s));
            var content = string.Join(" ", text);

            results.Add(new WeatherWarning(
                $"hydro-{Str(e, "biuro") ?? "imgw"}-{number}",
                Str(e, "zdarzenie") ?? "Ostrzeżenie hydrologiczne",
                (int)(Dbl(e, "stopień") ?? Dbl(e, "stopien") ?? 0),
                Dbl(e, "prawdopodobienstwo") is { } p ? (int)p : null,
                Date(e, "data_od") ?? Date(e, "opublikowano"),
                validTo,
                content.Length == 0 ? null : content,
                [voivodeshipTeryt],
                SourceName));
        }

        return results;
    }

    private static WeatherObservation? ParseSynopElement(JsonElement e)
    {
        var id = Str(e, "id_stacji");
        var name = Str(e, "stacja");
        var date = Str(e, "data_pomiaru");
        if (id is null || name is null || date is null) return null;
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) return null;

        var hour = (int)(Dbl(e, "godzina_pomiaru") ?? 0);
        var observedAt = new DateTimeOffset(day.ToDateTime(new TimeOnly(Math.Clamp(hour, 0, 23), 0)), TimeSpan.Zero);

        return new WeatherObservation(
            id,
            name,
            observedAt,
            Dbl(e, "temperatura"),
            Dbl(e, "predkosc_wiatru"),
            Dbl(e, "kierunek_wiatru") is { } dir ? (int)dir : null,
            Dbl(e, "wilgotnosc_wzgledna"),
            Dbl(e, "suma_opadu"),
            Dbl(e, "cisnienie"),
            SourceName);
    }

    private static IEnumerable<JsonElement> Elements(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray()) yield return item;
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            yield return root;
        }
    }

    internal static string? Str(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        var s = v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    internal static double? Dbl(JsonElement e, string name)
    {
        var s = Str(e, name);
        if (s is null) return null;
        return double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>IMGW timestamps look like "2026-10-03 06:00:00" and are treated as UTC.</summary>
    internal static DateTimeOffset? Date(JsonElement e, string name)
    {
        var s = Str(e, name);
        if (s is null) return null;

        string[] formats = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd"];
        if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
        {
            return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
        }

        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
        {
            return dto;
        }

        return null;
    }
}
