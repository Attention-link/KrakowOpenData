using System.Globalization;
using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Domain.PublicServices;
using KrakowOpenData.Domain.UrbanSpace;

namespace KrakowOpenData.Infrastructure.OpenDataPortal;

/// <summary>
/// Turns city Open Data tables into typed records: districts with population, and the list of
/// city service cards (procedures). Column names checked against live responses on 3 Oct 2026.
/// </summary>
public static class CityTableMapper
{
    private static readonly string[] Roman =
        ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII", "XIII", "XIV", "XV", "XVI", "XVII", "XVIII"];

    /// <summary>
    /// zameldowani-na-pobyt-staly-km-*: one row per district and sex
    /// (Nr_Dzielnicy, Dzielnica "Dzielnica I Stare Miasto", Płeć K/M, Liczba_osób).
    /// </summary>
    public static IReadOnlyList<District> Districts(OpenDataTableContent table, string asOf)
    {
        return table.Rows
            .Select(r => (Number: Int(r, "Nr_Dzielnicy"), Name: Get(r, "Dzielnica"), Sex: Get(r, "Płeć"), Count: Int(r, "Liczba_osób")))
            .Where(r => r.Number is >= 1 and <= 18)
            .GroupBy(r => r.Number!.Value)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var women = g.Where(r => r.Sex == "K").Sum(r => r.Count ?? 0);
                var men = g.Where(r => r.Sex == "M").Sum(r => r.Count ?? 0);
                var total = g.Sum(r => r.Count ?? 0);
                return new District(
                    Roman[g.Key - 1],
                    g.Key,
                    CleanDistrictName(g.Select(r => r.Name).FirstOrDefault(n => n is not null) ?? Roman[g.Key - 1], Roman[g.Key - 1]),
                    total,
                    $"Permanent residents on {asOf}: {women} women, {men} men",
                    "Otwarte Dane Kraków");
            })
            .ToList();
    }

    /// <summary>uslugi-adm-procedury-zew-gmk: Symbol usługi, Nazwa usługi, Adres do karty na BIP, Wersja, Data publikacji.</summary>
    public static IReadOnlyList<CityServiceCard> ServiceCards(OpenDataTableContent table) =>
        table.Rows
            .Select(r =>
            {
                var symbol = Get(r, "Symbol usługi");
                var title = Get(r, "Nazwa usługi");
                if (symbol is null || title is null) return null;

                var url = Get(r, "Adres do karty na BIP") ?? $"https://www.bip.krakow.pl/uslugi/{symbol}";
                var version = Get(r, "Wersja Karty Uslugi");
                var published = Get(r, "Data Publikacji W Bip");
                var summary = string.Join(", ", new[]
                {
                    version is null ? null : $"wersja {version}",
                    published is null ? null : $"opublikowano {published}"
                }.Where(s => s is not null));

                return new CityServiceCard(symbol, title, Topic(symbol), summary, [], null, null, null, null, null, url, []);
            })
            .Where(c => c is not null)
            .Select(c => c!)
            .ToList();

    /// <summary>"AM-12" → "AM": the department prefix groups related procedures.</summary>
    private static string Topic(string symbol)
    {
        var dash = symbol.IndexOf('-');
        return dash > 0 ? symbol[..dash] : symbol;
    }

    private static string CleanDistrictName(string name, string roman)
    {
        var prefix = $"Dzielnica {roman} ";
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? name[prefix.Length..] : name;
    }

    private static string? Get(IReadOnlyDictionary<string, string?> row, string column)
    {
        if (row.TryGetValue(column, out var v)) return string.IsNullOrWhiteSpace(v) ? null : v.Trim();

        // The city sometimes pads column names with spaces.
        foreach (var (key, value) in row)
        {
            if (string.Equals(key.Trim(), column, StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        return null;
    }

    private static int? Int(IReadOnlyDictionary<string, string?> row, string column) =>
        int.TryParse(Get(row, column), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
}
