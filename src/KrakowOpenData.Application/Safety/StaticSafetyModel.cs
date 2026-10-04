using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Application.Safety;

/// <summary>
/// A mapped thing that scores a factor: a water point, a stop, a park, … <see cref="RadiusMeters"/> is non-zero for parks only.
/// <see cref="Wheelchair"/> is yes | limited | no from OSM (or the GTFS stop flag); null = no data.
/// </summary>
public sealed record Feature(string Id, string Kind, string? Name, GeoPoint Location, double RadiusMeters, string? OpeningHours, string? Wheelchair = null);

/// <summary>
/// One place measured against every factor, before citizen reports are applied: the factor results of both layers,
/// their base scores and the exposure estimate. A grid cell is measured at its centre; a resident's point at the point itself.
/// </summary>
public sealed class PlaceMeasure
{
    public required GeoPoint Point { get; init; }
    public required int Row { get; init; }
    public required int Col { get; init; }
    public required int Lamps3x3 { get; init; }
    public required int Stops3x3 { get; init; }
    public required double Exposure { get; init; }
    public required IReadOnlyList<FactorResult> Heat { get; init; }
    public required IReadOnlyList<FactorResult> Safety { get; init; }
    public required IReadOnlyList<FactorResult> Flood { get; init; }
    public required IReadOnlyList<FactorResult> Air { get; init; }

    /// <summary>Accessibility factors per profile (wheelchair, pram, limited mobility). Meaningful only when <see cref="AccessCovered"/>.</summary>
    public IReadOnlyList<FactorResult> Access { get; init; } = [];

    public IReadOnlyList<FactorResult> AccessPram { get; init; } = [];

    public IReadOnlyList<FactorResult> AccessMobility { get; init; } = [];

    /// <summary>Why there is no accessibility score here: outside_area | no_data | unavailable; null when the place has accessibility data.</summary>
    public string? AccessNoDataCode { get; init; } = "unavailable";

    /// <summary>Mapped accessibility items within <see cref="AccessLayerData.CoverageRadius"/> metres.</summary>
    public int AccessItems { get; init; }

    public bool AccessCovered => AccessNoDataCode is null;

    /// <summary>True when the data is there but thin (few mapped items), so the score is less certain.</summary>
    public bool AccessThin => AccessCovered && AccessItems < AccessLayerData.ThinItems;

    public double AccessBase => SafetyModel.BaseScore(Access);

    public double AccessPramBase => SafetyModel.BaseScore(AccessPram);

    public double AccessMobilityBase => SafetyModel.BaseScore(AccessMobility);

    /// <summary>Base score of an accessibility layer (any of the three profiles).</summary>
    public double AccessBaseOf(Layer layer) => SafetyModel.BaseScore(Factors(layer));

    public FactorResult? Find(string key) =>
        new[] { Heat, Safety, Flood, Air, Access, AccessPram, AccessMobility }.SelectMany(l => l).FirstOrDefault(f => f.Definition.Key == key);

    public string CellId => GridSpec.IdOf(Row, Col);

    public double HeatBase => SafetyModel.BaseScore(Heat);

    public double SafetyBase => SafetyModel.BaseScore(Safety);

    public double FloodBase => SafetyModel.BaseScore(Flood);

    public double AirBase => SafetyModel.BaseScore(Air);

    public IReadOnlyList<FactorResult> Factors(Layer layer) => layer switch
    {
        Layer.Heat => Heat,
        Layer.Safety => Safety,
        Layer.Flood => Flood,
        Layer.Access => Access,
        Layer.AccessPram => AccessPram,
        Layer.AccessMobility => AccessMobility,
        _ => Air
    };
}

/// <summary>
/// The slow-changing part of the model: mapped features, lamp and stop counts per grid cell, and every inhabited
/// cell already measured. Built once and cached (see <see cref="SafetyModelProvider"/>); citizen reports and live
/// weather are applied on top of it per request.
/// </summary>
public sealed class StaticSafetyModel
{
    /// <summary>Area of the 3×3 block of cells used for lamp density, in km² (750 m × 750 m).</summary>
    public const double NeighbourhoodAreaKm2 = 3 * GridSpec.CellSizeMeters * 3 * GridSpec.CellSizeMeters / 1_000_000;

    private readonly IReadOnlyDictionary<string, IReadOnlyList<Feature>> _features;
    private readonly IReadOnlyDictionary<(int Row, int Col), int> _lamps;
    private readonly IReadOnlyDictionary<(int Row, int Col), int> _stops;
    private readonly Dictionary<string, PlaceMeasure> _cellsById;
    private readonly AccessLayerData? _access;
    private readonly IReadOnlyDictionary<Layer, IReadOnlyList<FactorDefinition>> _definitions;

