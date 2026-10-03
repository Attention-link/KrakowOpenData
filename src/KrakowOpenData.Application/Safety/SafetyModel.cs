namespace KrakowOpenData.Application.Safety;

/// <summary>
/// THE SCORING MODEL. Every number the UI shows (heat score, safety score, priority, bands) is computed here,
/// from plain functions with documented constants, so it can be read, challenged and tuned in one place.
///
/// <para><b>What the scores are.</b> Two scores per place, both 0–100, each named for what it measures:</para>
/// <list type="bullet">
/// <item><b>Heat score</b> (0 = cool, 100 = very hot): how much heat stress a place carries on a hot day, because it lacks the means to cool down:
/// shade/green, water, indoor refuge, toilets and a way to reach relief. <b>Higher = hotter / worse.</b> Internally the model first measures the
/// place's <i>cooling capacity</i> (higher = better) and the heat score is 100 minus that (<see cref="HeatScore"/>).</item>
/// <item><b>Safety score (night)</b> (0 = unsafe, 100 = safe): how well the place is set up for walking at night: street lighting, night
/// public transport, places that are open and staffed at night, and a defibrillator close by. <b>Higher = safer.</b></item>
/// </list>
/// <para><b>What they are not.</b> They are environmental scores built from mapped infrastructure and citizen
/// reports. They are <b>not crime statistics</b> and not measured temperatures or light levels: Kraków publishes no
/// open, geolocated incident data, and OpenStreetMap may miss some lamps and fountains.</para>
///
/// <para><b>Calculation, step by step</b> (see the methods below):</para>
/// <list type="number">
/// <item>The city is cut into square cells of about 250 m (<see cref="GridSpec"/>).</item>
/// <item>For each cell we measure <b>factors</b>: the straight-line distance to the nearest feature (e.g. nearest
/// drinking-water point) or a density (street lamps per km² in the 3×3 cells around it).</item>
/// <item>Each factor becomes a 0–100 <b>factor score</b>: 100 when the feature is close (≤ <c>FullWithin</c>),
/// 0 when far (≥ <c>ZeroBeyond</c>), a straight line in between (<see cref="Proximity"/>, <see cref="Density"/>).</item>
/// <item>The layer's <b>base score</b> is the weighted sum of its factor scores (weights add up to 100). For heat this is the cooling capacity.</item>
/// <item>Open citizen reports subtract a <b>report penalty</b> (<see cref="ReportRules"/>), capped at
/// <see cref="MaxReportPenalty"/> points.</item>
/// <item><b>Layer score</b> = base score − penalty, clamped to 0–100. Bands: Good ≥ 75, Fair ≥ 55, Weak ≥ 35, else Critical.</item>
/// <item><b>Combined score</b> (when the user wants "both") = 0.6 × the lower layer + 0.4 × the average of the two
/// (<see cref="Combine"/>): a place is only as good as its weak side.</item>
/// <item><b>Exposure</b> (0.2–1.0) estimates how many people the place affects, from lamp and stop counts around it
/// (<see cref="Exposure"/>), because there is no open population grid or district boundary data.</item>
/// <item><b>Priority</b> (planner view, 0–100) = (100 − score) × exposure × live pressure (<see cref="Priority"/>):
/// the worse a place scores, the more people it touches and the hotter / more urgent the situation, the higher it ranks.</item>
/// </list>
/// All thresholds are first estimates for Kraków and meant to be tuned; change them here only.
/// </summary>
public static class SafetyModel
{
    /// <summary>Most points the citizen reports can take off one layer of one cell.</summary>
    public const double MaxReportPenalty = 30;

    public const double GoodFrom = 75;
    public const double FairFrom = 55;
    public const double WeakFrom = 35;

    /// <summary>A factor scoring below this counts as "weak" (shown as a gap and used to suggest actions).</summary>
    public const double WeakFactorBelow = 35;

    /// <summary>
    /// Features farther than this are treated as "none nearby" (factor score 0, no distance shown).
    /// Keeps the nearest-feature search cheap and the numbers walkable.
    /// </summary>
    public const double SearchRadiusMeters = 1500;

    /// <summary>Average walking speed used for "x min walk": 80 m per minute (4.8 km/h).</summary>
    public const double WalkMetersPerMinute = 80;

    // ── Factor definitions ───────────────────────────────────────────────────

