using KrakowOpenData.Contracts;

namespace KrakowOpenData.Application.Safety;

/// <summary>
/// Describes, in plain words and with the real numbers, how every score is built: what 0 and 100 mean, the bands, each factor with its
/// weight, thresholds, why it is weighted that way, where its data comes from and how many mapped features exist today.
/// It feeds the tooltips, legends and "how was this calculated" panels of the apps and the planner dashboard, so the explanation can
/// never drift from the model: weights and thresholds are read from <see cref="SafetyModel"/>.
/// </summary>
public sealed class MethodService(SafetyModelProvider models, ConditionsService conditions)
{
    private sealed record Text(string Measures, string Why, string Source, string? Caveat = null);

    private static readonly Dictionary<string, Text> Texts = new()
    {
        ["green"] = new(
            "Walking distance to the edge of the nearest park or green area.",
            "Shade and vegetation are the biggest difference between a street that is bearable on a hot day and one that is not: trees and green areas lower the air and surface temperature, and they cool a whole area, not only a spot. A place with no green nearby has the largest single heat penalty.",
            "OpenStreetMap parks and green areas (leisure=park, garden).",
            "There is no tree-canopy or surface-temperature data, so a park is used as a stand-in for shade; a shaded street without a park is not seen."),
        ["water"] = new(
            "Walking distance to the nearest public drinking fountain or tap.",
            "Cool drinking water matters for safety in a heatwave, but a person can carry a bottle, so it is weighted below shade.",
            "OpenStreetMap drinking fountains and taps (amenity=drinking_water: public fountains, taps and springs).",
            "Only a small number of fountains is mapped for the whole city, so some areas score badly partly because points are missing from the map."),
        ["refuge"] = new(
            "Walking distance to the nearest indoor public place that is likely to be cooler: library, pharmacy or hospital.",
            "Somewhere indoors to cool down matters most for people who cannot stay out in the heat (older people, children, people with health conditions), so it carries a medium weight.",
            "OpenStreetMap libraries, pharmacies and hospitals.",
            "Opening hours are not checked and the building is assumed to be cooler than outdoors."),
        ["toilets"] = new(
            "Walking distance to the nearest public toilet.",
            "A basic comfort and health factor on a hot day, but a smaller effect on heat itself, so a low weight.",
            "OpenStreetMap public toilets (amenity=toilets)."),
        ["transit"] = new(
            "Walking distance to the nearest public transport stop.",
            "A stop is a way to reach relief farther away (a cooler park, a pool, an air-conditioned place), but it does not cool the place itself, so a low weight.",
            "ZTP Kraków GTFS timetable (every stop)."),
        ["lighting"] = new(
            "Street lamps per km² in the 3×3 squares around the place (a 750 m × 750 m block).",
            "Light is the strongest environmental factor in how safe a street feels and is at night: it lets people see and be seen. It carries the largest weight.",
            "OpenStreetMap street lamps (highway=street_lamp).",
            "It counts mapped lamps, not brightness: broken or badly aimed lamps are not seen, and unmapped lamps are missing."),
        ["nightTransit"] = new(
            "Walking distance to the nearest stop with scheduled departures between 23:00 and 04:30.",
            "Being able to leave an area at night by public transport lowers the risk of being stranded, so it has the second largest weight.",
            "ZTP Kraków GTFS timetable (night service)."),
        ["openPlaces"] = new(
            "Walking distance to the nearest place staffed at night: police station, hospital or a pharmacy open 24/7.",
            "People and staff around, and a place to ask for help, make a street safer, but the effect is smaller than light or a way home.",
            "OpenStreetMap police stations, hospitals and 24/7 pharmacies.",
            "Only places tagged as open at night count; opening hours in OpenStreetMap may be missing or out of date."),
        ["aed"] = new(
            "Walking distance to the nearest public defibrillator (AED).",
            "A defibrillator does not prevent harm; it helps when a medical emergency happens, so it carries the smallest weight.",
            "OpenStreetMap defibrillators (emergency=defibrillator).",
            "Whether the device is reachable at night is not known.")
    };

