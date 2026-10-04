using KrakowOpenData.Application.Accessibility;
using KrakowOpenData.Domain.Accessibility;
using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Application.Safety;

/// <summary>
/// The ACCESSIBILITY layer ("Dostępność / Kraków bez barier") as a fifth score layer. There is one score per
/// <see cref="AccessProfile"/> (wheelchair, pram, limited mobility); each profile is a layer of its own
/// (<see cref="Layer.Access"/> = wheelchair, the default, <see cref="Layer.AccessPram"/>, <see cref="Layer.AccessMobility"/>) with its own
/// factor weights, so planners can tune each one, and its own route thresholds.
///
/// <para>Factor keys are <c>access.{profile}.{factor}</c>, e.g. <c>access.pram.surface</c>. The factor (base) names are
/// steps, kerbs, surface, slope, stepFree, accessStops, accessToilets, rest, tactile. See <see cref="AccessKeys"/>.</para>
///
/// <para><b>No data is never "accessible".</b> Accessibility data (OpenStreetMap) is downloaded for a box
/// (<c>KrakowData:AccessArea</c>, default central Kraków). A square outside the box, or inside it with nothing mapped within
/// <see cref="AccessLayerData.CoverageRadius"/> metres, has no accessibility score at all (null / -1): it is flagged, left out of the planner
/// averages and never scored good or critical.</para>
/// </summary>
public static class AccessKeys
{
    public static readonly string[] Bases = ["steps", "kerbs", "surface", "slope", "stepFree", "accessStops", "accessToilets", "rest", "tactile"];

    public static bool IsAccessLayer(Layer layer) => layer is Layer.Access or Layer.AccessPram or Layer.AccessMobility;

    public static AccessProfile? ProfileOf(Layer layer) => layer switch
    {
        Layer.Access => AccessProfile.Wheelchair,
        Layer.AccessPram => AccessProfile.Pram,
        Layer.AccessMobility => AccessProfile.Mobility,
        _ => null
    };

    public static Layer LayerOf(AccessProfile profile) => profile.Key switch
    {
        "pram" => Layer.AccessPram,
        "mobility" => Layer.AccessMobility,
        _ => Layer.Access
    };

    public static string Make(AccessProfile profile, string @base) => $"access.{profile.Key}.{@base}";

    /// <summary>"access.pram.surface" gives "surface"; any other key is returned unchanged.</summary>
    public static string Base(string key) => key.StartsWith("access.", StringComparison.Ordinal) && key.LastIndexOf('.') is var i and > 6 ? key[(i + 1)..] : key;

    public static bool IsAccessKey(string key) => key.StartsWith("access.", StringComparison.Ordinal);
}

/// <summary>Default factor definitions (weights add up to 100 per profile) and the numbers behind them.</summary>
public static class AccessFactorSets
{
    // base → (kind, full, zero). Count factors: full at 0, zero at the given count of weighted barriers within 150 m.
    private static (FactorKind Kind, double Full, double Zero) Shape(string @base, AccessProfile p) => @base switch
    {
        "steps" => (FactorKind.Count, 0, 8),
        "kerbs" => (FactorKind.Count, 0, 10),
        "surface" => (FactorKind.Share, 70, 15),
        "slope" => (FactorKind.Count, 0, 4),
        "stepFree" => (FactorKind.Distance, 100, 500),
        "accessStops" => (FactorKind.Distance, 150, 800),
        "accessToilets" => (FactorKind.Distance, 300, 1200),
        "rest" => p.Key == "mobility" ? (FactorKind.Distance, 80, 300) : (FactorKind.Distance, 100, 400),
        _ => (FactorKind.Distance, 50, 300)   // tactile
    };

    /// <summary>Default weights per profile, in the order of <see cref="AccessKeys.Bases"/>. Each row adds up to 100.</summary>
    public static readonly IReadOnlyDictionary<string, double[]> Weights = new Dictionary<string, double[]>
    {
        //                steps kerbs surface slope stepFree stops toilets rest tactile
        ["wheelchair"] = [22, 20, 14, 8, 10, 10, 10, 6, 0],
        ["pram"] = [15, 15, 20, 10, 8, 12, 8, 12, 0],
        ["mobility"] = [10, 5, 12, 18, 8, 12, 8, 22, 5]
    };

    public static IReadOnlyList<FactorDefinition> For(Layer layer)
    {
        var profile = AccessKeys.ProfileOf(layer) ?? AccessProfile.Wheelchair;
        var weights = Weights[profile.Key];
        return AccessKeys.Bases.Select((b, i) =>
        {
            var (kind, full, zero) = Shape(b, profile);
            return new FactorDefinition(AccessKeys.Make(profile, b), layer, weights[i], kind, full, zero);
        }).ToList();
    }
}

/// <summary>What was found around a point for one profile (before scoring).</summary>
public sealed record AccessProfileReading(double Steps, double Kerbs, double? SurfaceShare, int FootwaySegments, double Steep);

/// <summary>The accessibility reading of a point: why it has no data (null code = it has data) and the per-profile numbers.</summary>
public sealed record AccessReading(string? NoDataCode, int ItemsNearby, IReadOnlyDictionary<string, AccessProfileReading> Profiles);

