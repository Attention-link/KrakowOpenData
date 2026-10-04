using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Safety;
using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.Accessibility;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Safety;

namespace KrakowOpenData.Application.Accessibility;

/// <summary>When the accessibility dataset was last downloaded (implemented by the OSM client; optional).</summary>
public interface IAccessDataStatus
{
    DateTimeOffset? LoadedAt { get; }
}

/// <summary>
/// "Kraków bez barier": concrete barriers and amenities around a point, and what a walking route means for a profile
/// (wheelchair, pram, limited mobility).
/// <list type="bullet">
/// <item>Every item has its facts, source, last OSM edit and reliability (see <see cref="AccessReliability"/>).</item>
/// <item>Missing data is "unknown" and is never counted as accessible; coverage says how much is known.</item>
/// <item>Resident reports (the PathHazard type, which feeds the Access score layer) are returned separately as <c>user_unverified</c>.</item>
/// <item>A route is matched against items within <see cref="MatchMeters"/> of points sampled every <see cref="StepMeters"/>.</item>
/// </list>
/// </summary>
public sealed class AccessService(
    IReadRepository<AccessFeature> features,
    ReportService reports,
    IWalkingRouter router,
    IClock clock,
    IAccessDataStatus? status = null)
{
    public const double MaxRadiusMeters = 1000;
    public const double MatchMeters = 15;
    public const double StepMeters = 10;
    public const int DefaultLimit = 150;
    public const int MaxLimit = 500;
    public const double MaxRouteMeters = 8000;
    public const int MaxPathPoints = 5000;

    /// <summary>Below this share of known values coverage counts as thin and the response carries a data note.</summary>
    public const double ThinCoverage = 0.3;

    public const string Licence = "Dane © współtwórcy OpenStreetMap, licencja ODbL 1.0 (openstreetmap.org/copyright)";

    public const string NoDataNote = "Brak danych nie oznacza, że trasa lub miejsce są dostępne. Bariery liczone są tylko tam, gdzie są zmapowane w OpenStreetMap.";

    public static readonly IReadOnlyDictionary<string, AccessKind> Kinds = new Dictionary<string, AccessKind>(StringComparer.OrdinalIgnoreCase)
    {
        ["steps"] = AccessKind.Steps,
        ["kerb"] = AccessKind.Kerb,
        ["elevator"] = AccessKind.Elevator,
        ["entrance"] = AccessKind.Entrance,
        ["place"] = AccessKind.Place,
        ["toilets"] = AccessKind.Toilets,
        ["bench"] = AccessKind.Bench,
        ["tactile"] = AccessKind.TactilePaving,
        ["path"] = AccessKind.Path
    };

    private static readonly HashSet<string> FootwayClasses = ["footway", "pedestrian", "path", "living_street", "cycleway"];

    private readonly object _lock = new();
    private AccessIndex? _index;

    public static string KindName(AccessKind kind) => Kinds.First(k => k.Value == kind).Key;

    public static string StatusName(AccessStatus s) => s.ToString().ToLowerInvariant();

    /// <summary>Parses a comma-separated kinds filter; throws a validation error for unknown names. Null or empty = all kinds.</summary>
    public static IReadOnlySet<AccessKind>? ParseKinds(string? kinds)
    {
        if (string.IsNullOrWhiteSpace(kinds)) return null;
        var set = new HashSet<AccessKind>();
        foreach (var part in kinds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Kinds.TryGetValue(part, out var k))
                throw new SafetyValidationException("kinds", $"Use any of: {string.Join(", ", Kinds.Keys)}.");
            set.Add(k);
        }

        return set;
    }

    private static int ListRank(AccessStatus s) => s switch
    {
        AccessStatus.No => 0,
        AccessStatus.Limited => 1,
        AccessStatus.Unknown => 2,
        _ => 3
    };

    public async Task<AccessNearbyDto> NearAsync(GeoPoint point, double radius, AccessProfile profile, IReadOnlySet<AccessKind>? kinds, int limit, CancellationToken ct = default)
    {
        if (radius <= 0 || radius > MaxRadiusMeters) throw new SafetyValidationException("radius", $"radius must be between 1 and {MaxRadiusMeters:0} metres.");
        limit = Math.Clamp(limit, 1, MaxLimit);

        var index = await IndexAsync(ct);
        var now = clock.UtcNow;
        var around = index.Near(point, radius).ToList();

        // Listed items: every barrier and amenity except entrances that say nothing (they only count in coverage)
        // and path stretches that are fine or unknown for this profile (too many to list; they count in coverage and routes).
        var listed = new List<(AccessFeature F, double D, AccessStatus S)>();
        foreach (var (f, d) in around)
        {
            var s = AccessRules.Status(f.Kind, f.Attributes, profile);
            if (f.Kind == AccessKind.Entrance && !f.Attributes.HasAccessData) continue;
            if (f.Kind == AccessKind.Path && s is AccessStatus.Yes or AccessStatus.Unknown) continue;
            listed.Add((f, d, s));
        }

        var filtered = kinds is null ? listed : listed.Where(x => kinds.Contains(x.F.Kind)).ToList();
        // Barriers first, so they are not buried under dozens of benches: no, limited, no data, yes; nearest first within each.
        var items = filtered.OrderBy(x => ListRank(x.S)).ThenBy(x => x.D).Take(limit).Select(x => Item(x.F, x.S, now, x.D, null)).ToList();

        var counts = filtered.GroupBy(x => x.F.Kind)
            .OrderBy(g => g.Key)
            .Select(g => new AccessCountDto(KindName(g.Key), g.Count(),
                g.Count(x => x.S == AccessStatus.Yes), g.Count(x => x.S == AccessStatus.Limited),
                g.Count(x => x.S == AccessStatus.No), g.Count(x => x.S == AccessStatus.Unknown)))
            .ToList();

        var coverage = Coverage(around.Select(x => x.Feature).ToList());
        var inArea = index.Covers(point);
        var (noteCode, note) = DataNote(inArea, coverage, around.Count);

        var userReports = (await reports.ListAsync(new ReportFilter(point, radius, ReportStatusFilter.Open, null, ReportType.PathHazard), false, 50, ct))
            .Select(r => new AccessUserReportDto(r.Id, r.Type, r.Latitude, r.Longitude,
                Math.Round(AccessIndex.Flat(point, new GeoPoint(r.Latitude, r.Longitude))), r.CreatedAt, r.Supporters, r.VerifiedByPlanner,
                AccessReliability.UserUnverified))
            .ToList();

        return new AccessNearbyDto(
            profile.Key, point.Latitude, point.Longitude, radius, items, filtered.Count, counts, coverage, noteCode, note,
            inArea, Area(index), status?.LoadedAt, userReports, Licence, IsSample(index));
    }

    /// <summary>Routes from A to B along streets (the fastest walking route; a straight line when routing is down) and assesses it.</summary>
    public async Task<AccessRouteDto> AssessRouteAsync(GeoPoint from, GeoPoint to, AccessProfile profile, CancellationToken ct = default)
    {
        if (GridSpec.Distance(from, to) > RouteService.MaxStraightMeters)
            throw new SafetyValidationException("to", $"Routes are limited to {RouteService.MaxStraightMeters / 1000:0} km between the two points.");

        IReadOnlyList<GeoPoint> path = [from, to];
        try
        {
            var routes = await router.RouteAsync([from, to], false, ct);
            if (routes.Count > 0) path = routes[0].Points;
        }
        // An HttpClient timeout is a TaskCanceledException too: only the caller's own cancellation may escape (as in RouteService).
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Street routing unavailable (down, timed out or paused for fair use): assess the straight line (the route note says data is incomplete anyway).
        }

        return await AssessPathAsync(path, profile, ct);
    }

    /// <summary>What a path (e.g. one from /api/safety/route) means for the profile.</summary>
    public async Task<AccessRouteDto> AssessPathAsync(IReadOnlyList<GeoPoint> path, AccessProfile profile, CancellationToken ct = default)
    {
        if (path.Count < 2) throw new SafetyValidationException("path", "Give at least two points.");
        if (path.Count > MaxPathPoints) throw new SafetyValidationException("path", $"At most {MaxPathPoints} points.");
        if (path.Any(p => !GridSpec.IsInArea(p))) throw new SafetyValidationException("path", "Every point must be in or around Kraków.");
        var length = PathSampler.Length(path);
        if (length > MaxRouteMeters) throw new SafetyValidationException("path", $"Paths are limited to {MaxRouteMeters / 1000:0} km.");

        var index = await IndexAsync(ct);
        var now = clock.UtcNow;
        var samples = PathSampler.Sample(path, StepMeters);
        var step = samples.Count > 1 ? length / (samples.Count - 1) : 0;

        double rough = 0, unpaved = 0, steep = 0;
        var unknownSamples = 0;
        var roughSurfaces = new Dictionary<string, double>();
        var near = new Dictionary<string, (AccessFeature F, double D, double Along)>();
        var matched = new HashSet<string>();   // steps and paths that were the closest line to at least one sample
        var inArea = samples.Any(index.Covers);

        for (var i = 0; i < samples.Count; i++)
        {
            var along = i * step;
            AccessFeature? way = null;
            var wayDistance = double.MaxValue;
            foreach (var (f, d) in index.Near(samples[i], MatchMeters))
            {
                if (!near.TryGetValue(f.Id, out var seen) || d < seen.D) near[f.Id] = (f, d, along);
                if (f.Line is not null && d < wayDistance && f.Kind is AccessKind.Path or AccessKind.Steps)
                {
                    way = f;
                    wayDistance = d;
                }
            }

            if (way is not null) matched.Add(way.Id);
            var weight = i == 0 || i == samples.Count - 1 ? step / 2 : step;
            if (way is null || (way.Kind == AccessKind.Path && way.Attributes.SurfaceClass is null))
            {
                unknownSamples++;
                continue;
            }

            if (way.Kind == AccessKind.Steps) continue;
            var a = way.Attributes;
            if (a.SurfaceClass == "paved_rough")
            {
                rough += weight;
                var name = a.SurfaceRaw ?? "paved_rough";
                roughSurfaces[name] = roughSurfaces.GetValueOrDefault(name) + weight;
            }
            else if (a.SurfaceClass == "unpaved") unpaved += weight;

            if (a.InclinePercent is { } incline && incline > profile.MaxInclinePercent) steep += weight;
        }

        // Lines count only where the route actually follows them (closest line to a sample, or within 5 m), so a cobbled street
        // or a flight of steps running parallel a few metres away is not blamed on the route. Points count within MatchMeters.
        var found = near.Values.Where(x => x.F.Line is null || matched.Contains(x.F.Id) || x.D <= 5).ToList();
        var steps = found.Where(x => x.F.Kind == AccessKind.Steps).ToList();
        var stepsOk = steps.Count(x => AccessRules.Status(AccessKind.Steps, x.F.Attributes, profile) == AccessStatus.Yes);
        var kerbs = found.Where(x => x.F.Kind == AccessKind.Kerb).ToList();
        var raised = kerbs.Count(x => x.F.Attributes.Kerb == "raised");
        var lowered = kerbs.Count(x => x.F.Attributes.Kerb is "lowered" or "flush");
        var unknownKerbs = kerbs.Count - raised - lowered;
        var elevators = found.Count(x => x.F.Kind == AccessKind.Elevator);
        var toilets = found.Count(x => x.F.Kind == AccessKind.Toilets && AccessRules.Status(AccessKind.Toilets, x.F.Attributes, profile) == AccessStatus.Yes);
        var benches = found.Count(x => x.F.Kind == AccessKind.Bench);
        var unknownShare = samples.Count == 0 ? 1 : (double)unknownSamples / samples.Count;

        var barriers = found
            .Where(x => x.F.Kind is AccessKind.Steps or AccessKind.Kerb or AccessKind.Path)
            .Select(x => (x.F, x.D, x.Along, S: AccessRules.Status(x.F.Kind, x.F.Attributes, profile)))
            // Rough surface is summarised in metres rather than listed stretch by stretch; steep, unpaved or narrow stretches are listed.
            .Where(x => x.S is AccessStatus.No || (x.S == AccessStatus.Limited && x.F.Kind != AccessKind.Path))
            .OrderBy(x => x.Along)
            .Take(80)
            .Select(x => Item(x.F, x.S, now, Math.Round(x.D), Math.Round(x.Along)))
            .ToList();

        var summary = Summary(profile, steps.Count - stepsOk, stepsOk, raised, unknownKerbs, rough, roughSurfaces, unpaved, steep, unknownShare, elevators, toilets, benches);

        return new AccessRouteDto(
            profile.Key, Math.Round(length), MatchMeters, StepMeters, steps.Count - stepsOk, stepsOk, raised, lowered, unknownKerbs,
            Math.Round(rough), Math.Round(unpaved), Math.Round(steep), Math.Round(unknownShare, 3), elevators, toilets, benches,
            summary, barriers, inArea, NoDataNote, Licence, IsSample(index));
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private async Task<AccessIndex> IndexAsync(CancellationToken ct)
    {
        var list = await features.ListAsync(cancellationToken: ct);
        lock (_lock)
        {
            if (_index is null || !ReferenceEquals(_index.Features, list)) _index = new AccessIndex(list);
            return _index;
        }
    }

    public static AccessItemDto Item(AccessFeature f, AccessStatus s, DateTimeOffset now, double? distance, double? along) =>
        new(
            f.Id,
            KindName(f.Kind),
            Math.Round(f.Location.Latitude, 6),
            Math.Round(f.Location.Longitude, 6),
            f.Name,
            StatusName(s),
            IsBarrier(f.Kind, s),
            AccessFacts.For(f),
            f.Source,
            f.LastEdited,
            AccessReliability.Of(s != AccessStatus.Unknown || f.Attributes.HasAccessData, f.LastEdited, now),
            f.OsmUrl,
            f.EditUrl,
            distance is null ? null : Math.Round(distance.Value),
            along);

    /// <summary>
    /// True when the item counts against the profile (status limited or no). Benches and tactile paving are amenities for these
    /// profiles, never barriers: a crossing without tactile paving does not stop a wheelchair.
    /// </summary>
    public static bool IsBarrier(AccessKind kind, AccessStatus s) =>
        kind is not (AccessKind.Bench or AccessKind.TactilePaving) && (s is AccessStatus.No or AccessStatus.Limited);

    /// <summary>How much is known around the point: per attribute, items with a value out of all items that could have one.</summary>
    public static IReadOnlyList<AccessCoverageDto> Coverage(IReadOnlyList<AccessFeature> around)
    {
        AccessCoverageDto Line(string key, string label, IEnumerable<AccessFeature> all, Func<AccessFeature, bool> known)
        {
            var list = all.ToList();
            return new AccessCoverageDto(key, list.Count(known), list.Count, label);
        }

        return
        [
            Line("entrances", "wejść ma dane o dostępności", around.Where(f => f.Kind == AccessKind.Entrance), f => f.Attributes.HasAccessData),
            Line("steps", "schodów ma dane o rampie", around.Where(f => f.Kind == AccessKind.Steps), f => f.Attributes.Ramp is not null || f.Attributes.RampNone),
            Line("kerbs", "krawężników i przejść ma dane o wysokości krawężnika",
                around.Where(f => f.Kind == AccessKind.Kerb || (f.Kind == AccessKind.TactilePaving && f.Attributes.Highway == "crossing")), f => f.Attributes.Kerb is not null),
            Line("paths", "odcinków chodników ma dane o nawierzchni",
                around.Where(f => f.Kind == AccessKind.Path && FootwayClasses.Contains(f.Attributes.Highway ?? "")), f => f.Attributes.SurfaceClass is not null),
            Line("toilets", "toalet ma dane o dostępie dla wózka", around.Where(f => f.Kind == AccessKind.Toilets), f => (f.Attributes.Wheelchair ?? f.Attributes.ToiletsWheelchair) is not null),
            Line("elevators", "wind ma dane o dostępie dla wózka", around.Where(f => f.Kind == AccessKind.Elevator), f => f.Attributes.Wheelchair is not null)
        ];
    }

    private static (string? Code, string? Note) DataNote(bool inArea, IReadOnlyList<AccessCoverageDto> coverage, int itemsAround)
    {
        if (!inArea)
            return ("outside_area", "To miejsce jest poza obszarem, dla którego pobrano dane o dostępności. Wszystko tutaj to brak danych, nie dostępność.");
        if (itemsAround == 0)
            return ("no_data", "W tej okolicy nie ma zmapowanych barier ani udogodnień. To brak danych, nie dowód, że barier nie ma.");

        var thin = coverage.Where(c => c.Key is "entrances" or "paths" or "kerbs" && c.Total >= 5 && (double)c.Known / c.Total < ThinCoverage).ToList();
        if (thin.Count == 0) return (null, null);
        var parts = string.Join("; ", thin.Select(c => $"tylko {c.Known} z {c.Total} {c.Label}"));
        return ("thin_coverage", $"Mało danych w tej okolicy: {parts}. Brak danych nie oznacza dostępności.");
    }

    private static IReadOnlyList<AccessSummaryLineDto> Summary(
        AccessProfile p, int stepsNoRamp, int stepsRamp, int raised, int unknownKerbs, double rough, Dictionary<string, double> roughSurfaces,
        double unpaved, double steep, double unknownShare, int elevators, int toilets, int benches)
    {
        var lines = new List<AccessSummaryLineDto>();
        var stepsSeverity = p.StepsBlock ? "high" : "medium";
        if (stepsNoRamp > 0)
            lines.Add(new("steps_no_ramp", stepsNoRamp, 0, 0, stepsSeverity, $"{stepsNoRamp} {Plural(stepsNoRamp, "schody", "schody", "schodów")} bez rampy"));
        if (raised > 0)
            lines.Add(new("kerb_raised", raised, 0, 0, p.RaisedKerbBlocks ? "high" : "medium", $"{raised} {Plural(raised, "wysoki krawężnik", "wysokie krawężniki", "wysokich krawężników")}"));
        if (steep >= StepMeters)
            lines.Add(new("steep", 0, Math.Round(steep), 0, p.StepsBlock ? "high" : "medium", $"{Math.Round(steep):0} m odcinków o nachyleniu powyżej {AccessFacts.Pl(p.MaxInclinePercent)}%"));
        if (unpaved >= StepMeters)
            lines.Add(new("unpaved", 0, Math.Round(unpaved), 0, p.UnpavedBlocks ? "high" : "medium", $"{Math.Round(unpaved):0} m nawierzchni nieutwardzonej"));
        if (rough >= StepMeters)
        {
            var top = roughSurfaces.OrderByDescending(kv => kv.Value).First().Key;
            lines.Add(new("rough", 0, Math.Round(rough), 0, "medium", $"{Math.Round(rough):0} m nierównej nawierzchni ({AccessFacts.SurfaceName(top)})"));
        }

        if (stepsRamp > 0)
            lines.Add(new("steps_ramp", stepsRamp, 0, 0, "info", $"{stepsRamp} {Plural(stepsRamp, "schody", "schody", "schodów")} z rampą"));
        if (unknownKerbs > 0)
            lines.Add(new("kerb_unknown", unknownKerbs, 0, 0, "info", $"{unknownKerbs} {Plural(unknownKerbs, "krawężnik", "krawężniki", "krawężników")} bez danych o wysokości"));

        var share = Math.Round(unknownShare * 100);
        lines.Add(new("unknown_share", 0, 0, Math.Round(unknownShare, 3), share >= 30 ? "medium" : "info", $"{share:0}% trasy bez danych o nawierzchni"));

        if (elevators > 0) lines.Add(new("elevator", elevators, 0, 0, "info", $"{elevators} {Plural(elevators, "winda", "windy", "wind")} przy trasie"));
        if (toilets > 0) lines.Add(new("toilets", toilets, 0, 0, "info", $"{toilets} {Plural(toilets, "dostępna toaleta", "dostępne toalety", "dostępnych toalet")} przy trasie"));
        if (benches > 0) lines.Add(new("bench", benches, 0, 0, "info", $"{benches} {Plural(benches, "ławka", "ławki", "ławek")} do odpoczynku przy trasie"));
        return lines;
    }

    /// <summary>Polish plural: 1 one, 2–4 few (not 12–14), otherwise many.</summary>
    public static string Plural(int n, string one, string few, string many)
    {
        var m10 = n % 10;
        var m100 = n % 100;
        if (n == 1) return one;
        return m10 is >= 2 and <= 4 && m100 is not (>= 12 and <= 14) ? few : many;
    }

    private static AccessAreaDto Area(AccessIndex index) => index.Bounds is { } b
        ? new AccessAreaDto(Math.Round(b.MinLat, 5), Math.Round(b.MinLon, 5), Math.Round(b.MaxLat, 5), Math.Round(b.MaxLon, 5))
        : new AccessAreaDto(0, 0, 0, 0);

    private static bool IsSample(AccessIndex index) =>
        index.Features.Count > 0 && index.Features.Any(f => f.Source.Contains("sample", StringComparison.OrdinalIgnoreCase) || f.Source.Contains("przykład", StringComparison.OrdinalIgnoreCase));
}
