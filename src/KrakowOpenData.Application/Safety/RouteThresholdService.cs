using KrakowOpenData.Contracts;

namespace KrakowOpenData.Application.Safety;

/// <summary>"Good enough" limits for the fastest walking route: below either number a safer alternative is searched for (see <see cref="RouteService"/>).</summary>
/// <param name="Average">The route's average score (0-100, higher is better) must be at least this.</param>
/// <param name="Worst">The route's weakest 50 m stretch must score at least this.</param>
public sealed record RouteThreshold(double Average, double Worst);

/// <summary>The defaults, the allowed range and the written reason for each layer's route thresholds.</summary>
public static class RouteThresholds
{
    public const double Min = 20;
    public const double Max = 95;
    public const double DefaultAverage = 65;
    public const double DefaultWorst = 45;

    public static readonly RouteThreshold Default = new(DefaultAverage, DefaultWorst);

    public static string Title(Layer layer) => layer switch
    {
        Layer.Safety => "Night safety",
        Layer.Heat => "Heat relief",
        Layer.Flood => "Flood",
        Layer.Air => "Air quality",
        Layer.Access => "Accessibility: wheelchair",
        Layer.AccessPram => "Accessibility: pram",
        _ => "Accessibility: limited mobility"
    };

    /// <summary>Why the default is what it is, in one or two sentences, for the planner.</summary>
    public static string Reason(Layer layer) => layer switch
    {
        Layer.Safety =>
            "At night one dark, empty stretch matters even when the rest is well lit, so the weakest stretch counts as well as the average. 65 on average is a street with working lamps and some open places; below 45 anywhere is a stretch where a walker is effectively alone in the dark.",
        Layer.Heat =>
            "Heat relief adds up over the whole walk (shade, water, places to sit and cool down), so the average carries the decision. 65 on average means most of the walk is covered; 45 for the worst stretch tolerates one short exposed section, which a walker can cross quickly.",
        Layer.Flood =>
            "Flood risk is local: one low stretch beside a river or an underpass can stop a walk entirely, so the worst stretch is as important as the average. The default is deliberately not relaxed; raise the worst-stretch limit if the city wants flood-prone stretches avoided more strictly.",
        Layer.Access =>
            "For a wheelchair one barrier can stop the walk (a flight of steps, a high kerb, a blocked path), so the weakest stretch counts as much as the average. The default is not relaxed: 65 on average means most of the route is step-free and smooth; below 45 anywhere is a stretch with a barrier that a wheelchair cannot pass.",
        Layer.AccessPram =>
            "A pram copes with some kerbs and a ramp tagged for strollers, but rough surface and steps hurt over the whole walk, so the average carries the decision. 65 on average is a mostly smooth, ramped route; 45 for the worst stretch tolerates one awkward spot that can be crossed with a lift of the pram.",
        Layer.AccessMobility =>
            "For people who walk with difficulty the length of the walk and the lack of places to rest matter more than a single step, so the average decides. 65 on average means gentle slopes and benches along the way; 45 for the worst stretch tolerates a short steep or rough section.",
        _ =>
            "Air quality is mostly felt as exposure over the whole walk, with main roads adding to it, so the average decides. 65 on average keeps a walker mostly away from traffic; 45 for the worst stretch tolerates crossing or walking a short way along a busy road."
    };

    public static string Key(Layer layer) => layer switch
    {
        Layer.Safety => "night",
        Layer.Heat => "heat",
        Layer.Flood => "flood",
        Layer.Air => "air",
        Layer.Access => "access",
        Layer.AccessPram => "accessPram",
        _ => "accessMobility"
    };

    /// <summary>The layer a planning mode is judged on; "both" has no layer of its own.</summary>
    public static Layer? LayerOf(PlanningEvent mode) => mode switch
    {
        PlanningEvent.Night => Layer.Safety,
        PlanningEvent.Heat => Layer.Heat,
        PlanningEvent.Flood => Layer.Flood,
        PlanningEvent.Air => Layer.Air,
        PlanningEvent.Access => Layer.Access,
        PlanningEvent.AccessPram => Layer.AccessPram,
        PlanningEvent.AccessMobility => Layer.AccessMobility,
        _ => null
    };

    public static string StoreKey(Layer layer, bool average) => $"{Key(layer)}.{(average ? "average" : "worst")}";
}

