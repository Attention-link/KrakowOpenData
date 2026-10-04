using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Application.Safety;

/// <summary>
/// Walking routes along real streets, so a walker can compare the fastest way with a safer or cooler one.
///
/// <para><b>How the better route is found.</b> The street router (OpenStreetMap, foot profile) gives the fastest route and its own
/// alternatives. Because those alternatives are often near-identical, a few more candidates are made by routing through a "via" point
/// pushed to the left and right of the middle of the straight line (a 15 % and a 30 % bend). Every candidate is scored every 50 m with the same
/// model as the map. A candidate is eligible when it is at most 30 % (and at least 300 m) longer than the fastest route. The winner maximises
/// <c>0.7 Ã— average + 0.3 Ã— worst</c> of the mode's "goodness" (night: safety score; heat: 100 âˆ’ heat score; both: overall score).
/// It is shown only when it beats the fastest route by at least <see cref="MinGain"/> points on average; otherwise the fastest is also the best.</para>
///
/// <para>Scores come from mapped lighting, night transport, open places, shade, water and citizen reports, not from crime or measured
/// temperature. If the street router cannot be reached, only a straight-line corridor check is returned.</para>
/// </summary>
public sealed class RouteService(ScoreService scores, IWalkingRouter router, RouteThresholdService? thresholds = null)
{
    public const double MaxStraightMeters = 5000;
    public const double MinGain = 3;
    public const double MaxExtraShare = 0.30;
    public const double MinExtraMeters = 300;

    /// <summary>
    /// When is the fastest route "not that unsafe"? When, in the mode's goodness (heat is turned around), its average is at least
    /// <see cref="AcceptableAverage"/> AND its weakest stretch is at least <see cref="AcceptableWorst"/> (that is, no stretch is in the Weak
    /// band's lower half or worse). Then a small, nearby improvement is all that is offered.
    /// These are the DEFAULTS: a city planner can set both numbers per safety measure (see <see cref="RouteThresholdService"/>).
    /// </summary>
    public const double AcceptableAverage = 65;

    public const double AcceptableWorst = 45;

    /// <summary>
    /// When the fastest route is NOT acceptable, safety wins over distance: the search widens (more and farther via points) and a
    /// detour of up to its own length again (at least <see cref="MinExtraMeters"/>, at most <see cref="WideMaxExtraMeters"/>) is allowed.
    /// </summary>
    public const double WideMaxExtraShare = 1.0;

    public const double WideMaxExtraMeters = 3000;
    public const double WideMinExtraMeters = 1000;

    public async Task<RoutesDto> GetRoutesAsync(GeoPoint from, GeoPoint to, PlanningEvent mode, CancellationToken ct = default)
    {
        var straight = GridSpec.Distance(from, to);
        if (straight > MaxStraightMeters)
            throw new SafetyValidationException("to", $"Routes are limited to {MaxStraightMeters / 1000:0} km between the two points.");

        // Read on every request, so a planner's change applies at once.
        var limit0 = thresholds is null ? RouteThresholds.Default : await thresholds.ForAsync(mode, ct);

        IReadOnlyList<RoutePath> candidates;
        try
        {
            candidates = await CandidatesAsync(from, to, straight, ct);
        }
        // An HttpClient timeout is a TaskCanceledException too: only the caller's own cancellation may escape, a slow router
        // must fall back to the straight-line check instead of failing the request.
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is not SafetyValidationException)
        {
            candidates = [];
        }

        if (candidates.Count == 0)
        {
            var line = new RoutePath([from, to], straight);
            var only = await DescribeAsync("fastest", line, mode, ct);
            return new RoutesDto(ScoreService.EventName(mode), "straight-line", only, null, "none", 0, 0, 0,
                "Street routing is unavailable right now, so this is a straight-line check, not a route. Try again shortly.",
                false, true, limit0.Average, limit0.Worst, ScoreService.AccessProfileOf(mode)?.Key);
        }

        var scored = new List<(RoutePath Path, RouteOptionDto Option)>();
        foreach (var path in candidates) scored.Add((path, await DescribeAsync("candidate", path, mode, ct)));