    public async Task<MethodDto> GetAsync(CancellationToken ct = default)
    {
        var model = await models.GetAsync(ct);
        var current = await conditions.GetAsync(ct);

        IReadOnlyList<FactorInfoDto> Factors(IReadOnlyList<FactorDefinition> defs) => defs.Select(def =>
        {
            var text = Texts[def.Key];
            return new FactorInfoDto(
                def.Key,
                SafetyMapping.LabelOf(def.Key),
                def.Layer.ToString(),
                def.Weight,
                text.Measures,
                Describe(def, def.FullWithin),
                Describe(def, def.ZeroBeyond),
                text.Why,
                text.Source,
                model.FeatureCount(def.Key),
                text.Caveat);
        }).ToList();

        var heat = new LayerMethodDto(
            "Heat",
            "Heat score",
            "Higher = hotter. 0 is a cool place with every kind of relief close by; 100 is a place with none.",
            "How much heat stress a place carries on a hot day because it lacks ways to cool down. It is built from what is missing around the place (shade, water, indoor refuge, toilets, transport), not from a temperature reading, so it shows where a heatwave hurts most. The live temperature is shown next to it.",
            "Heat score = Σ weight × (100 − factor score) ÷ 100 over the five factors, + open heat reports (up to 30 points), limited to 0–100. A factor scores 100 when its nearest feature is within the 'full' distance and 0 beyond the 'zero' distance, in a straight line in between.",
            [
                new BandInfoDto("Good", 0, 25, "Low heat stress: shade, water and indoor refuge are close."),
                new BandInfoDto("Fair", 25, 45, "Moderate: most relief is within reach, some is missing."),
                new BandInfoDto("Weak", 45, 65, "High: important relief is missing or far."),
                new BandInfoDto("Critical", 65, 100, "Very high: little or no relief nearby. A hot day here is hard to cope with.")
            ],
            Factors(SafetyModel.HeatFactors));

        var safety = new LayerMethodDto(
            "Safety",
            "Night safety score",
            "Higher = safer. 0 is a place with no light, no night transport and no help nearby; 100 is well lit and well served.",
            "How well a place is set up for walking at night, from mapped lighting, night public transport, places that are open at night and defibrillators, reduced by open citizen reports. It is not a crime statistic: Kraków publishes no open, geolocated incident data.",
            "Night safety score = Σ weight × factor score ÷ 100 over the four factors, − open night-safety reports (up to 30 points), limited to 0–100.",
            [
                new BandInfoDto("Good", 75, 100, "Safe set-up: well lit, a way home and help nearby."),
                new BandInfoDto("Fair", 55, 75, "Mostly fine: one or two things are missing."),
                new BandInfoDto("Weak", 35, 55, "Poor: lighting or night transport is thin."),
                new BandInfoDto("Critical", 0, 35, "Very poor: little light, no night transport or help nearby.")
            ],
            Factors(SafetyModel.SafetyFactors));

        var kpis = new List<KpiInfoDto>
        {
            new("cells", "Squares scored", "Number of 250 m × 250 m squares that are built up (at least 3 mapped street lamps or a public transport stop) and so get a score.", "Count of grid squares with ≥ 3 mapped lamps or ≥ 1 stop.", "OpenStreetMap lamps + ZTP Kraków GTFS stops"),
            new("averageScore", "Average score", "The mean of the score of the planned event across all scored squares. For Heat this is the heat score (higher = hotter); for Night safety the safety score (higher = safer); for Both the overall score (higher = better).", "Mean of the event score over all squares (each square counts once).", "This model (SafetyModel)"),
            new("criticalCells", "Critical squares", "Squares in the worst band for the planned event: heat score of 65 or more, safety score below 35, or overall score below 35.", "Count of squares whose band is Critical.", "This model (SafetyModel)"),
            new("weakCells", "Weak squares", "Squares in the second worst band: heat score 45 to 65, safety or overall score 35 to 55.", "Count of squares whose band is Weak.", "This model (SafetyModel)"),
            new("noWater500", "No drinking fountain within 500 m", "Share of built-up area where the nearest mapped drinking fountain or tap is more than 500 m away, or none is mapped within 1.5 km.", "Σ exposure of squares with water distance > 500 m ÷ Σ exposure of all squares × 100.", "OpenStreetMap drinking-water points"),
            new("noGreen500", "No park within 500 m", "Share of built-up area where the nearest mapped park or green area is more than 500 m away.", "Σ exposure of squares with park distance > 500 m ÷ Σ exposure of all squares × 100.", "OpenStreetMap parks"),
            new("poorlyLit", "Poorly lit", "Share of built-up area where the lighting factor scores below 35 (fewer than about 165 mapped lamps per km²).", "Σ exposure of squares with lighting factor < 35 ÷ Σ exposure of all squares × 100.", "OpenStreetMap street lamps"),
            new("noNightTransit500", "No night transport within 500 m", "Share of built-up area where the nearest stop with departures between 23:00 and 04:30 is more than 500 m away.", "Σ exposure of squares with night-stop distance > 500 m ÷ Σ exposure of all squares × 100.", "ZTP Kraków GTFS timetable"),
            new("openReports", "Open citizen reports", "Reports from residents that are still open (not marked resolved by a planner), for the layers of the planned event.", "Count of open reports in the event's layers, last 30 days.", "Citizen reports filed in this app"),
            new("activeAlerts", "Active alerts", "Planner alerts that have been sent and have not yet expired.", "Count of alerts with status active and expiry in the future.", "Planner alerts created in this app"),
            new("devicesActive", "Devices reached", "Phones that asked for alerts in the last 15 minutes. It is how many people an alert could reach right now.", "Count of distinct anonymous device ids seen in the last 15 minutes.", "Alert checks from the resident app"),
            new("priority", "Priority", "How urgently a square needs action. High when the score is poor, many people are affected and conditions are demanding.", SafetyModelFormulas.Priority, "This model (SafetyModel)"),
            new("exposure", "Exposure", "A proxy for how many people a square affects.", SafetyModelFormulas.Exposure, "OpenStreetMap lamps + ZTP Kraków GTFS stops"),
            new("temperature", "Temperature", "Current air temperature at the Kraków IMGW synoptic station. It is shown beside the heat score; it is not part of the score.", "Latest synoptic observation.", "IMGW-PIB public data (danepubliczne.imgw.pl)"),
            new("air", "Air quality", "Worst PM2.5 reading among Kraków GIOŚ stations, banded.", "Highest PM2.5 across stations with a current value.", "GIOŚ national air-quality network"),
            new("daylight", "Daylight", "Sunrise and sunset for Kraków today, computed astronomically.", "Solar position at 50.06° N, 19.94° E.", "Astronomical calculation (no data feed)")
        };

        return new MethodDto(
            current.GeneratedAt,
            [safety, heat],
            SafetyModelFormulas.Combined,
            $"Each open citizen report subtracts points from its own layer in its own 250 m square: 5 × type weight × decay × credibility × support, capped at {SafetyModel.MaxReportPenalty:0} points per layer. A single unconfirmed report counts at a quarter; two or more people agreeing, or a planner verifying, counts in full. Reports fade (half-life 1 to 14 days depending on type) unless confirmed again. For heat, reports raise the heat score.",
            SafetyModelFormulas.Priority,
            SafetyModelFormulas.Exposure,
            current.Heat.TemperatureC,
            current.Heat.Pressure,
            kpis,
            [
                "Scores describe mapped infrastructure and citizen reports. They are not crime statistics and not measured temperatures or light levels.",
                "Distances are straight-line metres; walking minutes assume 80 m per minute.",
                "OpenStreetMap may miss lamps, fountains and other features, which lowers scores in those areas.",
                "Thresholds and weights are first estimates for Kraków and should be tuned with local knowledge."
            ]);
    }

    private static string Describe(FactorDefinition def, double value) =>
        def.Kind == FactorKind.Density
            ? $"{value:0} lamps per km²"
            : $"{value:0} m";
}

/// <summary>Formulas shown to people, kept next to the code that implements them.</summary>
internal static class SafetyModelFormulas
{
    public const string Combined = "Overall (both views) = 0.6 × the lower of cooling capacity (100 − heat score) and the safety score + 0.4 × their average, so a place is only as good as its weak side. Higher = better.";

    public const string Priority = "Priority = (100 − score) × exposure × pressure, limited to 100. For heat, 'score' is the cooling capacity (100 − heat score) and pressure is 0.8 with no warning, 1.0 on a hot day (30 °C or more), 1.1 / 1.25 / 1.5 for heat warnings level 1 / 2 / 3. For night safety, pressure is 1.0.";

    public const string Exposure = "Exposure (0.2–1.0) = (street lamps + 3 × stops in the 3×3 squares around) ÷ 200. Busy, built-up squares count more than thin edges. There is no population grid, so this is a proxy.";
}
