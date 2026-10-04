using System.Globalization;
using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.Accessibility;

namespace KrakowOpenData.Application.Accessibility;

/// <summary>
/// Turns an item's attributes into concrete facts ("Rampa: brak", "Krawężnik: wysoki", "Nawierzchnia: kostka brukowa").
/// The facts that matter for a kind are always listed, with "brak danych" when OSM says nothing, so an item is never a bare
/// accessible / inaccessible. Polish wording is the API's default; apps translate by key and value.
/// </summary>
public static class AccessFacts
{
    public const string UnknownValue = "unknown";
    public const string UnknownText = "brak danych";

    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        ["wheelchair"] = "Dostęp dla wózka",
        ["step_count"] = "Liczba stopni",
        ["ramp"] = "Rampa",
        ["handrail"] = "Poręcz",
        ["kerb"] = "Krawężnik",
        ["tactile"] = "Płytki z wypustkami",
        ["width"] = "Szerokość",
        ["door"] = "Drzwi",
        ["toilets_wheelchair"] = "Toaleta dla wózka",
        ["changing_table"] = "Przewijak",
        ["backrest"] = "Oparcie",
        ["surface"] = "Nawierzchnia",
        ["smoothness"] = "Równość nawierzchni",
        ["incline"] = "Nachylenie",
        ["category"] = "Rodzaj",
        ["description"] = "Opis"
    };

    public static readonly IReadOnlyDictionary<string, string> Surfaces = new Dictionary<string, string>
    {
        ["sett"] = "kostka brukowa",
        ["cobblestone"] = "kocie łby",
        ["unhewn_cobblestone"] = "kocie łby",
        ["cobblestone:flattened"] = "kostka brukowa",
        ["paving_stones"] = "płyty chodnikowe / kostka",
        ["asphalt"] = "asfalt",
        ["concrete"] = "beton",
        ["concrete:plates"] = "płyty betonowe",
        ["stone"] = "kamień",
        ["gravel"] = "żwir",
        ["fine_gravel"] = "drobny żwir",
        ["compacted"] = "utwardzona ziemia",
        ["dirt"] = "ziemia",
        ["ground"] = "grunt",
        ["earth"] = "ziemia",
        ["grass"] = "trawa",
        ["sand"] = "piasek",
        ["mud"] = "błoto",
        ["wood"] = "drewno",
        ["metal"] = "metal",
        ["bricks"] = "cegła",
        ["grass_paver"] = "płyty ażurowe",
        ["unpaved"] = "nieutwardzona",
        ["paved"] = "utwardzona"
    };

    private static readonly Dictionary<string, string> SurfaceClasses = new()
    {
        ["paved_smooth"] = "równa, utwardzona",
        ["paved_rough"] = "nierówna (bruk, kostka)",
        ["unpaved"] = "nieutwardzona"
    };

    public static IReadOnlyList<AccessFactDto> For(AccessFeature f)
    {
        var a = f.Attributes;
        var facts = new List<AccessFactDto>();
        switch (f.Kind)
        {
            case AccessKind.Steps:
                facts.Add(Fact("step_count", a.StepCount?.ToString(CultureInfo.InvariantCulture), a.StepCount?.ToString(CultureInfo.InvariantCulture)));
                facts.Add(RampFact(a));
                facts.Add(YesNoFact("handrail", a.Handrail));
                if (a.InclinePercent is not null) facts.Add(InclineFact(a));
                break;
            case AccessKind.Kerb:
                facts.Add(Fact("kerb", a.Kerb, a.Kerb switch { "raised" => "wysoki", "lowered" => "obniżony", "flush" => "zrównany z jezdnią", _ => null }));
                facts.Add(YesNoFact("tactile", a.Tactile));
                break;
            case AccessKind.Elevator:
                facts.Add(WheelchairFact(a.Wheelchair));
                if (a.WidthMeters is not null) facts.Add(WidthFact(a));
                break;
            case AccessKind.Entrance:
                facts.Add(WheelchairFact(a.Wheelchair));
                facts.Add(Fact("step_count", a.StepCount?.ToString(CultureInfo.InvariantCulture), a.StepCount switch { 0 => "bez stopni", { } n => n.ToString(CultureInfo.InvariantCulture), null => null }));
                facts.Add(RampFact(a));
                facts.Add(WidthFact(a));
                facts.Add(Fact("door", a.Door, a.Door switch { "automatic" => "automatyczne", "hinged" => "otwierane ręcznie", "sliding" => "przesuwne", "revolving" => "obrotowe", "no" => "brak drzwi", { } d => d, null => null }));
                break;
            case AccessKind.Place:
                facts.Add(WheelchairFact(a.Wheelchair));
                facts.Add(Fact("toilets_wheelchair", a.ToiletsWheelchair, WheelchairText(a.ToiletsWheelchair)));
                if (a.Category is not null) facts.Add(Fact("category", a.Category, a.Category));
                if (a.Description is not null) facts.Add(Fact("description", a.Description, a.Description));
                break;
            case AccessKind.Toilets:
                facts.Add(WheelchairFact(a.Wheelchair ?? a.ToiletsWheelchair));
                facts.Add(YesNoFact("changing_table", a.ChangingTable));
                break;
            case AccessKind.Bench:
                facts.Add(YesNoFact("backrest", a.Backrest));
                break;
            case AccessKind.TactilePaving:
                facts.Add(YesNoFact("tactile", a.Tactile));
                facts.Add(Fact("kerb", a.Kerb, a.Kerb switch { "raised" => "wysoki", "lowered" => "obniżony", "flush" => "zrównany z jezdnią", _ => null }));
                break;
            case AccessKind.Path:
                facts.Add(SurfaceFact(a));
                if (a.Smoothness is not null) facts.Add(Fact("smoothness", a.Smoothness, a.Smoothness));
                facts.Add(InclineFact(a));
                if (a.WidthMeters is not null) facts.Add(WidthFact(a));
                break;
        }

        return facts;
    }

    public static string SurfaceName(string? raw) => raw is not null && Surfaces.TryGetValue(raw, out var name) ? name : raw ?? UnknownText;

    private static AccessFactDto SurfaceFact(AccessAttributes a)
    {
        if (a.SurfaceClass is null) return Fact("surface", null, null);
        var text = a.SurfaceRaw is not null && Surfaces.ContainsKey(a.SurfaceRaw) ? Surfaces[a.SurfaceRaw] : SurfaceClasses[a.SurfaceClass];
        return Fact("surface", a.SurfaceClass, $"{text} ({SurfaceClasses[a.SurfaceClass]})");
    }

    private static AccessFactDto InclineFact(AccessAttributes a) =>
        Fact("incline", a.InclinePercent?.ToString("0.#", CultureInfo.InvariantCulture), a.InclinePercent is null ? null : Pl(a.InclinePercent.Value, "0.#") + "%");

    private static AccessFactDto WidthFact(AccessAttributes a) =>
        Fact("width", a.WidthMeters?.ToString("0.##", CultureInfo.InvariantCulture), a.WidthMeters is null ? null : Pl(a.WidthMeters.Value, "0.##") + " m");

    private static AccessFactDto RampFact(AccessAttributes a) => a.Ramp switch
    {
        "wheelchair" => Fact("ramp", "wheelchair", "jest, dla wózków inwalidzkich"),
        "stroller" => Fact("ramp", "stroller", "jest, dla wózków dziecięcych"),
        "yes" => Fact("ramp", "yes", "jest (typ nieznany)"),
        _ when a.RampNone => Fact("ramp", "no", "brak"),
        _ => Fact("ramp", null, null)
    };

    private static AccessFactDto WheelchairFact(string? value) => Fact("wheelchair", value, WheelchairText(value));

    private static string? WheelchairText(string? value) => value switch { "yes" => "tak", "limited" => "ograniczony", "no" => "nie", _ => null };

    private static AccessFactDto YesNoFact(string key, bool? value) =>
        Fact(key, value switch { true => "yes", false => "no", null => null }, value switch { true => "tak", false => "nie", null => null });

    /// <summary>A number with a decimal comma (Polish), without depending on the server's culture data.</summary>
    public static string Pl(double value, string format = "0.#") => value.ToString(format, CultureInfo.InvariantCulture).Replace('.', ',');

    private static AccessFactDto Fact(string key, string? value, string? text) =>
        new(key, Labels.GetValueOrDefault(key, key), value ?? UnknownValue, value is null ? UnknownText : text ?? value);
}