    private static readonly Layer[] Layers = Enum.GetValues<Layer>();

    /// <summary>The factors of a layer with the weights this model was built with (the defaults, or what a planner set).</summary>
    public IReadOnlyList<FactorDefinition> Definitions(Layer layer) => _definitions[layer];

    public StaticSafetyModel(
        IReadOnlyDictionary<string, IReadOnlyList<Feature>> featuresByFactor,
        IReadOnlyDictionary<(int Row, int Col), int> lampsPerCell,
        IReadOnlyDictionary<(int Row, int Col), int> stopsPerCell,
        IReadOnlyList<string> dataGaps,
        DateTimeOffset builtAt,
        IReadOnlyDictionary<Layer, IReadOnlyList<FactorDefinition>>? definitions = null,
        AccessLayerData? access = null)
    {
        _access = access;
        _definitions = definitions ?? Layers.ToDictionary(l => l, SafetyModel.FactorsOf);
        _features = featuresByFactor;
        AccessStopsAvailable = featuresByFactor.Any(kv => AccessKeys.IsAccessKey(kv.Key) && AccessKeys.Base(kv.Key) == "accessStops" && kv.Value.Count > 0);
        _lamps = lampsPerCell;
        _stops = stopsPerCell;
        DataGaps = dataGaps;
        BuiltAt = builtAt;

        // A cell is on the map when it has at least 3 mapped lamps or at least 1 stop in it: that is how we
        // tell built-up land from fields, forest and water, since there are no land-use polygons.
        var keys = lampsPerCell.Where(k => k.Value >= 3).Select(k => k.Key)
            .Concat(stopsPerCell.Keys)
            .Distinct();

        Cells = keys
            .Select(k => Measure(GridSpec.CenterOf(k.Row, k.Col)))
            .OrderBy(c => c.Row).ThenBy(c => c.Col)
            .ToList();
        _cellsById = Cells.ToDictionary(c => c.CellId);
    }

    public IReadOnlyList<PlaceMeasure> Cells { get; }

    public IReadOnlyList<string> DataGaps { get; }

    /// <summary>The accessibility dataset behind the Access layers (null when it could not be loaded).</summary>
    public AccessLayerData? AccessData => _access;

    public DateTimeOffset BuiltAt { get; }

    public PlaceMeasure? FindCell(string id) => _cellsById.GetValueOrDefault(id);

    /// <summary>A human place name for a point: the nearest public transport stop within 600 m, e.g. "Czyżyny Dworzec". Null when none.</summary>
    public string? LabelFor(GeoPoint point)
    {
        var (distance, feature) = Nearest("transit", point);
        return distance is <= 600 ? feature?.Name : null;
    }

    public int FeatureCount(string factorKey) => factorKey switch
    {
        "lighting" => _lamps.Values.Sum(),
        "transit" => _stops.Values.Sum(),
        _ when AccessKeys.IsAccessKey(factorKey) && AccessKeys.Base(factorKey) is "steps" or "kerbs" or "surface" or "slope" =>
            _access?.FeatureCount(AccessKeys.Base(factorKey)) ?? 0,
        _ => _features.TryGetValue(factorKey, out var list) ? list.Count : 0
    };

    /// <summary>The features behind one factor (for offline use in apps).</summary>
    public IReadOnlyList<Feature> FeaturesOf(string factorKey) => _features.TryGetValue(factorKey, out var list) ? list : [];

