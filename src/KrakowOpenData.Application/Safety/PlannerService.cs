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
        var cells = grid.Cells;
        var now = grid.GeneratedAt;

        // The score shown for the event: the cooling score for Heat, safety score for Night, flood safety, clean air; every score is higher = better.
        double EventScore(ScoredCell c) => evt switch
        {
            PlanningEvent.Heat => c.Heat,
            PlanningEvent.Night => c.Safety,
            PlanningEvent.Flood => c.Flood,
            PlanningEvent.Air => c.Air,
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
        double? Value(ScoredCell c, string key) => c.Measure.Factors(key is "lighting" or "nightTransit" or "openPlaces" or "aed" ? Layer.Safety : key is "river" or "emergency" or "evacuation" ? Layer.Flood : key is "traffic" or "trees" or "cleanIndoor" ? Layer.Air : Layer.Heat)
            .First(f => f.Definition.Key == key).Value;

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

        var openReports = cells.Sum(c => c.OpenReports);
        kpis.Add(new KpiDto("openReports", openReports, "reports"));

        // Score distribution in 10 bins.
        var histogram = Enumerable.Range(0, 10)
            .Select(i => new HistogramBinDto(i * 10, i * 10 + 10, cells.Count(c => Math.Min(9, (int)(EventScore(c) / 10)) == i)))
            .ToList();

        // Which factors leave the most gaps.
        var gaps = layers
            .SelectMany(SafetyModel.FactorsOf)
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
                layers.SelectMany(c.Measure.Factors).Where(f => f.IsWeak).OrderByDescending(f => f.Definition.Weight * (100 - f.Score))
                    .Select(f => f.Definition.Key).ToList(),
                SuggestedActions.For(c.Measure, c, evt),
                grid.Model.LabelFor(c.Measure.Point),
                Math.Round(c.Flood),
                Math.Round(c.Air)))
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
            notes);
    }
}