/// <summary>
/// The accessibility dataset prepared for scoring: an <see cref="AccessIndex"/> over every mapped item, the "coverage" test and the
/// per-profile counts. Built with the safety model; the planner-visible numbers all come from here.
/// </summary>
public sealed class AccessLayerData
{
    /// <summary>Barriers and path stretches are counted within this many metres of the point (a 250 m square has a half-diagonal of 177 m).</summary>
    public const double BarrierRadius = 150;

    /// <summary>A point has "data" when at least one item of any kind is mapped within this distance and it lies inside the download area.</summary>
    public const double CoverageRadius = 250;

    /// <summary>Below this many mapped items within <see cref="CoverageRadius"/> the score is given but flagged as thin.</summary>
    public const int ThinItems = 5;

    private static readonly HashSet<string> Footways = ["footway", "pedestrian", "path", "living_street", "cycleway"];

    public AccessLayerData(IReadOnlyList<AccessFeature> features)
    {
        Index = new AccessIndex(features);
        Features = features;
    }

    public AccessIndex Index { get; }

    public IReadOnlyList<AccessFeature> Features { get; }

    public bool IsEmpty => Features.Count == 0;

    public int FeatureCount(string @base) => @base switch
    {
        "steps" => Features.Count(f => f.Kind == AccessKind.Steps),
        "kerbs" => Features.Count(f => f.Kind == AccessKind.Kerb),
        "surface" or "slope" => Features.Count(f => f.Kind == AccessKind.Path),
        _ => 0
    };

    public AccessReading Read(GeoPoint point)
    {
        var profiles = AccessProfile.All.ToDictionary(p => p.Key, _ => new AccessProfileReading(0, 0, null, 0, 0));
        if (IsEmpty) return new AccessReading("unavailable", 0, profiles);
        if (!Index.Covers(point)) return new AccessReading("outside_area", 0, profiles);

        var items = Index.Near(point, CoverageRadius).ToList();
        if (items.Count == 0) return new AccessReading("no_data", 0, profiles);

        var near = items.Where(x => x.Distance <= BarrierRadius).Select(x => x.Feature).ToList();
        foreach (var p in AccessProfile.All)
        {
            double steps = 0, kerbs = 0, steep = 0;
            var footways = 0;
            var smooth = 0;
            foreach (var f in near)
            {
                var s = AccessRules.Status(f.Kind, f.Attributes, p);
                switch (f.Kind)
                {
                    case AccessKind.Steps:
                        steps += Weight(s);
                        break;
                    case AccessKind.Entrance when f.Attributes.Wheelchair is null:
                        steps += Weight(s);   // steps at an entrance with no ramp
                        break;
                    case AccessKind.Kerb:
                        kerbs += Weight(s);
                        break;
                    case AccessKind.Path when Footways.Contains(f.Attributes.Highway ?? ""):
                        footways++;
                        if (s == AccessStatus.Yes) smooth++;
                        if (f.Attributes.InclinePercent is { } i && i > p.MaxInclinePercent) steep++;
                        break;
                }
            }

            profiles[p.Key] = new AccessProfileReading(steps, kerbs, footways == 0 ? null : 100.0 * smooth / footways, footways, steep);
        }

        return new AccessReading(null, items.Count, profiles);
    }

    private static double Weight(AccessStatus s) => s switch { AccessStatus.No => 1, AccessStatus.Limited => 0.5, _ => 0 };

    /// <summary>The mapped features behind the distance factors of a profile, keyed by the full factor key.</summary>
    public IEnumerable<KeyValuePair<string, IReadOnlyList<Feature>>> FeatureLists(AccessProfile p, IReadOnlyList<Feature> accessibleStops)
    {
        IReadOnlyList<Feature> Pick(Func<AccessFeature, bool> filter) => Features.Where(filter)
            .Select(f => new Feature(f.Id, f.Kind.ToString(), f.Name, f.Location, 0, null, "yes")).ToList();

        bool Yes(AccessFeature f) => AccessRules.Status(f.Kind, f.Attributes, p) == AccessStatus.Yes;

        yield return new(AccessKeys.Make(p, "stepFree"), Pick(f => f.Kind is AccessKind.Entrance or AccessKind.Elevator or AccessKind.Place && Yes(f)));
        yield return new(AccessKeys.Make(p, "accessToilets"), Pick(f => f.Kind == AccessKind.Toilets && Yes(f)));
        yield return new(AccessKeys.Make(p, "rest"), Pick(f => f.Kind == AccessKind.Bench));
        yield return new(AccessKeys.Make(p, "tactile"), Pick(f => f.Kind == AccessKind.TactilePaving && Yes(f)));
        yield return new(AccessKeys.Make(p, "accessStops"), accessibleStops);
    }

    public AccessLayerInfo Info()
    {
        var b = Index.Bounds;
        return new AccessLayerInfo(!IsEmpty, b is null ? null : (Math.Round(b.Value.MinLat, 5), Math.Round(b.Value.MinLon, 5), Math.Round(b.Value.MaxLat, 5), Math.Round(b.Value.MaxLon, 5)));
    }
}

public sealed record AccessLayerInfo(bool HasData, (double MinLat, double MinLon, double MaxLat, double MaxLon)? Area);