        var fastest = scored.MinBy(c => c.Path.DistanceMeters);   // shortest walk = fastest at a constant walking speed
        var profile = ScoreService.AccessProfileOf(mode)?.Key;

        if (ScoreService.IsAccess(mode) && fastest.Option.NoDataShare >= 0.999)
        {
            // Nothing is known about accessibility along this route: say so and do not search for a "better" one in the dark.
            return new RoutesDto(ScoreService.EventName(mode), "street", fastest.Option with { Kind = "fastest" }, null, "none", 0, 0, 0,
                "There is no accessibility data along this route (outside the downloaded area or nothing mapped). That is not the same as accessible: check the way on site.",
                false, false, limit0.Average, limit0.Worst, profile);
        }

        // Is the fastest route good enough? If not, safety matters more than distance: look farther and allow a longer detour.
        var acceptable = IsAcceptable(fastest.Option, mode, limit0);
        var widened = false;
        if (!acceptable && straight > 150)
        {
            widened = true;
            var wide = await Task.WhenAll(WideViaPoints(from, to, straight).Select(v => TryRouteAsync([from, v, to], ct)));
            foreach (var path in wide.SelectMany(p => p).OrderBy(p => p.DistanceMeters))
            {
                if (scored.Any(s => Math.Abs(s.Path.DistanceMeters - path.DistanceMeters) < 25)) continue;
                scored.Add((path, await DescribeAsync("candidate", path, mode, ct)));
            }
        }

        var extra = widened
            ? Math.Clamp(Math.Max(WideMinExtraMeters, fastest.Path.DistanceMeters * WideMaxExtraShare), MinExtraMeters, WideMaxExtraMeters)
            : Math.Max(MinExtraMeters, fastest.Path.DistanceMeters * MaxExtraShare);
        var limit = fastest.Path.DistanceMeters + extra;
        var best = scored
            .Where(c => !ReferenceEquals(c.Path, fastest.Path) && c.Path.DistanceMeters <= limit)
            .OrderByDescending(c => Objective(c.Option, mode))
            .Select(c => ((RoutePath, RouteOptionDto)?)c)
            .FirstOrDefault();

        var fastestOption = fastest.Option with { Kind = "fastest" };
        var kind = BetterKind(mode);
        var gain = best is { } b ? Gain(b.Item2, fastest.Option, mode) : 0;
        var hasBetter = best is not null && gain >= MinGain;
        var betterOption = hasBetter ? best!.Value.Item2 with { Kind = kind } : null;

