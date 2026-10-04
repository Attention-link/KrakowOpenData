namespace KrakowOpenData.Application.Safety;

/// <summary>
/// Turns the weights a planner typed into weights that are valid for the model: every layer's weights add up to exactly 100.
/// A planner may type any non-negative numbers (for example 3, 1, 1 for "three times as important"); they are scaled, rounded to whole
/// points and the rounding is settled on the largest remainders so the total is exactly 100.
/// </summary>
public static class FactorWeights
{
    /// <summary>The definitions of a layer with the saved weights applied (the defaults for a factor that has none).</summary>
    public static IReadOnlyList<FactorDefinition> Apply(Layer layer, IReadOnlyDictionary<string, double> overrides)
    {
        var defaults = SafetyModel.FactorsOf(layer);
        if (!defaults.Any(d => overrides.ContainsKey(d.Key))) return defaults;

        var raw = defaults.Select(d => overrides.TryGetValue(d.Key, out var w) ? w : d.Weight).ToList();
        var weights = Normalise(raw);
        return defaults.Select((d, i) => d with { Weight = weights[i] }).ToList();
    }

    /// <summary>Scales <paramref name="raw"/> to whole numbers that add up to 100 (largest remainder method).</summary>
    public static IReadOnlyList<double> Normalise(IReadOnlyList<double> raw)
    {
        var total = raw.Sum();
        if (total <= 0) throw new ArgumentException("At least one weight must be above zero.", nameof(raw));

        var exact = raw.Select(w => w * 100.0 / total).ToList();
        var floors = exact.Select(Math.Floor).ToList();
        var left = 100 - (int)floors.Sum();
        foreach (var i in exact.Select((v, i) => (Remainder: v - Math.Floor(v), i)).OrderByDescending(x => x.Remainder).ThenBy(x => x.i).Take(left))
            floors[i.i] += 1;
        return floors;
    }
}