    /// <summary>
    /// HEAT factors (weights add up to 100). Distances are straight-line metres.
    /// <list type="bullet">
    /// <item><c>water</c> 30: nearest drinking-water point (OSM <c>amenity=drinking_water</c>). Full ≤ 150 m, zero ≥ 800 m.</item>
    /// <item><c>green</c> 30: nearest park, measured to its edge (centre distance minus the park's equivalent radius).
    /// Shade proxy: there is no tree-canopy data. Full ≤ 100 m, zero ≥ 600 m.</item>
    /// <item><c>refuge</c> 20: nearest indoor public place assumed to be cooler: library, pharmacy or hospital.
    /// Opening hours are not checked. Full ≤ 200 m, zero ≥ 900 m.</item>
    /// <item><c>toilets</c> 10: nearest public toilet. Full ≤ 200 m, zero ≥ 800 m.</item>
    /// <item><c>transit</c> 10: nearest public transport stop, i.e. a way to reach relief farther away. Full ≤ 150 m, zero ≥ 600 m.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<FactorDefinition> HeatFactors { get; } =
    [
        new("water", Layer.Heat, 25, FactorKind.Distance, 150, 800),
        new("green", Layer.Heat, 35, FactorKind.Distance, 100, 600),
        new("refuge", Layer.Heat, 20, FactorKind.Distance, 200, 900),
        new("toilets", Layer.Heat, 10, FactorKind.Distance, 200, 800),
        new("transit", Layer.Heat, 10, FactorKind.Distance, 150, 600)
    ];

    /// <summary>
    /// NIGHT-SAFETY factors (weights add up to 100).
    /// <list type="bullet">
    /// <item><c>lighting</c> 40: street lamps per km² in the 3×3 cells around the place (OSM <c>highway=street_lamp</c>).
    /// Zero ≤ 40 /km², full ≥ 400 /km². Lamp density, not lux: it does not see lamps that are broken or badly aimed.</item>
    /// <item><c>nightTransit</c> 25: nearest stop that has scheduled departures between 23:00 and 04:30 (GTFS).
    /// Full ≤ 250 m, zero ≥ 1000 m.</item>
    /// <item><c>openPlaces</c> 20: nearest place that is staffed at night: police station, hospital, or a pharmacy
    /// tagged <c>opening_hours=24/7</c>. Full ≤ 250 m, zero ≥ 1000 m.</item>
    /// <item><c>aed</c> 15: nearest defibrillator. Whether it is reachable at night is unknown. Full ≤ 100 m, zero ≥ 500 m.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<FactorDefinition> SafetyFactors { get; } =
    [
        new("lighting", Layer.Safety, 40, FactorKind.Density, 400, 40),
        new("nightTransit", Layer.Safety, 25, FactorKind.Distance, 250, 1000),
        new("openPlaces", Layer.Safety, 20, FactorKind.Distance, 250, 1000),
        new("aed", Layer.Safety, 15, FactorKind.Distance, 100, 500)
    ];

    public static IReadOnlyList<FactorDefinition> FactorsOf(Layer layer) => layer == Layer.Heat ? HeatFactors : SafetyFactors;

    // ── Building blocks ──────────────────────────────────────────────────────

    /// <summary>
    /// 100 when <paramref name="distanceMeters"/> ≤ <paramref name="fullWithin"/>, 0 when ≥ <paramref name="zeroBeyond"/>,
    /// a straight line in between. <c>null</c> (nothing within the search radius) scores 0.
    /// Example: full 150, zero 800, distance 475 → 50.
    /// </summary>
    public static double Proximity(double? distanceMeters, double fullWithin, double zeroBeyond)
    {
        if (distanceMeters is not { } d) return 0;
        if (d <= fullWithin) return 100;
        if (d >= zeroBeyond) return 0;
        return 100 * (zeroBeyond - d) / (zeroBeyond - fullWithin);
    }

    /// <summary>
    /// Density scores rise with the value: 0 at or below <paramref name="zeroAt"/>, 100 at or above
    /// <paramref name="fullAt"/>, a straight line in between.
    /// </summary>
    public static double Density(double perKm2, double fullAt, double zeroAt)
    {
        if (perKm2 >= fullAt) return 100;
        if (perKm2 <= zeroAt) return 0;
        return 100 * (perKm2 - zeroAt) / (fullAt - zeroAt);
    }

    /// <summary>Scores one factor from the measured <paramref name="value"/> (metres for distance factors, lamps/km² for density).</summary>
    public static double FactorScore(FactorDefinition factor, double? value) =>
        factor.Kind == FactorKind.Density
            ? Density(value ?? 0, factor.FullWithin, factor.ZeroBeyond)
            : Proximity(value, factor.FullWithin, factor.ZeroBeyond);

    /// <summary>Weighted sum of factor scores. The weights of a layer add up to 100, so the result is 0–100.</summary>
    public static double BaseScore(IEnumerable<FactorResult> factors) => factors.Sum(f => f.Points);