        return new RoutesDto(
            ScoreService.EventName(mode),
            "street",
            fastestOption,
            betterOption,
            hasBetter ? kind : "none",
            hasBetter ? Math.Round(gain, 1) : 0,
            hasBetter ? Math.Round(betterOption!.LengthMeters - fastestOption.LengthMeters) : 0,
            hasBetter ? betterOption!.WalkingMinutes - fastestOption.WalkingMinutes : 0,
            hasBetter
                ? (widened
                    ? "The fastest route scores poorly here, so a safer route was searched for farther away. It is longer, but it scores better along the way."
                    : "The route is longer but scores better on average along the way.")
                : (widened
                    ? "The fastest route scores poorly and no street route within reach is clearly better. Take extra care or consider another way to travel."
                    : "No street route nearby scores clearly better, so the fastest route is also the best option."),
            widened,
            acceptable,
            limit0.Average,
            limit0.Worst,
            profile);
    }

    /// <summary>True when the route is good enough with the default thresholds (see <see cref="AcceptableAverage"/>).</summary>
    public static bool IsAcceptable(RouteOptionDto route, PlanningEvent mode) => IsAcceptable(route, mode, RouteThresholds.Default);

    /// <summary>True when the route is good enough, under the given thresholds, that only a modest, nearby improvement is worth offering.</summary>
    public static bool IsAcceptable(RouteOptionDto route, PlanningEvent mode, RouteThreshold threshold) =>
        Goodness(route.Average, mode) >= threshold.Average && Goodness(route.Worst, mode) >= threshold.Worst && route.NoDataShare <= MaxAcceptableNoData;

    /// <summary>A route whose accessibility is unknown for more than this share is not "acceptable": no data is not accessibility.</summary>
    public const double MaxAcceptableNoData = 0.5;

    /// <summary>
    /// Via points for the wide search: left and right of the middle of the straight line at 50 %, 80 % and 120 % of its length
    /// (the offset is limited to 120 m–2.5 km), so a detour can swing well away from a poor area.
    /// </summary>
    public static IReadOnlyList<GeoPoint> WideViaPoints(GeoPoint from, GeoPoint to, double straightMeters) =>
        ViaPointsAt(from, to, straightMeters, [0.5, 0.8, 1.2], 2500);

    public static string BetterKind(PlanningEvent mode) => mode switch
    {
        PlanningEvent.Heat => "coolest",
        PlanningEvent.Night => "safest",
        PlanningEvent.Flood => "driest",
        PlanningEvent.Air => "cleanest",
        PlanningEvent.Access or PlanningEvent.AccessPram or PlanningEvent.AccessMobility => "mostAccessible",
        _ => "balanced"
    };

    /// <summary>The fastest route (with the router's alternatives) plus via-point candidates, without duplicates.</summary>
    private async Task<IReadOnlyList<RoutePath>> CandidatesAsync(GeoPoint from, GeoPoint to, double straight, CancellationToken ct)
    {
        var direct = await router.RouteAsync([from, to], true, ct);
        if (direct.Count == 0) return [];

        var all = new List<RoutePath>(direct);
        if (straight > 150)
        {
            var vias = ViaPoints(from, to, straight).Select(v => TryRouteAsync([from, v, to], ct)).ToList();
            foreach (var routes in await Task.WhenAll(vias)) all.AddRange(routes);
        }

        // Drop near-duplicates: same length (within 25 m) as a route already kept.
        var unique = new List<RoutePath>();
        foreach (var p in all.OrderBy(p => p.DistanceMeters))
            if (!unique.Any(u => Math.Abs(u.DistanceMeters - p.DistanceMeters) < 25)) unique.Add(p);
        return unique;
    }

    private async Task<IReadOnlyList<RoutePath>> TryRouteAsync(IReadOnlyList<GeoPoint> waypoints, CancellationToken ct)
    {
        try
        {
            return await router.RouteAsync(waypoints, false, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)   // a timed-out via route is just one candidate fewer
        {
            return [];
        }
    }

    /// <summary>Four points to bend the route through: left and right of the middle of the straight line, at 15 % and 30 % of its length.</summary>
    public static IReadOnlyList<GeoPoint> ViaPoints(GeoPoint from, GeoPoint to, double straightMeters) =>
        ViaPointsAt(from, to, straightMeters, [0.15, 0.30], 900);

    private static IReadOnlyList<GeoPoint> ViaPointsAt(GeoPoint from, GeoPoint to, double straightMeters, double[] shares, double maxOffset)
    {
        var mid = new GeoPoint((from.Latitude + to.Latitude) / 2, (from.Longitude + to.Longitude) / 2);
        // Perpendicular direction in metres (x east, y north), then back to degrees.
        const double mLat = 111_320;
        var mLon = mLat * Math.Cos(mid.Latitude * Math.PI / 180);
        var dx = (to.Longitude - from.Longitude) * mLon;
        var dy = (to.Latitude - from.Latitude) * mLat;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return [];
        var px = -dy / len;
        var py = dx / len;

        var result = new List<GeoPoint>();
        foreach (var share in shares)
        {
            var offset = Math.Clamp(straightMeters * share, 120, maxOffset);
            foreach (var side in new[] { 1, -1 })
                result.Add(new GeoPoint(mid.Latitude + side * py * offset / mLat, mid.Longitude + side * px * offset / mLon));
        }

        return result;
    }

    private async Task<RouteOptionDto> DescribeAsync(string kind, RoutePath path, PlanningEvent mode, CancellationToken ct)
    {
        var (samples, reports) = await scores.SamplePathAsync(path.Points, ct);
        if (ScoreService.IsAccess(mode))
        {
            // Accessibility: only samples WITH data are averaged; samples with no data (outside the downloaded area, nothing mapped) are never counted as
            // accessible. NoDataShare says how much of the route is unknown.
            var known = samples.Select((s, i) => (Value: AccessValue(s, mode), Index: i)).Where(x => x.Value is not null).ToList();
            var noData = samples.Count == 0 ? 1 : 1 - (double)known.Count / samples.Count;
            var weakest = known.Count == 0 ? (Value: (double?)0, Index: 0) : known.MinBy(x => x.Value!.Value);
            return new RouteOptionDto(
                kind,
                Math.Round(path.DistanceMeters),
                (int)Math.Ceiling(path.DistanceMeters / SafetyModel.WalkMetersPerMinute),
                Simplify(path.Points),
                samples,
                known.Count == 0 ? 0 : Math.Round(known.Average(x => x.Value!.Value), 1),
                weakest.Value ?? 0,
                weakest.Index,
                reports,
                Math.Round(noData, 3));
        }

        var values = samples.Select(s => ModeValue(s, mode)).ToList();
        var worstIndex = WorstIndex(values, mode);
        return new RouteOptionDto(
            kind,
            Math.Round(path.DistanceMeters),
            (int)Math.Ceiling(path.DistanceMeters / SafetyModel.WalkMetersPerMinute),
            Simplify(path.Points),
            samples,
            Math.Round(values.Average(), 1),
            values[worstIndex],
            worstIndex,
            reports);
    }

    /// <summary>The value of a sample in the scale of the mode: safety, heat (higher = hotter) or overall.</summary>
    public static double ModeValue(CorridorSampleDto s, PlanningEvent mode) => mode switch
    {
        PlanningEvent.Heat => s.Heat,
        PlanningEvent.Night => s.Safety,
        PlanningEvent.Flood => s.Flood,
        PlanningEvent.Air => s.Air,
        PlanningEvent.Access => s.Access ?? 0,
        PlanningEvent.AccessPram => s.AccessPram ?? 0,
        PlanningEvent.AccessMobility => s.AccessMobility ?? 0,
        _ => s.Combined
    };

    /// <summary>The accessibility score of a sample for an access mode; null for samples with no accessibility data (and for other modes).</summary>
    public static double? AccessValue(CorridorSampleDto s, PlanningEvent mode) => mode switch
    {
        PlanningEvent.Access => s.Access,
        PlanningEvent.AccessPram => s.AccessPram,
        PlanningEvent.AccessMobility => s.AccessMobility,
        _ => null
    };

    /// <summary>Same value turned around so that higher is always better (heat 100 â†’ 0).</summary>
    private static double Goodness(double value, PlanningEvent mode) => value;

    private static int WorstIndex(IReadOnlyList<double> values, PlanningEvent mode)
    {
        var worst = 0;
        for (var i = 1; i < values.Count; i++)
            if (Goodness(values[i], mode) < Goodness(values[worst], mode)) worst = i;
        return worst;
    }

    /// <summary>Points taken off a route's objective per whole route of unknown accessibility: a route with no data must not win by being unknown.</summary>
    public const double NoDataPenalty = 30;

    private static double Objective(RouteOptionDto o, PlanningEvent mode) =>
        0.7 * Goodness(o.Average, mode) + 0.3 * Goodness(o.Worst, mode) - NoDataPenalty * o.NoDataShare;

    private static double Gain(RouteOptionDto better, RouteOptionDto fastest, PlanningEvent mode) =>
        Goodness(better.Average, mode) - Goodness(fastest.Average, mode);

    /// <summary>Keeps the drawn path light: at most about 400 points, rounded to 5 decimals (about 1 m).</summary>
    private static IReadOnlyList<double[]> Simplify(IReadOnlyList<GeoPoint> points)
    {
        var step = Math.Max(1, points.Count / 400);
        var result = new List<double[]>();
        for (var i = 0; i < points.Count; i += step) result.Add([Math.Round(points[i].Latitude, 5), Math.Round(points[i].Longitude, 5)]);
        var last = points[^1];
        if (result[^1][0] != Math.Round(last.Latitude, 5) || result[^1][1] != Math.Round(last.Longitude, 5))
            result.Add([Math.Round(last.Latitude, 5), Math.Round(last.Longitude, 5)]);
        return result;
    }
}

