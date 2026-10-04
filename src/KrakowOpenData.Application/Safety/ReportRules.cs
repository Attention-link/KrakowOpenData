using KrakowOpenData.Domain.Safety;

namespace KrakowOpenData.Application.Safety;

/// <summary>
/// How citizen reports change a score. A report never rewrites the infrastructure facts; it subtracts points
/// from the layer it belongs to, and the effect fades and needs agreement.
///
/// <para><b>Per (cell, report type) group of open reports:</b></para>
/// <list type="number">
/// <item><b>Supporters</b> = distinct anonymous device ids among everyone who reported or confirmed ("still true").</item>
/// <item><b>Credibility</b>: a single, unconfirmed report counts at a quarter (it could be one person's mistake or a
/// prank). Two or more supporters, or a planner verification, count in full.</item>
/// <item><b>Support boost</b>: +25% for each supporter beyond the first, up to 4 supporters (+75%).</item>
/// <item><b>Decay</b>: halves every <c>HalfLifeHours</c> since the last activity (report or confirmation).
/// A light that was out 14 days ago matters half as much as one reported today; "still true" taps keep it alive.</item>
/// <item><b>Points</b> = 5 × type weight (1–3) × decay × credibility × support boost.</item>
/// </list>
/// The points of all groups of a layer in a cell are added and capped at <see cref="SafetyModel.MaxReportPenalty"/>.
/// A resolved report (a planner marked it fixed) counts for nothing. A report affects its own cell only.
///
/// <para>Examples: one fresh unconfirmed "light out" (weight 3) = 5·3·1·0.25 ≈ 3.8 points.
/// Two residents agree = 5·3·1·1.25 ≈ 18.8 points.</para>
/// </summary>
public static class ReportRules
{
    public const double PointsPerWeight = 5;
    public const double UnconfirmedCredibility = 0.25;
    public const double BoostPerExtraSupporter = 0.25;
    public const int MaxBoostedSupporters = 4;

    /// <summary>Open reports older than this are left out of lists (they no longer move the score anyway).</summary>
    public static readonly TimeSpan ListAge = TimeSpan.FromDays(30);

    private static readonly Dictionary<ReportType, ReportRule> Rules = new()
    {
        [ReportType.LightOut] = new(ScoreLayer.Safety, 3, 14 * 24, "Street light out or too dark"),
        [ReportType.UnsafeAtNight] = new(ScoreLayer.Safety, 3, 7 * 24, "Feels unsafe at night"),
        // Blocked or hazardous path, also a barrier for a wheelchair or pram (steps with no ramp, high kerb, broken lift): feeds the accessibility layer.
        [ReportType.PathHazard] = new(ScoreLayer.Access, 2, 7 * 24, "Blocked or hazardous path"),
        [ReportType.WaterNotWorking] = new(ScoreLayer.Heat, 2, 3 * 24, "Water point not working"),
        [ReportType.NoShade] = new(ScoreLayer.Heat, 2, 2 * 24, "No shade, very hot spot"),
        [ReportType.HeatSpot] = new(ScoreLayer.Heat, 3, 24, "Overheated area, no relief nearby"),
        [ReportType.FloodedStreet] = new(ScoreLayer.Flood, 3, 24, "Flooded street or underpass"),
        [ReportType.BlockedDrain] = new(ScoreLayer.Flood, 2, 7 * 24, "Blocked drain or gully"),
        [ReportType.RisingWater] = new(ScoreLayer.Flood, 3, 12, "River or stream rising fast"),
        [ReportType.SmokeOrBurning] = new(ScoreLayer.Air, 3, 24, "Smoke or burning smell"),
        [ReportType.StrongFumes] = new(ScoreLayer.Air, 2, 24, "Strong fumes or chemical smell"),
        [ReportType.DustCloud] = new(ScoreLayer.Air, 2, 12, "Dust cloud or construction dust")
    };

    public static IEnumerable<(ReportType Type, ReportRule Rule)> All => Rules.Select(r => (r.Key, r.Value));

    public static ReportRule For(ReportType type) => Rules[type];

    /// <summary>
    /// False for a value with no rule (e.g. a number that an older build let into the store). Readers skip such reports
    /// instead of failing every list and score that contains one.
    /// </summary>
    public static bool IsKnown(ReportType type) => Rules.ContainsKey(type);

    /// <summary>
    /// Points one (cell, type) group takes off its layer. <paramref name="reports"/> are the open reports of
    /// that type in the cell.
    /// </summary>
    public static double GroupPenalty(ReportType type, IReadOnlyCollection<CitizenReport> reports, DateTimeOffset now)
    {
        if (reports.Count == 0) return 0;
        var rule = For(type);

        var supporters = reports.SelectMany(r => r.DeviceIds).Distinct(StringComparer.Ordinal).Count();
        var verified = reports.Any(r => r.VerifiedByPlanner);
        var credibility = verified || supporters >= 2 ? 1.0 : UnconfirmedCredibility;
        var boost = 1 + BoostPerExtraSupporter * (Math.Min(Math.Max(supporters, 1), MaxBoostedSupporters) - 1);

        var latest = reports.Max(r => r.LastActivityAt);
        var ageHours = Math.Max(0, (now - latest).TotalHours);
        var decay = Math.Pow(0.5, ageHours / rule.HalfLifeHours);

        return PointsPerWeight * rule.Weight * decay * credibility * boost;
    }

    /// <summary>Total penalty for one layer of one cell: the sum of its groups, capped.</summary>
    public static double CellPenalty(ScoreLayer layer, IEnumerable<CitizenReport> openReportsInCell, DateTimeOffset now)
    {
        var total = openReportsInCell
            .Where(r => r.Status == ReportStatus.Open && For(r.Type).Layer == layer)
            .GroupBy(r => r.Type)
            .Sum(g => GroupPenalty(g.Key, g.ToList(), now));
        return Math.Min(total, SafetyModel.MaxReportPenalty);
    }
}

/// <param name="Weight">Seriousness 1–3 (see <see cref="ReportRules"/>).</param>
/// <param name="HalfLifeHours">Hours after which the effect halves if nobody confirms it again.</param>
public sealed record ReportRule(ScoreLayer Layer, double Weight, double HalfLifeHours, string Label);
