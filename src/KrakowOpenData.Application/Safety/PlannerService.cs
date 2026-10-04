using KrakowOpenData.Application.Accessibility;
using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.Safety;

namespace KrakowOpenData.Application.Safety;

/// <summary>
/// The planner dashboard in one call: headline numbers, how scores are distributed, which factors leave the most gaps,
/// and the places to act on first. Everything is derived from <see cref="ScoreService"/>, so the dashboard and the
/// resident map always agree.
///
/// <para><b>Gap</b> of a factor = share of built-up area (grid cells weighted by exposure) where that factor scores below <see cref="SafetyModel.WeakFactorBelow"/>.
/// <b>Priority</b> is explained on <see cref="SafetyModel.Priority"/>. Reach of an alert is the number of phones that asked
/// for alerts inside the circle in the last 15 minutes (<see cref="PresenceTracker"/>).</para>
/// </summary>
public sealed class PlannerService(ScoreService scores, ISafetyStore store, PresenceTracker presence)
{
    public const int DefaultTop = 12;

    public async Task<PlannerSummaryDto> GetSummaryAsync(PlanningEvent evt, int top, CancellationToken ct = default)
    {
        var grid = await scores.ScoreGridAsync(evt, ct);
        var isAccess = ScoreService.IsAccess(evt);
        // Accessibility: squares with no accessibility data are LEFT OUT of every average, share, gap and ranking (they are counted separately),
        // so missing data can never look like good or bad accessibility.
        IReadOnlyList<ScoredCell> cells = isAccess ? grid.Cells.Where(c => c.HasAccessData).ToList() : grid.Cells;
        var now = grid.GeneratedAt;

        // The score shown for the event: the cooling score for Heat, safety score for Night, flood safety, clean air; every score is higher = better.
        double EventScore(ScoredCell c) => evt switch
        {
            PlanningEvent.Heat => c.Heat,
            PlanningEvent.Night => c.Safety,
            PlanningEvent.Flood => c.Flood,
            PlanningEvent.Air => c.Air,
            PlanningEvent.Access => c.Access ?? 0,
            PlanningEvent.AccessPram => c.AccessPram ?? 0,
            PlanningEvent.AccessMobility => c.AccessMobility ?? 0,
            _ => c.Combined
        };

        var layers = ScoreService.LayersOf(evt);

        // Headline numbers.
        var kpis = new List<KpiDto>
        {
            new("cells", cells.Count, "cells"),
            new("averageScore", Math.Round(cells.Count == 0 ? 0 : cells.Average(EventScore), 1), "score"),
            new("criticalCells", cells.Count(c => SafetyModel.Band(c.Goodness(evt)) == ScoreBand.Critical), "cells"),
            new("weakCells", cells.Count(c => SafetyModel.Band(c.Goodness(evt)) == ScoreBand.Weak), "cells")
        };

        // Shares are exposure-weighted: a thinly built fringe cell counts less than a busy street grid (see SafetyModel.Exposure).
        var totalExposure = cells.Sum(c => c.Measure.Exposure);
        double Share(Func<ScoredCell, bool> predicate) => totalExposure == 0 ? 0 : Math.Round(100.0 * cells.Where(predicate).Sum(c => c.Measure.Exposure) / totalExposure, 1);
        double? Value(ScoredCell c, string key) => c.Measure.Find(key)?.Value;

        if (layers.Contains(Layer.Heat))
        {
            kpis.Add(new KpiDto("noWater500", Share(c => Value(c, "water") is null or > 500), "%"));
            kpis.Add(new KpiDto("noGreen500", Share(c => Value(c, "green") is null or > 500), "%"));
        }

        if (layers.Contains(Layer.Safety))
        {
            kpis.Add(new KpiDto("poorlyLit", Share(c => c.Measure.Safety.First(f => f.Definition.Key == "lighting").IsWeak), "%"));
            kpis.Add(new KpiDto("noNightTransit500", Share(c => Value(c, "nightTransit") is null or > 500), "%"));
        }

        if (layers.Contains(Layer.Flood))
        {
            kpis.Add(new KpiDto("nearRiver200", Share(c => Value(c, "river") is { } d && d < 200), "%"));
            kpis.Add(new KpiDto("noEmergency1000", Share(c => Value(c, "emergency") is null or > 1000), "%"));
            kpis.Add(new KpiDto("riverLevel", Math.Round(100 * SafetyModel.FloodLevel(grid.Conditions.Hydro.WorstState)), "%"));
        }

        if (layers.Contains(Layer.Air))
        {
            kpis.Add(new KpiDto("nearMainRoad100", Share(c => Value(c, "traffic") is { } d && d < 100), "%"));
            kpis.Add(new KpiDto("noTrees500", Share(c => Value(c, "trees") is null or > 500), "%"));
            kpis.Add(new KpiDto("airLevel", Math.Round(100 * SafetyModel.AirLevel(grid.Conditions.Air.Pm25Average ?? grid.Conditions.Air.Pm25)), "%"));
        }

        if (isAccess)
        {
            var p = ScoreService.AccessProfileOf(evt)!;
            string K(string b) => AccessKeys.Make(p, b);
            kpis.Add(new KpiDto("accessCoverage", grid.Cells.Count == 0 ? 0 : Math.Round(100.0 * cells.Count / grid.Cells.Count, 1), "%"));
            kpis.Add(new KpiDto("cellsNoData", grid.Cells.Count - cells.Count, "cells"));
            kpis.Add(new KpiDto("stepsBarriers", Share(c => c.Measure.Find(K("steps"))!.IsWeak || c.Measure.Find(K("kerbs"))!.IsWeak), "%"));
            // Left out while no ZTP stop is flagged wheelchair-accessible: "100% have none" would only reflect missing data (see StaticSafetyModel.AccessStopsAvailable).
            if (grid.Model.AccessStopsAvailable)
                kpis.Add(new KpiDto("noAccessibleStop400", Share(c => Value(c, K("accessStops")) is null or > 400), "%"));
            kpis.Add(new KpiDto("noAccessibleToilet800", Share(c => Value(c, K("accessToilets")) is null or > 800), "%"));
            kpis.Add(new KpiDto("noRest300", Share(c => Value(c, K("rest")) is null or > 300), "%"));
        }

        var openReports = cells.Sum(c => c.OpenReports);
        kpis.Add(new KpiDto("openReports", openReports, "reports"));

        // Score distribution in 10 bins.
        var histogram = Enumerable.Range(0, 10)
            .Select(i => new HistogramBinDto(i * 10, i * 10 + 10, cells.Count(c => Math.Min(9, (int)(EventScore(c) / 10)) == i)))
            .ToList();

        // Which factors leave the most gaps.
        var gaps = layers
            .SelectMany(SafetyModel.FactorsOf)
            .Where(def => def.Weight > 0)
            .Where(def => grid.Model.AccessStopsAvailable || AccessKeys.Base(def.Key) != "accessStops" || !AccessKeys.IsAccessKey(def.Key))   // not available: no gap from it
            .Select(def =>
            {
                var results = cells.Select(c => (Cell: c, Factor: c.Measure.Factors(def.Layer).First(f => f.Definition.Key == def.Key))).ToList();
                return new FactorGapDto(
                    def.Key,
                    SafetyMapping.LabelOf(def.Key),
                    def.Layer.ToString(),
                    totalExposure == 0 ? 0 : Math.Round(100.0 * results.Where(r => r.Factor.IsWeak).Sum(r => r.Cell.Measure.Exposure) / totalExposure, 1),
                    totalExposure == 0 ? 0 : Math.Round(results.Sum(r => r.Factor.Score * r.Cell.Measure.Exposure) / totalExposure, 1));
            })
            .OrderByDescending(g => g.WeakShare)
            .ToList();

        // Where to act first.
        var topCells = cells
            .OrderByDescending(c => c.Priority)
            .ThenBy(c => c.Goodness(evt))
            .Take(Math.Clamp(top, 1, 50))
            .Select(c => new PriorityCellDto(
                c.Measure.CellId,
                Math.Round(c.Measure.Point.Latitude, 6),
                Math.Round(c.Measure.Point.Longitude, 6),
                Math.Round(c.Priority),
                Math.Round(c.Heat),
                Math.Round(c.Safety),
                Math.Round(c.Combined),
                Math.Round(c.Measure.Exposure * 100),
                c.OpenReports,
                layers.SelectMany(c.Measure.Factors).Where(f => f.IsWeak && f.Definition.Weight > 0).OrderByDescending(f => f.Definition.Weight * (100 - f.Score))
                    .Select(f => f.Definition.Key).ToList(),
                SuggestedActions.For(c.Measure, c, evt),
                grid.Model.LabelFor(c.Measure.Point),
                Math.Round(c.Flood),
                Math.Round(c.Air),
                isAccess ? Math.Round(EventScore(c)) : null))
            .ToList();

        // Reports and alerts.
        var reportRows = ReportRules.All.Select(r => r.Type).Where(type => ScoreService.InEvent(type, evt)).ToList();
        var allReports = grid.Cells.Count == 0 ? [] : (await scores.OpenReportsByCellAsync(ct)).Values.SelectMany(r => r).ToList();
        var reportCounts = reportRows.Select(type =>
        {
            var ofType = allReports.Where(r => r.Type == type).ToList();
            return new ReportCountDto(type.ToString(), ofType.Count, ofType.Count(r => r.CreatedAt >= now.AddHours(-24)), ofType.Count(r => r.VerifiedByPlanner));
        }).ToList();

        var alerts = await store.ListAlertsAsync(ct);
        var notes = new List<string>
        {
            "Scores are environmental (mapped infrastructure + citizen reports), not crime statistics.",
            "Exposure is a proxy from lamp and stop counts; there is no open population grid."
        };
        var waterPoints = grid.Model.FeatureCount("water");
        if (waterPoints < 300)
            notes.Add($"Only {waterPoints} drinking fountains and taps are mapped in OpenStreetMap for a city of about 800,000 people, so many areas score low for water partly because points are missing from the map, not only because water is absent. Adding them in OpenStreetMap, or a list from the water utility or the city, would improve the heat-relief score.");
        if (evt == PlanningEvent.Flood) notes.Add("Flood scores use distance from mapped rivers and streams, emergency services and exits, plus live IMGW river levels. There is no elevation or official flood-hazard map in this model yet, so low-lying areas away from a river are not flagged.");
        if (evt == PlanningEvent.Air) notes.Add("Air scores use distance from mapped main roads, trees and parks, indoor places, plus live GIOŚ PM2.5 readings. Traffic volume and industrial sources are not known.");
        if (isAccess)
        {
            var profile = ScoreService.AccessProfileOf(evt)!;
            notes.Add($"Accessibility ({profile.Key} profile): scores come from OpenStreetMap and ZTP data for the downloaded area only. {grid.Cells.Count - cells.Count} of {grid.Cells.Count} built-up squares have no accessibility data; they are left out of every number here and are never counted as accessible. Choose another profile with the profile parameter.");
            if (!grid.Model.AccessStopsAvailable)
                notes.Add("No stop in the ZTP open data is flagged wheelchair-accessible (wheelchair_boarding), so the accessible-stops factor is not available: it is left out and the other factors' weights are rescaled to 100. It counts again as soon as any stop is flagged.");
        }

        if (grid.Model.DataGaps.Count > 0) notes.Add("Some datasets are still loading, so scores may be incomplete: " + string.Join(", ", grid.Model.DataGaps) + ".");

        return new PlannerSummaryDto(
            ScoreService.EventName(evt),
            now,
            grid.Conditions,
            cells.Count,
            kpis,
            histogram,
            gaps,
            topCells,
            reportCounts,
            alerts.Count(a => a.IsActiveAt(now)),
            presence.CountActive(),
            notes,
            isAccess ? ScoreService.AccessProfileOf(evt)!.Key : null,
            isAccess ? ScoreService.AccessInfo(grid, evt) : null);
    }
}
