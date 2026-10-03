using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Application.Safety;

/// <summary>A mapped thing that scores a factor: a water point, a stop, a park, … <see cref="RadiusMeters"/> is non-zero for parks only.</summary>
public sealed record Feature(string Id, string Kind, string? Name, GeoPoint Location, double RadiusMeters, string? OpeningHours);

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

    public string CellId => GridSpec.IdOf(Row, Col);

    public double HeatBase => SafetyModel.BaseScore(Heat);

    public double SafetyBase => SafetyModel.BaseScore(Safety);

    public double FloodBase => SafetyModel.BaseScore(Flood);

    public double AirBase => SafetyModel.BaseScore(Air);

    public IReadOnlyList<FactorResult> Factors(Layer layer) => layer switch { Layer.Heat => Heat, Layer.Safety => Safety, Layer.Flood => Flood, _ => Air };
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

    public StaticSafetyModel(
        IReadOnlyDictionary<string, IReadOnlyList<Feature>> featuresByFactor,
        IReadOnlyDictionary<(int Row, int Col), int> lampsPerCell,
        IReadOnlyDictionary<(int Row, int Col), int> stopsPerCell,
        IReadOnlyList<string> dataGaps,
        DateTimeOffset builtAt)
    {
        _features = featuresByFactor;
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

        FactorResult Score(FactorDefinition def)
        {
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
            Heat = SafetyModel.HeatFactors.Select(Score).ToList(),
            Safety = SafetyModel.SafetyFactors.Select(Score).ToList(),
            Flood = SafetyModel.FloodFactors.Select(Score).ToList(),
            Air = SafetyModel.AirFactors.Select(Score).ToList()
        };
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