/// <summary>
/// Lets a planner decide, per safety measure, how good the fastest route has to be before it is shown without a safer alternative.
/// Like the factor weights, a change is shared by everyone until it is reset, and is kept in <see cref="ISafetyStore"/>.
/// </summary>
public sealed class RouteThresholdService(ISafetyStore store)
{
    /// <summary>The thresholds in use for a mode (the defaults unless a planner has changed them). Read on every request, so a change applies at once.</summary>
    public async Task<RouteThreshold> ForAsync(PlanningEvent mode, CancellationToken ct = default)
    {
        if (RouteThresholds.LayerOf(mode) is not { } layer) return RouteThresholds.Default;
        return Resolve(layer, await store.GetRouteThresholdOverridesAsync(ct));
    }

    public async Task<RouteThresholdsDto> GetAsync(CancellationToken ct = default) =>
        Describe(await store.GetRouteThresholdOverridesAsync(ct));

    public async Task<RouteThresholdsDto> SetAsync(SetRouteThresholdsRequest request, CancellationToken ct = default)
    {
        var current = new Dictionary<string, double>(await store.GetRouteThresholdOverridesAsync(ct), StringComparer.Ordinal);

        foreach (var (name, value) in request.Thresholds)
        {
            if (!Enum.TryParse<Layer>(name, true, out var layer) || !Enum.IsDefined(layer))
                throw new SafetyValidationException(name, $"Unknown measure '{name}'.");
            if (!Valid(value.Average) || !Valid(value.Worst))
                throw new SafetyValidationException(name, $"Thresholds must be numbers from {RouteThresholds.Min:0} to {RouteThresholds.Max:0}.");
            if (value.Worst > value.Average)
                throw new SafetyValidationException(name, "The weakest-stretch limit cannot be higher than the average limit.");

            SetOrClear(current, RouteThresholds.StoreKey(layer, true), value.Average, RouteThresholds.DefaultAverage);
            SetOrClear(current, RouteThresholds.StoreKey(layer, false), value.Worst, RouteThresholds.DefaultWorst);
        }

        await store.SaveRouteThresholdOverridesAsync(current, ct);
        return Describe(current);
    }

    public async Task<RouteThresholdsDto> ResetAsync(CancellationToken ct = default)
    {
        await store.SaveRouteThresholdOverridesAsync(new Dictionary<string, double>(), ct);
        return Describe(new Dictionary<string, double>());
    }

    private static bool Valid(double v) => !double.IsNaN(v) && !double.IsInfinity(v) && v >= RouteThresholds.Min && v <= RouteThresholds.Max;

    private static void SetOrClear(Dictionary<string, double> map, string key, double value, double def)
    {
        if (Math.Abs(value - def) < 0.0001) map.Remove(key); else map[key] = Math.Round(value, 1);
    }

    private static RouteThreshold Resolve(Layer layer, IReadOnlyDictionary<string, double> overrides)
    {
        var average = overrides.TryGetValue(RouteThresholds.StoreKey(layer, true), out var a) ? a : RouteThresholds.DefaultAverage;
        var worst = overrides.TryGetValue(RouteThresholds.StoreKey(layer, false), out var w) ? w : RouteThresholds.DefaultWorst;
        // A saved pair could break "worst <= average" when only one side was customised and the other is a default: keep the rule true.
        return new RouteThreshold(average, Math.Min(worst, average));
    }

    private static RouteThresholdsDto Describe(IReadOnlyDictionary<string, double> overrides)
    {
        var layers = new[] { Layer.Safety, Layer.Heat, Layer.Flood, Layer.Air, Layer.Access, Layer.AccessPram, Layer.AccessMobility }.Select(layer =>
        {
            var t = Resolve(layer, overrides);
            var customized = overrides.ContainsKey(RouteThresholds.StoreKey(layer, true)) || overrides.ContainsKey(RouteThresholds.StoreKey(layer, false));
            return new LayerRouteThresholdDto(layer.ToString(), RouteThresholds.Title(layer), t.Average, t.Worst,
                RouteThresholds.DefaultAverage, RouteThresholds.DefaultWorst, customized, RouteThresholds.Reason(layer));
        }).ToList();
        return new RouteThresholdsDto(layers, layers.Any(l => l.Customized), RouteThresholds.Min, RouteThresholds.Max);
    }
}