    /// <summary>Layer score after reports: base − penalty, clamped to 0–100.</summary>
    public static double LayerScore(double baseScore, double reportPenalty) =>
        Math.Clamp(baseScore - Math.Min(reportPenalty, MaxReportPenalty), 0, 100);

    /// <summary>
    /// The heat score people see: 100 − <paramref name="coolingCapacity"/> (the layer score after reports). 0 = plenty of ways to cool down,
    /// 100 = none. Open heat reports lower the cooling capacity, so they raise the heat score.
    /// </summary>
    public static double HeatScore(double coolingCapacity) => Math.Clamp(100 - coolingCapacity, 0, 100);

    /// <summary>Band of a heat score: Good = low heat stress (≤ 25), Fair ≤ 45, Weak ≤ 65, Critical above that.</summary>
    public static ScoreBand HeatBand(double heatScore) => Band(100 - heatScore);

    /// <summary>"Weakest link" blend for the "both" view: 0.6 × lower score + 0.4 × average. Takes the cooling capacity (not the heat score) and the safety score, so both point the same way (higher = better).</summary>
    public static double Combine(double coolingCapacity, double safety) =>
        0.6 * Math.Min(coolingCapacity, safety) + 0.4 * ((coolingCapacity + safety) / 2);

    public static ScoreBand Band(double score) =>
        score >= GoodFrom ? ScoreBand.Good : score >= FairFrom ? ScoreBand.Fair : score >= WeakFrom ? ScoreBand.Weak : ScoreBand.Critical;

    /// <summary>
    /// How many people a place plausibly affects, 0.2–1.0: (lamps in the 3×3 cells + 3 × stops in the 3×3 cells) / 200.
    /// A proxy for built-up, walked-on streets, because the API has no population grid or district polygons.
    /// The floor of 0.2 keeps thinly built areas on the map.
    /// </summary>
    public static double Exposure(int lampsAround, int stopsAround) =>
        Math.Clamp((lampsAround + 3.0 * stopsAround) / 200.0, 0.2, 1.0);

    /// <summary>
    /// Planner priority 0–100: (100 − score) × exposure × <paramref name="pressure"/>, capped at 100.
    /// <paramref name="score"/> is the heat, safety or combined score depending on the event being planned for.
    /// </summary>
    public static double Priority(double score, double exposure, double pressure) =>
        Math.Clamp((100 - score) * exposure * pressure, 0, 100);

    /// <summary>
    /// Live pressure multiplier for the heat layer, from the current weather (IMGW):
    /// none 0.8 · hot day (≥ 30 °C, no warning) 1.0 · warning level 1 → 1.1 · level 2 → 1.25 · level 3 → 1.5.
    /// The night layer uses 1.0 (<see cref="NightPressure"/>).
    /// </summary>
    public static double HeatPressureFactor(HeatPressure pressure) => pressure switch
    {
        HeatPressure.None => 0.8,
        HeatPressure.HotDay => 1.0,
        HeatPressure.WarningLevel1 => 1.1,
        HeatPressure.WarningLevel2 => 1.25,
        HeatPressure.WarningLevel3 => 1.5,
        _ => 1.0
    };

    public const double NightPressure = 1.0;
}

public enum Layer
{
    Heat,
    Safety
}

public enum FactorKind
{
    /// <summary>Straight-line metres to the nearest feature; closer is better.</summary>
    Distance,

    /// <summary>A count per km²; higher is better.</summary>
    Density
}

public enum ScoreBand
{
    Critical,
    Weak,
    Fair,
    Good
}

/// <summary>Live heat situation, from IMGW weather and warnings.</summary>
public enum HeatPressure
{
    None,
    HotDay,
    WarningLevel1,
    WarningLevel2,
    WarningLevel3
}

/// <param name="FullWithin">Distance (metres) up to which a distance factor scores 100; for a density factor, the density (per km²) that scores 100.</param>
/// <param name="ZeroBeyond">Distance (metres) from which a distance factor scores 0; for a density factor, the density that scores 0.</param>
public sealed record FactorDefinition(string Key, Layer Layer, double Weight, FactorKind Kind, double FullWithin, double ZeroBeyond);

/// <summary>One factor measured for one place: the raw value, its 0–100 score and its contribution to the layer score.</summary>
public sealed record FactorResult(FactorDefinition Definition, double? Value, double Score, string? NearestName)
{
    /// <summary>Weight × score / 100 (points added to the layer's base score).</summary>
    public double Points => Definition.Weight * Score / 100;

    public bool IsWeak => Score < SafetyModel.WeakFactorBelow;
}
