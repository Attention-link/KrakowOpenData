using KrakowOpenData.Contracts;

namespace KrakowOpenData.Application.Safety;

/// <summary>
/// Lets a planner change how much each factor counts in a score, and says why each factor has its weight.
///
/// <para>Weights are shared: a change applies to every score, for residents and planners alike, until it is reset. The numbers a planner
/// types are scaled so each layer adds up to 100 (see <see cref="FactorWeights"/>). Factors can be set to 0 to leave them out. The reasons
/// shown are the reasons for the <i>default</i> weights (from <see cref="MethodService"/>): they tell a planner what a factor stands for
/// and what is lost by turning it down.</para>
/// </summary>
public sealed class WeightService(MethodService method, SafetyModelProvider models, ISafetyStore store)
{
    public async Task<WeightsDto> GetAsync(CancellationToken ct = default)
    {
        var described = await method.GetAsync(ct);
        var overrides = await store.GetWeightOverridesAsync(ct);

        var layers = described.Layers.Select(l =>
        {
            var layer = Enum.Parse<Layer>(l.Layer);
            var factors = l.Factors
                .Select(f => new FactorWeightDto(f.Key, f.Label, f.Weight, f.DefaultWeight ?? f.Weight, f.Measures, f.WhyWeighted, f.Source, f.DataCaveat))
                .ToList();
            var customized = SafetyModel.FactorsOf(layer).Any(d => overrides.ContainsKey(d.Key));
            return new LayerWeightsDto(l.Layer, l.Title, customized, factors);
        }).ToList();

        return new WeightsDto(layers, layers.Any(l => l.Customized));
    }

    public async Task<WeightsDto> SetAsync(SetWeightsRequest request, CancellationToken ct = default)
    {
        var known = Enum.GetValues<Layer>().SelectMany(l => SafetyModel.FactorsOf(l)).ToDictionary(d => d.Key, d => d.Layer);

        foreach (var (key, value) in request.Weights)
        {
            if (!known.ContainsKey(key)) throw new SafetyValidationException(key, $"Unknown factor '{key}'.");
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 1000)
                throw new SafetyValidationException(key, "A weight must be a number from 0 to 1000.");
        }

        // What is saved: for every layer the request touches, the weights of ALL its factors (typed or default), scaled to 100.
        var saved = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var layer in Enum.GetValues<Layer>())
        {
            var defs = SafetyModel.FactorsOf(layer);
            if (!defs.Any(d => request.Weights.ContainsKey(d.Key))) continue;

            var raw = defs.Select(d => request.Weights.TryGetValue(d.Key, out var w) ? w : d.Weight).ToList();
            if (raw.Sum() <= 0) throw new SafetyValidationException(defs[0].Key, $"At least one {layer} factor must have a weight above zero.");
            var scaled = FactorWeights.Normalise(raw);
            for (var i = 0; i < defs.Count; i++) saved[defs[i].Key] = scaled[i];
        }

        // Layers the request did not mention keep what they had.
        var current = await store.GetWeightOverridesAsync(ct);
        foreach (var (key, value) in current)
        {
            if (!known.TryGetValue(key, out var layer)) continue;
            if (SafetyModel.FactorsOf(layer).Any(d => saved.ContainsKey(d.Key))) continue;
            saved[key] = value;
        }

        // A layer whose weights equal the defaults is not "customized".
        foreach (var layer in Enum.GetValues<Layer>())
        {
            var defs = SafetyModel.FactorsOf(layer);
            if (defs.All(d => !saved.TryGetValue(d.Key, out var w) || Math.Abs(w - d.Weight) < 0.0001))
                foreach (var d in defs) saved.Remove(d.Key);
        }

        await store.SaveWeightOverridesAsync(saved, ct);
        models.Invalidate();
        return await GetAsync(ct);
    }

    public async Task<WeightsDto> ResetAsync(CancellationToken ct = default)
    {
        await store.SaveWeightOverridesAsync(new Dictionary<string, double>(), ct);
        models.Invalidate();
        return await GetAsync(ct);
    }
}
