using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Accessibility;
using KrakowOpenData.Domain.Accessibility;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Mobility;
using KrakowOpenData.Domain.Safety;
using KrakowOpenData.Domain.UrbanSpace;

namespace KrakowOpenData.Application.Safety;

/// <summary>
/// Builds and caches the <see cref="StaticSafetyModel"/> from the public datasets, and decides which mapped feature
/// feeds which factor:
/// <list type="bullet">
/// <item>water ← OSM drinking-water points · toilets ← OSM public toilets · aed ← OSM defibrillators</item>
/// <item>green ← parks · refuge ← libraries, pharmacies, hospitals · openPlaces ← police, hospitals, 24/7 pharmacies</item>
/// <item>transit ← every GTFS stop · nightTransit ← stops with departures between 23:00 and 04:30</item>
/// <item>lighting ← street lamps (counted per grid cell)</item>
/// <item>access.* ← the accessibility dataset (OSM steps, kerbs, lifts, entrances, wheelchair tags, toilets, benches, tactile paving, path surface and slope) and the ZTP stops with wheelchair_boarding, per profile (see <see cref="AccessLayerData"/>)</item>
/// <item>access.* ← the accessibility dataset (OSM steps, kerbs, lifts, entrances, wheelchair tags, toilets, benches, tactile paving, path surface and slope) and the ZTP stops with wheelchair_boarding, per profile (see <see cref="AccessLayerData"/>)</item>
/// </list>
/// A dataset that cannot be loaded does not stop the model: its factors then score 0 and the dataset is listed in
/// <see cref="StaticSafetyModel.DataGaps"/> so the UI can warn that scores are incomplete. A degraded model is
/// rebuilt after a minute, a complete one after <see cref="Lifetime"/>.
/// </summary>
public sealed class SafetyModelProvider(
    IReadRepository<StreetLight> lights,
    IReadRepository<Amenity> amenities,
    IReadRepository<SafetyPlace> places,
    IReadRepository<TransitStop> stops,
    ITransitScheduleRepository schedule,
    IClock clock,
    ISafetyStore? store = null,
    IReadRepository<AccessFeature>? accessFeatures = null)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan DegradedLifetime = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private StaticSafetyModel? _model;
    private DateTimeOffset _expires;

    /// <summary>Forget the cached model so the next request builds a new one (after a planner changed the factor weights).</summary>
    public void Invalidate() => _expires = DateTimeOffset.MinValue;

    public async Task<StaticSafetyModel> GetAsync(CancellationToken ct = default)
    {
        if (_model is { } fresh && clock.UtcNow < _expires) return fresh;

        await _gate.WaitAsync(ct);
        try
        {
            if (_model is { } again && clock.UtcNow < _expires) return again;

            var model = await BuildAsync(ct);
            _model = model;
            _expires = clock.UtcNow + (model.DataGaps.Count == 0 ? Lifetime : DegradedLifetime);
            return model;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<StaticSafetyModel> BuildAsync(CancellationToken ct)
    {
        var gaps = new List<string>();
        Exception? firstFailure = null;

        async Task<IReadOnlyList<T>> Load<T>(string name, Func<Task<IReadOnlyList<T>>> load)
        {
            try
            {
                return await load();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                gaps.Add(name);
                firstFailure ??= ex;
                return [];
            }
        }

        var lampList = await Load("streetLights", () => lights.ListAsync(cancellationToken: ct));
        var amenityList = await Load("amenities", () => amenities.ListAsync(cancellationToken: ct));
        var placeList = await Load("places", () => places.ListAsync(cancellationToken: ct));
        var stopList = await Load("stops", () => stops.ListAsync(cancellationToken: ct));
        // Accessibility (OpenStreetMap steps, kerbs, lifts, ...). Without it the Access layers have no data at all; they never fall back to "accessible".
        IReadOnlyList<AccessFeature> accessList = accessFeatures is null ? [] : await Load("access", () => accessFeatures.ListAsync(cancellationToken: ct));

        IReadOnlySet<string> nightIds = new HashSet<string>();
        if (!gaps.Contains("stops"))
        {
            try
            {
                nightIds = await schedule.GetNightServiceStopIdsAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                gaps.Add("nightTransit");
                firstFailure ??= ex;
            }
        }

        // Nothing at all to score with: report "still loading" (503) instead of a map of zeros.
        if (gaps.Contains("streetLights") && gaps.Contains("amenities") && gaps.Contains("stops") && firstFailure is not null)
            throw firstFailure;

        var features = new Dictionary<string, IReadOnlyList<Feature>>
        {
            ["water"] = AmenityFeatures(amenityList, AmenityKind.DrinkingWater),
            ["toilets"] = AmenityFeatures(amenityList, AmenityKind.Toilets),
            ["aed"] = AmenityFeatures(amenityList, AmenityKind.Defibrillator),
            ["green"] = PlaceFeatures(placeList, p => p.Kind == SafetyPlaceKind.Park),
            ["refuge"] = PlaceFeatures(placeList, p => p.Kind is SafetyPlaceKind.Library or SafetyPlaceKind.Pharmacy or SafetyPlaceKind.Hospital),
            ["openPlaces"] = PlaceFeatures(placeList, p =>
                p.Kind is SafetyPlaceKind.Police or SafetyPlaceKind.Hospital || (p.Kind == SafetyPlaceKind.Pharmacy && p.IsOpenAllNight)),
            ["transit"] = stopList.Select(StopFeature).ToList(),
            ["nightTransit"] = stopList.Where(s => nightIds.Contains(s.Id)).Select(StopFeature).ToList(),
            ["river"] = PlaceFeatures(placeList, p => p.Kind == SafetyPlaceKind.Waterway),
            ["traffic"] = PlaceFeatures(placeList, p => p.Kind == SafetyPlaceKind.MajorRoad),
            ["emergency"] = PlaceFeatures(placeList, p => p.Kind is SafetyPlaceKind.Hospital or SafetyPlaceKind.Police),
            ["evacuation"] = stopList.Select(StopFeature).ToList(),
            ["trees"] = PlaceFeatures(placeList, p => p.Kind == SafetyPlaceKind.Park),
            ["cleanIndoor"] = PlaceFeatures(placeList, p => p.Kind is SafetyPlaceKind.Library or SafetyPlaceKind.Pharmacy or SafetyPlaceKind.Hospital)
        };

        // Rivers and main roads come with the places dataset; if it loaded without them (an old saved copy), say so.
        if (placeList.Count == 0 && !gaps.Contains("places")) gaps.Add("places");   // loaded, but empty: parks, hospitals, rivers and roads are all missing
        if (placeList.Count > 0 && !placeList.Any(p => p.Kind == SafetyPlaceKind.Waterway)) gaps.Add("rivers");
        if (placeList.Count > 0 && !placeList.Any(p => p.Kind == SafetyPlaceKind.MajorRoad)) gaps.Add("mainRoads");

        // Accessibility: one set of distance features per profile; the ZTP wheelchair_boarding flag decides which stops count.
        var accessData = new AccessLayerData(accessList);
        if (accessFeatures is not null && accessList.Count == 0 && !gaps.Contains("access")) gaps.Add("access");   // loaded but empty: every square is "no data"
        var accessibleStops = stopList.Where(s => s.WheelchairAccessible == true).Select(StopFeature).ToList();
        foreach (var profile in AccessProfile.All)
            foreach (var (key, list) in accessData.FeatureLists(profile, accessibleStops))
                features[key] = list;

        var overrides = store is null ? new Dictionary<string, double>() : await store.GetWeightOverridesAsync(ct);
        var definitions = Enum.GetValues<Layer>().ToDictionary(l => l, l => FactorWeights.Apply(l, overrides));

        return new StaticSafetyModel(features, CountPerCell(lampList.Select(l => l.Location)), CountPerCell(stopList.Select(s => s.Location)), gaps, clock.UtcNow, definitions, accessData);
    }

    private static IReadOnlyList<Feature> AmenityFeatures(IReadOnlyList<Amenity> all, AmenityKind kind) =>
        all.Where(a => a.Kind == kind)
            .Select(a => new Feature(a.Id, kind.ToString(), a.Name, a.Location, 0, OpeningHoursFrom(a.Details), WheelchairFrom(a.Details)))
            .ToList();

    private static IReadOnlyList<Feature> PlaceFeatures(IReadOnlyList<SafetyPlace> all, Func<SafetyPlace, bool> filter) =>
        all.Where(filter)
            .Select(p => new Feature(p.Id, p.Kind.ToString(), p.Name, p.Location, p.EquivalentRadiusMeters, p.OpeningHours, p.Wheelchair))
            .ToList();

    private static Feature StopFeature(TransitStop s) =>
        new(s.Id, "Stop", s.Name, s.Location, 0, null, s.WheelchairAccessible switch { true => "yes", false => "no", null => null });

    /// <summary>
    /// The wheelchair access of an amenity from its details ("wheelchair: yes", or "toilets:wheelchair: yes" for toilets):
    /// yes | limited | no, or null when not mapped. "designated" counts as yes.
    /// </summary>
    public static string? WheelchairFrom(string? details)
    {
        if (details is null) return null;
        string? Value(string key) => details.Split("; ")
            .Select(part => part.Split(": ", 2))
            .Where(kv => kv.Length == 2 && kv[0] == key)
            .Select(kv => kv[1].Trim().ToLowerInvariant())
            .FirstOrDefault();

        return (Value("wheelchair") ?? Value("toilets:wheelchair")) switch
        {
            "yes" or "designated" => "yes",
            "limited" => "limited",
            "no" => "no",
            _ => null
        };
    }

    private static Dictionary<(int Row, int Col), int> CountPerCell(IEnumerable<GeoPoint> points)
    {
        var counts = new Dictionary<(int Row, int Col), int>();
        foreach (var p in points)
        {
            if (!GridSpec.IsInArea(p)) continue;
            var cell = GridSpec.CellOf(p);
            counts[cell] = counts.GetValueOrDefault(cell) + 1;
        }

        return counts;
    }

    /// <summary>Amenity details are stored as "key: value; key: value"; pulls out the opening hours.</summary>
    private static string? OpeningHoursFrom(string? details)
    {
        const string key = "opening_hours: ";
        var start = details?.IndexOf(key, StringComparison.Ordinal) ?? -1;
        if (start < 0) return null;
        var rest = details![(start + key.Length)..];
        var end = rest.IndexOf(';');
        return (end < 0 ? rest : rest[..end]).Trim();
    }
}
