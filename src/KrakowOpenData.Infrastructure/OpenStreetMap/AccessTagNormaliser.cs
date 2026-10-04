using System.Globalization;
using System.Text.RegularExpressions;

namespace KrakowOpenData.Infrastructure.OpenStreetMap;

/// <summary>
/// Pure functions that turn raw OpenStreetMap tag values into normalised accessibility values
/// (ported from "Kraków bez barier" data/normalise.py). Rule for all of them: never invent a value.
/// Unknown, unparsable or contradictory input gives null.
/// </summary>
public static partial class AccessTagNormaliser
{
    private static readonly HashSet<string> Paved =
    [
        "asphalt", "concrete", "paved", "paving_stones", "concrete:plates", "metal", "wood", "tartan", "rubber", "bricks", "brick",
        "metal_grid", "clay", "acrylic", "chipseal", "paving_stones:30", "paving_stones:20", "concrete:lanes_smooth"
    ];

    private static readonly HashSet<string> Rough =
    [
        "cobblestone", "sett", "unhewn_cobblestone", "paving_stones:lanes", "concrete:lanes", "cobblestone:flattened", "stone",
        "grass_paver", "pebblestone_paved"
    ];

    private static readonly HashSet<string> Unpaved =
    [
        "gravel", "fine_gravel", "compacted", "dirt", "grass", "sand", "ground", "earth", "mud", "unpaved", "pebblestone", "woodchips",
        "salt", "snow", "ice", "rock", "dirt/sand"
    ];

    private static readonly string[] SmoothnessOrder =
        ["excellent", "good", "intermediate", "bad", "very_bad", "horrible", "very_horrible", "impassable"];

    private static IEnumerable<string> Tokens(string? value) =>
        (value ?? string.Empty).Split(';').Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0);

    /// <summary>Normalised smoothness (worst of several values) or null.</summary>
    public static string? Smoothness(string? raw)
    {
        var known = Tokens(raw).Where(t => SmoothnessOrder.Contains(t)).ToList();
        return known.Count == 0 ? null : known.MaxBy(t => Array.IndexOf(SmoothnessOrder, t));
    }

    /// <summary>
    /// "paved_smooth" | "paved_rough" | "unpaved" | null. Unpaved wins over everything; a sett-like surface or a paved one with
    /// smoothness "intermediate" or worse is rough; an unknown surface value gives null (we do not guess).
    /// </summary>
    public static string? SurfaceClass(string? surface, string? smoothness = null)
    {
        var classes = new List<string>();
        foreach (var t in Tokens(surface))
        {
            if (Unpaved.Contains(t)) classes.Add("unpaved");
            else if (Rough.Contains(t)) classes.Add("paved_rough");
            else if (Paved.Contains(t)) classes.Add("paved_smooth");
            else return null;
        }

        if (classes.Count == 0) return null;
        if (classes.Contains("unpaved")) return "unpaved";
        if (classes.Contains("paved_rough")) return "paved_rough";
        var sm = Smoothness(smoothness);
        return sm is not null && Array.IndexOf(SmoothnessOrder, sm) >= 2 ? "paved_rough" : "paved_smooth";
    }

    [GeneratedRegex(@"^([-+]?\d+(?:[.,]\d+)?)(%|°|deg)?$")]
    private static partial Regex InclineRegex();

    [GeneratedRegex(@"^([-+]?\d+(?:[.,]\d+)?)(m|cm)?$")]
    private static partial Regex WidthRegex();

    /// <summary>Absolute slope in percent, or null. "3%", "-4 %", "3.5", "2°" are numbers; "up", "down", "yes" are not.</summary>
    public static double? InclinePercent(string? raw)
    {
        if (raw is null) return null;
        var m = InclineRegex().Match(raw.Trim().ToLowerInvariant().Replace(" ", string.Empty));
        if (!m.Success) return null;
        var v = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        if (m.Groups[2].Value is "°" or "deg")
        {
            if (Math.Abs(v) >= 90) return null;
            v = Math.Tan(v * Math.PI / 180) * 100;
        }

        if (Math.Abs(v) > 100) return null;
        return Math.Round(Math.Abs(v), 1);
    }

    /// <summary>Width in metres, or null. Accepts "1.5", "1,5", "1.5 m", "90 cm".</summary>
    public static double? WidthMeters(string? raw)
    {
        if (raw is null) return null;
        var m = WidthRegex().Match(raw.Trim().ToLowerInvariant().Replace(" ", string.Empty));
        if (!m.Success) return null;
        var v = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        if (m.Groups[2].Value == "cm") v /= 100;
        return v is > 0 and <= 30 ? Math.Round(v, 2) : null;
    }

    /// <summary>"yes" | "limited" | "no" | null; "designated" means built for wheelchair users → "yes".</summary>
    public static string? Wheelchair(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "yes" or "designated" => "yes",
        "limited" => "limited",
        "no" => "no",
        _ => null
    };

    /// <summary>"flush" | "lowered" | "raised" | null. "rolled" counts as lowered, kerb=no as flush.</summary>
    public static string? Kerb(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "flush" or "no" => "flush",
        "lowered" or "rolled" => "lowered",
        "raised" => "raised",
        _ => null
    };

    /// <summary>yes → true, no → false, anything else → null.</summary>
    public static bool? YesNo(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "yes" => true,
        "no" => false,
        _ => null
    };

    /// <summary>A small non-negative whole number (step count), or null.</summary>
    public static int? Count(string? raw) =>
        int.TryParse(raw?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n is >= 0 and < 1000 ? n : null;

    /// <summary>
    /// The ramp from ramp:wheelchair, ramp:stroller and ramp: "wheelchair" | "stroller" | "yes" | null, plus whether the tags
    /// explicitly say there is no ramp.
    /// </summary>
    public static (string? Ramp, bool None) Ramp(Func<string, string?> tag)
    {
        string? Get(string key) => tag(key)?.Trim().ToLowerInvariant();
        var ramp = Get("ramp:wheelchair") == "yes" ? "wheelchair"
            : Get("ramp:stroller") == "yes" ? "stroller"
            : Get("ramp") == "yes" ? "yes"
            : null;
        var given = new[] { "ramp", "ramp:wheelchair", "ramp:stroller" }.Select(Get).Where(v => v is not null).ToList();
        var none = ramp is null && given.Count > 0 && given.All(v => v == "no");
        return (ramp, none);
    }

    /// <summary>Tactile paving: yes/no; "incorrect" and "partial" count as present but flawed (false is only "no").</summary>
    public static bool? Tactile(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "yes" or "incorrect" or "partial" => true,
        "no" => false,
        _ => null
    };
}