    /// <summary>Measures any point against all factors (see <see cref="SafetyModel"/>).</summary>
    public PlaceMeasure Measure(GeoPoint point)
    {
        var (row, col) = GridSpec.CellOf(point);
        var lamps = Around(_lamps, row, col);
        var stops = Around(_stops, row, col);
        var lampsPerKm2 = lamps / NeighbourhoodAreaKm2;

        var reading = _access?.Read(point) ?? new AccessReading("unavailable", 0, new Dictionary<string, AccessProfileReading>());

        FactorResult Score(FactorDefinition def)
        {
            // Accessibility counts around the place (steps, kerbs, smooth footways, slopes) come from the accessibility index, per profile.
            // Nothing mapped is "no value" and never scores as accessible (see AccessLayerData).
            if (AccessKeys.IsAccessKey(def.Key) && AccessKeys.Base(def.Key) is "steps" or "kerbs" or "surface" or "slope")
            {
                var profileKey = def.Key.Split('.')[1];
                var r = reading.NoDataCode is null && reading.Profiles.TryGetValue(profileKey, out var pr) ? pr : null;
                double? value = r is null ? null : AccessKeys.Base(def.Key) switch
                {
                    "steps" => r.Steps,
                    "kerbs" => r.Kerbs,
                    "slope" => r.Steep,
                    _ => r.SurfaceShare
                };
                return new FactorResult(def, value is null ? null : Math.Round(value.Value, 1), r is null ? 0 : SafetyModel.FactorScore(def, value), null);
            }

            if (def.Kind == FactorKind.Density)
                return new FactorResult(def, Math.Round(lampsPerKm2, 1), SafetyModel.FactorScore(def, lampsPerKm2), null);

            // A "far is good" factor with no mapped line at all (dataset missing) must not read as perfect: it is unknown, scored 50.
            if (def.Kind == FactorKind.DistanceAway && !(_features.TryGetValue(def.Key, out var any) && any.Count > 0))
                return new FactorResult(def, null, 50, null);

            var (distance, nearest) = Nearest(def.Key, point);
            return new FactorResult(def, distance is null ? null : Math.Round(distance.Value), SafetyModel.FactorScore(def, distance), nearest?.Name);
        }

        return new PlaceMeasure
        {
            Point = point,
            Row = row,
            Col = col,
            Lamps3x3 = lamps,
            Stops3x3 = stops,
            Exposure = SafetyModel.Exposure(lamps, stops),
            Heat = Definitions(Layer.Heat).Select(Score).ToList(),
            Safety = Definitions(Layer.Safety).Select(Score).ToList(),
            Flood = Definitions(Layer.Flood).Select(Score).ToList(),
            Air = Definitions(Layer.Air).Select(Score).ToList(),
            Access = Rescaled(Definitions(Layer.Access).Select(Score).ToList()),
            AccessPram = Rescaled(Definitions(Layer.AccessPram).Select(Score).ToList()),
            AccessMobility = Rescaled(Definitions(Layer.AccessMobility).Select(Score).ToList()),
            AccessNoDataCode = reading.NoDataCode,
            AccessItems = reading.ItemsNearby
        };
    }

    /// <summary>Why the <c>accessStops</c> factor is not available (shown as the factor's data note).</summary>
    public const string AccessStopsNote = "no stop in the ZTP open data is flagged wheelchair-accessible";

    /// <summary>The data-gap code reported in the conditions while <see cref="AccessStopsAvailable"/> is false.</summary>
    public const string AccessStopsGap = "access_stops";

    /// <summary>
    /// True when at least one stop is flagged wheelchair-accessible in the ZTP open data. When none is (the feeds currently flag none, or the stops dataset
    /// is unavailable) the <c>accessStops</c> factor is NOT AVAILABLE: scoring leaves it out and rescales the other weights of the profile (see <see cref="Rescaled"/>).
    /// </summary>
    public bool AccessStopsAvailable { get; }

    /// <summary>
    /// When accessible stops are unavailable: the stops factor becomes "no data" (no points, never weak) and the other factors' weights are scaled up so they
    /// add up to what all weights did (100 by default). Done at scoring time only; the configured and planner weights (<see cref="Definitions"/>) are untouched.
    /// </summary>
    private IReadOnlyList<FactorResult> Rescaled(List<FactorResult> factors)
    {
        if (AccessStopsAvailable) return factors;
        var stops = factors.FirstOrDefault(f => AccessKeys.Base(f.Definition.Key) == "accessStops");
        if (stops is null) return factors;
        var total = factors.Sum(f => f.Definition.Weight);
        var remaining = total - stops.Definition.Weight;
        var scale = remaining > 0 ? total / remaining : 1;
        return factors.Select(f => f == stops
            ? new FactorResult(f.Definition, null, 0, null, false)
            : f with { Definition = f.Definition with { Weight = f.Definition.Weight * scale } }).ToList();
    }

    /// <summary>
    /// The nearest feature for a factor and its distance in metres (to a park's edge for parks), or
    /// <c>(null, null)</c> when none lies within <see cref="SafetyModel.SearchRadiusMeters"/>.
    /// </summary>
    public (double? Distance, Feature? Feature) Nearest(string factorKey, GeoPoint point)
    {
        if (!_features.TryGetValue(factorKey, out var list)) return (null, null);

        double best = double.MaxValue;
        Feature? bestFeature = null;
        foreach (var f in list)
        {
            var d = Math.Max(0, GridSpec.Distance(point, f.Location) - f.RadiusMeters);
            if (d < best)
            {
                best = d;
                bestFeature = f;
            }
        }

        return best <= SafetyModel.SearchRadiusMeters ? (best, bestFeature) : (null, null);
    }

    private static int Around(IReadOnlyDictionary<(int Row, int Col), int> counts, int row, int col)
    {
        var total = 0;
        for (var r = row - 1; r <= row + 1; r++)
        for (var c = col - 1; c <= col + 1; c++)
            if (counts.TryGetValue((r, c), out var n)) total += n;
        return total;
    }
}
