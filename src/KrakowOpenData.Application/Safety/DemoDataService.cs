using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Domain.Safety;

namespace KrakowOpenData.Application.Safety;

/// <summary>
/// Fills an empty system with believable citizen reports so a demo or a screenshot is not a blank map. It picks the
/// real lowest-scoring built-up cells (so the demo reports sit where the data says help is needed) and files a handful
/// of reports there. Every demo report's note starts with "DEMO" and the planner dashboard can show that. It does
/// nothing if demo reports already exist.
/// </summary>
public sealed class DemoDataService(ScoreService scores, ISafetyStore store, IClock clock)
{
    public const string Marker = "DEMO";

    public async Task<int> SeedAsync(CancellationToken ct = default)
    {
        var existing = await store.ListReportsAsync(ct);
        if (existing.Any(r => r.Note?.StartsWith(Marker, StringComparison.Ordinal) == true)) return 0;

        var grid = await scores.ScoreGridAsync(PlanningEvent.Both, ct);
        var busy = grid.Cells.Where(c => c.Measure.Exposure >= 0.6).ToList();
        if (busy.Count < 10) busy = grid.Cells.ToList();

        var darkest = busy.OrderBy(c => c.Measure.SafetyBase).ThenBy(c => c.Measure.CellId, StringComparer.Ordinal).Take(6).ToList();
        var hottest = busy.OrderBy(c => c.Measure.HeatBase).ThenBy(c => c.Measure.CellId, StringComparer.Ordinal).Take(5).ToList();

        var now = clock.UtcNow;
        var created = 0;

        async Task Add(ScoredCell cell, ReportType type, int supporters, double hoursAgo, string note)
        {
            var devices = Enumerable.Range(1, supporters).Select(i => $"demo-device-{created:00}-{i:00}").ToList();
            var at = now.AddHours(-hoursAgo);
            await store.SaveReportAsync(new CitizenReport(
                $"rep-demo{created:000}", type, cell.Measure.Point, $"{Marker}: {note}", at, at, devices, false, ReportStatus.Open, null, null), ct);
            created++;
        }

        var lightTypes = new[] { ReportType.LightOut, ReportType.UnsafeAtNight, ReportType.LightOut, ReportType.PathHazard, ReportType.UnsafeAtNight, ReportType.LightOut };
        for (var i = 0; i < darkest.Count; i++)
            await Add(darkest[i], lightTypes[i], supporters: i % 3 == 0 ? 3 : i % 3 == 1 ? 2 : 1, hoursAgo: 3 + i * 9, "sample report for the demo");

        var heatTypes = new[] { ReportType.NoShade, ReportType.WaterNotWorking, ReportType.HeatSpot, ReportType.NoShade, ReportType.WaterNotWorking };
        for (var i = 0; i < hottest.Count; i++)
            await Add(hottest[i], heatTypes[i], supporters: i % 2 == 0 ? 2 : 1, hoursAgo: 1 + i * 5, "sample report for the demo");

        return created;
    }
}
