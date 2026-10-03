using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Safety;

namespace KrakowOpenData.Application.Safety;

/// <summary>What the user (or planner) is planning for. Decides which score and which live pressure rank places.</summary>
public enum PlanningEvent
{
    Heat,
    Night,
    Both
}

/// <summary>One grid cell with live scores (reports and live pressure applied).</summary>
public sealed record ScoredCell(
    PlaceMeasure Measure,
    double Heat,
    double Safety,
    double Combined,
    double HeatPenalty,
    double SafetyPenalty,
    double Priority,
    int OpenReports)
{
    /// <summary>How well the place can cool down (100 − heat score); points the same way as the safety score.</summary>
    public double Cooling => 100 - Heat;

    /// <summary>The event's score expressed so that higher = better (heat is turned around), used for bands and ordering.</summary>
    public double Goodness(PlanningEvent evt) => evt switch { PlanningEvent.Heat => Cooling, PlanningEvent.Night => Safety, _ => Combined };
}

/// <summary>The whole city scored for one event, with the conditions it was scored under.</summary>
public sealed record ScoredGrid(PlanningEvent Event, IReadOnlyList<ScoredCell> Cells, ConditionsDto Conditions, StaticSafetyModel Model, DateTimeOffset GeneratedAt);

/// <summary>
/// Applies live conditions and citizen reports on top of the static model (see <see cref="SafetyModel"/> for the maths)
/// and answers the resident questions: the whole grid, one place, a walking corridor.
/// </summary>
public sealed class ScoreService(
    SafetyModelProvider models,
    ConditionsService conditions,
    ISafetyStore store,
    IClock clock)
{
    public const double CorridorStepMeters = 50;
    public const double CorridorMaxMeters = 5000;

    public static PlanningEvent ParseEvent(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "heat" => PlanningEvent.Heat,
        "night" or "safety" => PlanningEvent.Night,
        _ => PlanningEvent.Both
    };

    public static string EventName(PlanningEvent e) => e switch
    {
        PlanningEvent.Heat => "heat",
        PlanningEvent.Night => "night",
        _ => "both"
    };

    // ── Scoring ──────────────────────────────────────────────────────────────

    /// <summary>Scores every inhabited cell for <paramref name="evt"/>.</summary>
    public async Task<ScoredGrid> ScoreGridAsync(PlanningEvent evt, CancellationToken ct = default)
    {
        var model = await models.GetAsync(ct);
        var current = await conditions.GetAsync(ct);
        // Datasets still loading are reported next to the live-source gaps, so the UI can warn that scores are incomplete.
        current = current with { DataGaps = [.. current.DataGaps, .. model.DataGaps.Where(g => !current.DataGaps.Contains(g))] };
        var reports = await OpenReportsByCellAsync(ct);
        var pressure = PressureOf(current);

        var cells = model.Cells.Select(m => Score(m, reports.GetValueOrDefault(m.CellId), evt, pressure)).ToList();
        return new ScoredGrid(evt, cells, current, model, clock.UtcNow);
    }

    /// <summary>The compact grid for the map and for offline caching.</summary>
    public async Task<GridDto> GetGridAsync(PlanningEvent evt, CancellationToken ct = default)
    {
        var scored = await ScoreGridAsync(evt, ct);
        var rows = scored.Cells
            .Select(c => new[]
            {
                c.Measure.Row, c.Measure.Col, R(c.Heat), R(c.Safety), R(c.Combined), R(c.Measure.Exposure * 100), c.OpenReports, R(c.Priority)
            })
            .ToList();

        return new GridDto(
            new GridMetaDto(GridSpec.OriginLatitude, GridSpec.OriginLongitude, GridSpec.CellLatitudeDegrees, GridSpec.CellLongitudeDegrees, GridSpec.CellSizeMeters, SafetyModel.GoodFrom, SafetyModel.FairFrom, SafetyModel.WeakFrom, SafetyModel.SearchRadiusMeters),
            EventName(evt),
            ["row", "col", "heat", "safety", "combined", "exposure", "openReports", "priority"],
            rows,
            scored.GeneratedAt,
            scored.Conditions);
    }

    /// <summary>Full detail for the place at <paramref name="point"/>: both layers with factors, what is nearby, reports, actions.</summary>
    public async Task<PlaceScoreDto> GetPlaceAsync(GeoPoint point, PlanningEvent evt, CancellationToken ct = default)
    {
        var model = await models.GetAsync(ct);
        var current = await conditions.GetAsync(ct);
        var reports = await OpenReportsByCellAsync(ct);
        var measure = model.Measure(point);
        var scored = Score(measure, reports.GetValueOrDefault(measure.CellId), evt, PressureOf(current));

        var nearby = reports.Values.SelectMany(r => r)
            .Where(r => GridSpec.Distance(point, r.Location) <= 400)
            .OrderByDescending(r => r.LastActivityAt)
            .Select(r => r.ToDto(includeNote: false))
            .ToList();

        return BuildPlace(measure, scored, evt, model, nearby, clock.UtcNow);
    }

    /// <summary>Detail for a grid cell by id (measured at the cell centre).</summary>
    public async Task<PlaceScoreDto?> GetCellAsync(string cellId, PlanningEvent evt, bool includeNotes, CancellationToken ct = default)
    {
        if (!GridSpec.TryParseId(cellId, out var row, out var col)) return null;
        var model = await models.GetAsync(ct);
        var current = await conditions.GetAsync(ct);
        var reports = await OpenReportsByCellAsync(ct);
        var measure = model.FindCell(GridSpec.IdOf(row, col)) ?? model.Measure(GridSpec.CenterOf(row, col));
        var cellReports = reports.GetValueOrDefault(measure.CellId) ?? [];
        var scored = Score(measure, cellReports, evt, PressureOf(current));

        var dtos = cellReports.OrderByDescending(r => r.LastActivityAt).Select(r => r.ToDto(includeNotes)).ToList();
        return BuildPlace(measure, scored, evt, model, dtos, clock.UtcNow);
    }

    /// <summary>
    /// Scores samples every 50 m along the straight line from <paramref name="from"/> to <paramref name="to"/>
    /// (at most 5 km). It is a corridor check, not a street route: it tells how well served and lit the way is,
    /// not which streets to take. For street routes see <see cref="RouteService"/>.
    /// </summary>
    public async Task<CorridorDto> GetCorridorAsync(GeoPoint from, GeoPoint to, CancellationToken ct = default)
    {
        var length = GridSpec.Distance(from, to);
        if (length > CorridorMaxMeters)
            throw new SafetyValidationException("to", $"The corridor is limited to {CorridorMaxMeters / 1000:0} km.");

        var (samples, reportsNearby) = await SamplePathAsync([from, to], ct);
        var weakest = samples.Select((s, i) => (s, i)).MinBy(x => x.s.Safety).i;

        return new CorridorDto(
            Math.Round(length),
            (int)Math.Ceiling(length / SafetyModel.WalkMetersPerMinute),
            samples,
            samples.Min(s => s.Safety),
            Math.Round(samples.Average(s => s.Safety), 1),
            samples.Max(s => s.Heat),
            Math.Round(samples.Average(s => s.Heat), 1),
            weakest,
            reportsNearby,
            "Straight-line corridor, not a street route. Scores come from mapped lighting, night transit, open places and citizen reports.");
    }

    /// <summary>
    /// Scores a path (a polyline) every <see cref="CorridorStepMeters"/> along its length: heat score, safety score and overall score at each sample,
    /// with open citizen reports applied. Also counts the open reports within 100 m of the path.
    /// </summary>
    public async Task<(IReadOnlyList<CorridorSampleDto> Samples, int OpenReportsNearby)> SamplePathAsync(IReadOnlyList<GeoPoint> path, CancellationToken ct = default)
    {
        var model = await models.GetAsync(ct);
        var reports = await OpenReportsByCellAsync(ct);
        var now = clock.UtcNow;

        var samples = new List<CorridorSampleDto>();
        foreach (var p in PathSampler.Sample(path, CorridorStepMeters))
        {
            var m = model.Measure(p);
            var cellReports = reports.GetValueOrDefault(m.CellId) ?? [];
            var cooling = SafetyModel.LayerScore(m.HeatBase, ReportRules.CellPenalty(ScoreLayer.Heat, cellReports, now));
            var safety = SafetyModel.LayerScore(m.SafetyBase, ReportRules.CellPenalty(ScoreLayer.Safety, cellReports, now));
            samples.Add(new CorridorSampleDto(Math.Round(p.Latitude, 6), Math.Round(p.Longitude, 6), R(SafetyModel.HeatScore(cooling)), R(safety), R(SafetyModel.Combine(cooling, safety))));
        }

        var points = samples.Select(s => new GeoPoint(s.Latitude, s.Longitude)).ToList();
        var reportsNearby = reports.Values.SelectMany(r => r).Count(r => points.Any(s => GridSpec.Distance(s, r.Location) <= 100));
        return (samples, reportsNearby);
    }

    /// <summary>
    /// The mapped features behind the factors (water, toilets, parks, refuge, open places, AEDs), so an app can cache them
    /// and still show "nearest relief" with no connection. Stops are left out: they are numerous and the app does not need them.
    /// </summary>
    public async Task<IReadOnlyList<FeatureDto>> GetFeaturesAsync(CancellationToken ct = default)
    {
        var model = await models.GetAsync(ct);
        return new[] { "water", "toilets", "green", "refuge", "openPlaces", "aed" }
            .SelectMany(key => model.FeaturesOf(key).Select(f => new FeatureDto(
                key, f.Kind, f.Name, Math.Round(f.Location.Latitude, 6), Math.Round(f.Location.Longitude, 6), f.RadiusMeters, f.OpeningHours)))
            .ToList();
    }

    // ── Internals ────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies reports and live pressure to a measured place:
    /// cooling capacity = base − report penalty (clamped), heat score = 100 − that, combined = <see cref="SafetyModel.Combine"/>,
    /// priority = <see cref="SafetyModel.Priority"/> of the event's score.
    /// </summary>
    public ScoredCell Score(PlaceMeasure m, IReadOnlyList<CitizenReport>? cellReports, PlanningEvent evt, HeatPressure pressure)
    {
        var now = clock.UtcNow;
        var reports = cellReports ?? [];
        var heatPenalty = ReportRules.CellPenalty(ScoreLayer.Heat, reports, now);
        var safetyPenalty = ReportRules.CellPenalty(ScoreLayer.Safety, reports, now);
        var cooling = SafetyModel.LayerScore(m.HeatBase, heatPenalty);
        var safety = SafetyModel.LayerScore(m.SafetyBase, safetyPenalty);
        var combined = SafetyModel.Combine(cooling, safety);

        var priority = evt switch
        {
            PlanningEvent.Heat => SafetyModel.Priority(cooling, m.Exposure, SafetyModel.HeatPressureFactor(pressure)),
            PlanningEvent.Night => SafetyModel.Priority(safety, m.Exposure, SafetyModel.NightPressure),
            _ => SafetyModel.Priority(combined, m.Exposure, Math.Max(SafetyModel.HeatPressureFactor(pressure), SafetyModel.NightPressure))
        };

        // Only the reports of the layers being planned for: Heat counts heat reports, Night safety counts night reports.
        var openReports = reports.Count(r => r.Status == ReportStatus.Open && InEvent(r.Type, evt));
        return new ScoredCell(m, SafetyModel.HeatScore(cooling), safety, combined, heatPenalty, safetyPenalty, priority, openReports);
    }

    /// <summary>True when a report type belongs to a layer the event plans for (Both = every type).</summary>
    public static bool InEvent(ReportType type, PlanningEvent evt) => evt switch
    {
        PlanningEvent.Heat => ReportRules.For(type).Layer == ScoreLayer.Heat,
        PlanningEvent.Night => ReportRules.For(type).Layer == ScoreLayer.Safety,
        _ => true
    };

    public static HeatPressure PressureOf(ConditionsDto c) => Enum.TryParse<HeatPressure>(c.Heat.Pressure, out var p) ? p : HeatPressure.None;

    public async Task<Dictionary<string, List<CitizenReport>>> OpenReportsByCellAsync(CancellationToken ct)
    {
        var cutoff = clock.UtcNow - ReportRules.ListAge;
        return (await store.ListReportsAsync(ct))
            .Where(r => r.Status == ReportStatus.Open && r.LastActivityAt >= cutoff)
            .GroupBy(r => GridSpec.IdOf(r.Location))
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    private PlaceScoreDto BuildPlace(
        PlaceMeasure measure, ScoredCell scored, PlanningEvent evt, StaticSafetyModel model,
        IReadOnlyList<ReportDto> reports, DateTimeOffset now)
    {
        LayerScoreDto LayerDto(Layer layer, double score, double penalty)
        {
            var factors = measure.Factors(layer).Select(f => f.ToDto()).ToList();
            var basePoints = measure.Factors(layer).Sum(f => f.Points);
            // Heat: the score is hot-ness (100 − cooling capacity), so the base is what the missing relief adds, and reports add to it.
            return layer == Layer.Heat
                ? new LayerScoreDto(R(score), SafetyModel.HeatBand(score).ToString(), R(100 - basePoints), Math.Round(penalty, 1), factors)
                : new LayerScoreDto(R(score), SafetyModel.Band(score).ToString(), R(basePoints), Math.Round(penalty, 1), factors);
        }

        var nearest = NearestFeatures(measure.Point, model);
        var actions = SuggestedActions.For(measure, scored, evt);

        return new PlaceScoreDto(
            measure.CellId,
            Math.Round(measure.Point.Latitude, 6),
            Math.Round(measure.Point.Longitude, 6),
            LayerDto(Layer.Heat, scored.Heat, scored.HeatPenalty),
            LayerDto(Layer.Safety, scored.Safety, scored.SafetyPenalty),
            R(scored.Combined),
            SafetyModel.Band(scored.Combined).ToString(),
            Math.Round(measure.Exposure, 2),
            R(scored.Priority),
            EventName(evt),
            nearest,
            reports,
            actions,
            now,
            model.LabelFor(measure.Point));
    }

    private static List<NearestFeatureDto> NearestFeatures(GeoPoint point, StaticSafetyModel model)
    {
        var list = new List<NearestFeatureDto>();
        foreach (var key in new[] { "water", "green", "refuge", "toilets", "openPlaces", "nightTransit", "aed", "transit" })
        {
            var (distance, feature) = model.Nearest(key, point);
            if (distance is null || feature is null) continue;
            list.Add(new NearestFeatureDto(
                key,
                feature.Kind,
                feature.Name,
                Math.Round(feature.Location.Latitude, 6),
                Math.Round(feature.Location.Longitude, 6),
                Math.Round(distance.Value),
                (int)Math.Ceiling(distance.Value / SafetyModel.WalkMetersPerMinute),
                feature.OpeningHours));
        }

        return list;
    }

    private static int R(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}

/// <summary>Maps domain objects to DTOs.</summary>
public static class SafetyMapping
{
    private static readonly Dictionary<string, string> FactorLabels = new()
    {
        ["water"] = "Drinking water",
        ["green"] = "Parks and shade",
        ["refuge"] = "Indoor refuge",
        ["toilets"] = "Public toilets",
        ["transit"] = "Public transport",
        ["lighting"] = "Street lighting",
        ["nightTransit"] = "Night transport",
        ["openPlaces"] = "Open and staffed places",
        ["aed"] = "Defibrillator (AED)"
    };

    public static string LabelOf(string factorKey) => FactorLabels.GetValueOrDefault(factorKey, factorKey);

    public static FactorDto ToDto(this FactorResult f) =>
        new(f.Definition.Key, LabelOf(f.Definition.Key), f.Definition.Layer.ToString(), f.Definition.Weight, f.Value,
            f.Definition.Kind == FactorKind.Density ? "lamps/km²" : "m", Math.Round(f.Score, 1), Math.Round(f.Points, 1), f.NearestName,
            Math.Round(f.Definition.Layer == Layer.Heat ? f.Definition.Weight - f.Points : f.Points, 1));

    public static ReportDto ToDto(this CitizenReport r, bool includeNote) =>
        new(r.Id, r.Type.ToString(), ReportRules.For(r.Type).Layer.ToString(), Math.Round(r.Location.Latitude, 6), Math.Round(r.Location.Longitude, 6),
            GridSpec.IdOf(r.Location), r.CreatedAt, r.LastActivityAt, r.Supporters, r.VerifiedByPlanner, r.Status.ToString(),
            includeNote ? r.Note : null, includeNote ? r.ResolutionNote : null);

    public static PlannerAlertDto ToDto(this PlannerAlert a, int? devices) =>
        new(a.Id, a.Layer?.ToString(), a.Severity.ToString(), a.Title, a.Message, a.MessageTranslations, a.Center.Latitude, a.Center.Longitude,
            a.RadiusMeters, a.CreatedAt, a.ExpiresAt, a.CellId, a.Status.ToString(), devices);

    public static AgencyDto ToDto(this Agency a) => new(a.Id, a.Name, a.Responsibility, a.Phone, a.Url, a.ContactVerified);

    public static DispatchDto ToDto(this AgencyDispatch d) =>
        new(d.Id, d.AgencyId, d.AgencyName, d.Subject, d.Body, d.Location?.Latitude, d.Location?.Longitude, d.CellId, d.Delivery, d.Reference, d.CreatedAt);
}

/// <summary>
/// Rule-based suggestions for a place: every weak factor (score below <see cref="SafetyModel.WeakFactorBelow"/>) of the
/// layer(s) being planned for becomes one action, plus one for open citizen reports. The suggestions are starting
/// points for a planner, not decisions.
/// </summary>
public static class SuggestedActions
{
    private static readonly Dictionary<string, (string Code, string Text)> Catalog = new()
    {
        ["water"] = ("ADD_WATER_POINT", "Install a drinking-water point or schedule a water truck here."),
        ["green"] = ("ADD_SHADE", "Add shade (trees, shade sails) or a mist curtain; consider extended park access."),
        ["refuge"] = ("OPEN_COOL_SPACE", "Open an air-conditioned public space (library, community centre) during heat warnings."),
        ["toilets"] = ("ADD_TOILET", "Provide a temporary public toilet."),
        ["transit"] = ("REVIEW_STOPS", "Review stop coverage so residents can reach relief points."),
        ["lighting"] = ("FIX_LIGHTING", "Inspect street lighting here; repair or add lamps (lighting work order)."),
        ["nightTransit"] = ("REVIEW_NIGHT_SERVICE", "Review night service at the nearest stops."),
        ["openPlaces"] = ("ADD_NIGHT_PRESENCE", "Consider a patrol route or a staffed point after dark."),
        ["aed"] = ("ADD_AED", "Install a defibrillator that is reachable around the clock.")
    };

    public static IReadOnlyList<SuggestedActionDto> For(PlaceMeasure m, ScoredCell scored, PlanningEvent evt)
    {
        var layers = evt switch
        {
            PlanningEvent.Heat => new[] { Layer.Heat },
            PlanningEvent.Night => new[] { Layer.Safety },
            _ => new[] { Layer.Heat, Layer.Safety }
        };

        var actions = layers
            .SelectMany(m.Factors)
            .Where(f => f.IsWeak)
            .OrderByDescending(f => f.Definition.Weight * (100 - f.Score))
            .Select(f =>
            {
                var (code, text) = Catalog[f.Definition.Key];
                return new SuggestedActionDto(code, f.Definition.Layer.ToString(), f.Definition.Key, f.Score < 15 ? "high" : "medium", text);
            })
            .ToList();

        if (scored.OpenReports > 0)
        {
            actions.Insert(0, new SuggestedActionDto("REVIEW_REPORTS", "Both", "reports", "high",
                $"Review {scored.OpenReports} open citizen report(s) here and verify or resolve them."));
        }

        return actions;
    }
}
