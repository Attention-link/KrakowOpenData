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
        ["river"] = new(
            "Straight-line distance to the nearest mapped river, stream or canal.",
            "Closeness to water is the main thing that decides whether a street can flood in a river flood, so it carries the largest weight. It rises with distance: a place on the bank scores 0, one 500 m or more away scores 100.",
            "OpenStreetMap waterways (waterway=river, stream, canal), sampled about every 120 m.",
            "There is no elevation model or official flood-hazard map in this score, so a low-lying place away from a river, and flooding from heavy rain or blocked drains, are not seen. Citizen reports fill part of that gap."),
        ["emergency"] = new(
            "Walking distance to the nearest hospital or police station.",
            "When water rises, emergency help that is close lowers the risk to people who need it, but it does not stop the flooding, so it has a medium weight.",
            "OpenStreetMap hospitals and police stations.",
            "Fire stations are not included, and a station on the other side of a river may not be reachable during a flood."),
        ["evacuation"] = new(
            "Walking distance to the nearest public transport stop.",
            "A stop is a way to leave the area when water rises, so it carries a lower weight.",
            "ZTP Kraków GTFS timetable (every stop).",
            "Services may stop in a flood; this only shows that a way out is mapped."),
        ["traffic"] = new(
            "Straight-line distance to the nearest motorway, trunk or primary road.",
            "Road traffic is the main local source of nitrogen dioxide and fine dust in a city, and the air is worst next to the road, so it carries the largest weight. It rises with distance: a place on the road scores 0, one 300 m or more away scores 100.",
            "OpenStreetMap main roads (highway=motorway, trunk, primary), sampled about every 120 m.",
            "Traffic volume is not known: a mapped main road stands in for traffic. Industry, heating and other sources are not in this score."),
        ["trees"] = new(
            "Walking distance to the edge of the nearest park or green area.",
            "Trees and green areas filter and dilute polluted air and lower its concentration near them, so they carry a large weight.",
            "OpenStreetMap parks and green areas (leisure=park).",
            "A park is used as a stand-in for the effect of trees; there is no tree-canopy data."),
        ["cleanIndoor"] = new(
            "Walking distance to the nearest library, pharmacy or hospital.",
            "On a smog day a public indoor place lets people with asthma or heart conditions get out of the polluted air, but it does not change the air outside, so it has a smaller weight.",
            "OpenStreetMap libraries, pharmacies and hospitals.",
            "Whether the building has filtered air and is open is not known."),
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
        ["river"] = new(
            "Straight-line distance to the nearest mapped river, stream or canal.",
            "Closeness to water is the main thing that decides whether a street can flood in a river flood, so it carries the largest weight. It rises with distance: a place on the bank scores 0, one 500 m or more away scores 100.",
            "OpenStreetMap waterways (waterway=river, stream, canal), sampled about every 120 m.",
            "There is no elevation model or official flood-hazard map in this score, so a low-lying place away from a river, and flooding from heavy rain or blocked drains, are not seen. Citizen reports fill part of that gap."),
        ["emergency"] = new(
            "Walking distance to the nearest hospital or police station.",
            "When water rises, emergency help that is close lowers the risk to people who need it, but it does not stop the flooding, so it has a medium weight.",
            "OpenStreetMap hospitals and police stations.",
            "Fire stations are not included, and a station on the other side of a river may not be reachable during a flood."),
        ["evacuation"] = new(
            "Walking distance to the nearest public transport stop.",
            "A stop is a way to leave the area when water rises, so it carries a lower weight.",
            "ZTP Kraków GTFS timetable (every stop).",
            "Services may stop in a flood; this only shows that a way out is mapped."),
        ["traffic"] = new(
            "Straight-line distance to the nearest motorway, trunk or primary road.",
            "Road traffic is the main local source of nitrogen dioxide and fine dust in a city, and the air is worst next to the road, so it carries the largest weight. It rises with distance: a place on the road scores 0, one 300 m or more away scores 100.",
            "OpenStreetMap main roads (highway=motorway, trunk, primary), sampled about every 120 m.",
            "Traffic volume is not known: a mapped main road stands in for traffic. Industry, heating and other sources are not in this score."),
        ["trees"] = new(
            "Walking distance to the edge of the nearest park or green area.",
            "Trees and green areas filter and dilute polluted air and lower its concentration near them, so they carry a large weight.",
            "OpenStreetMap parks and green areas (leisure=park).",
            "A park is used as a stand-in for the effect of trees; there is no tree-canopy data."),
        ["cleanIndoor"] = new(
            "Walking distance to the nearest library, pharmacy or hospital.",
            "On a smog day a public indoor place lets people with asthma or heart conditions get out of the polluted air, but it does not change the air outside, so it has a smaller weight.",
            "OpenStreetMap libraries, pharmacies and hospitals.",
            "Whether the building has filtered air and is open is not known."),
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
                text.Caveat,
                SafetyModel.FactorsOf(def.Layer).First(d => d.Key == def.Key).Weight);
        }).ToList();

        var heat = new LayerMethodDto(
            "Heat",
            "Heat-relief score",
            "Higher = more heat relief. 100 is a place with every kind of relief close by; 0 is a place with none, where a hot day is hardest to cope with.",
            "How well a place can cool down on a hot day, from the relief around it (shade, water, indoor refuge, toilets, transport). It is built from what is mapped around the place, not from a temperature reading, so it shows where a heatwave hurts most. The live temperature is shown next to it. Like every score here, higher is better.",
            "Heat-relief score = Σ weight × factor score ÷ 100 over the five factors, − open heat reports (up to 30 points), limited to 0–100. A factor scores 100 when its nearest feature is within the 'full' distance and 0 beyond the 'zero' distance, in a straight line in between.",
            [
                new BandInfoDto("Good", 75, 100, "Low heat stress: shade, water and indoor refuge are close."),
                new BandInfoDto("Fair", 55, 75, "Moderate heat: most relief is within reach, some is missing."),
                new BandInfoDto("Weak", 35, 55, "High heat: important relief is missing or far."),
                new BandInfoDto("Critical", 0, 35, "Very high heat: little or no relief nearby. A hot day here is hard to cope with.")
            ],
            Factors(model.Definitions(Layer.Heat)));

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
            Factors(model.Definitions(Layer.Safety)));

        var flood = new LayerMethodDto(
            "Flood",
            "Flood safety score",
            "Higher = safer. 0 is a place on a riverbank with no emergency help or way out nearby; 100 is far from any river with help and exits close.",
            "How exposed a place is to a river flood and how well it is set up to cope: how far it is from a mapped river, stream or canal, whether a hospital or police station is near, and whether a public transport stop offers a way out. On top of that, when IMGW reports a river above its warning or alarm level, places near water lose up to 35 points. It is not a flood-hazard map: there is no elevation or official flood-zone data in it yet.",
            "Flood safety score = Σ weight × factor score ÷ 100 over the three factors − open flood reports (up to 30 points) − live river adjustment (level × 35 × (1 − river score ÷ 100); level 0 normal, 0.5 above warning, 1 above alarm), limited to 0–100.",
            [
                new BandInfoDto("Good", 75, 100, "Well away from water, with help and a way out close."),
                new BandInfoDto("Fair", 55, 75, "Some exposure: close to water or help is a little far."),
                new BandInfoDto("Weak", 35, 55, "Exposed: near a river, or help and exits are far."),
                new BandInfoDto("Critical", 0, 35, "Very exposed: on or next to the water, with little help or no way out nearby.")
            ],
            Factors(model.Definitions(Layer.Flood)));

        var air = new LayerMethodDto(
            "Air",
            "Clean-air score",
            "Higher = cleaner. 0 is a place right on a main road with no trees or indoor refuge; 100 is far from traffic, with green areas and indoor places close.",
            "How well a place is protected from polluted air: how far it is from main roads, whether trees and parks are close, and whether there is a public indoor place to wait out bad air. On top of that, when the current GIOŚ PM2.5 readings are above 15 µg/m³ (the WHO daily guideline), every place loses points, up to 40 at 75 µg/m³ or more, and poorly protected places lose most. It does not measure the air at your door: it shows where people are more exposed.",
            "Clean-air score = Σ weight × factor score ÷ 100 over the three factors − open air reports (up to 30 points) − live air adjustment (level × 40 × (0.5 + 0.5 × (1 − base score ÷ 100)); level = (PM2.5 − 15) ÷ 60 between 0 and 1), limited to 0–100.",
            [
                new BandInfoDto("Good", 75, 100, "Away from traffic, with trees and indoor places close."),
                new BandInfoDto("Fair", 55, 75, "Mostly fine: near a busier road or short of green."),
                new BandInfoDto("Weak", 35, 55, "Exposed: close to main roads with little green."),
                new BandInfoDto("Critical", 0, 35, "Very exposed: next to heavy traffic with no green and no refuge nearby.")
            ],
            Factors(model.Definitions(Layer.Air)));

        var kpis = new List<KpiInfoDto>
        {
            new("nearRiver200", "Within 200 m of a river", "Share of built-up area whose nearest mapped river, stream or canal is closer than 200 m.", "Σ exposure of squares with river distance < 200 m ÷ Σ exposure of all squares × 100.", "OpenStreetMap waterways + exposure proxy"),
            new("noEmergency1000", "No hospital or police within 1 km", "Share of built-up area where the nearest hospital or police station is more than 1 km away, or none is mapped within 1.5 km.", "Σ exposure of squares with emergency distance > 1000 m ÷ Σ exposure of all squares × 100.", "OpenStreetMap hospitals and police stations"),
            new("riverLevel", "River situation", "How high Kraków's rivers are right now: 0 % normal, 50 % a gauge above its warning level, 100 % above alarm.", "Worst state across IMGW river gauges in Kraków.", "IMGW-PIB river gauges"),
            new("nearMainRoad100", "Within 100 m of a main road", "Share of built-up area whose nearest mapped motorway, trunk or primary road is closer than 100 m.", "Σ exposure of squares with main-road distance < 100 m ÷ Σ exposure of all squares × 100.", "OpenStreetMap main roads + exposure proxy"),
            new("noTrees500", "No park within 500 m", "Share of built-up area where the nearest mapped park or green area is more than 500 m away.", "Σ exposure of squares with park distance > 500 m ÷ Σ exposure of all squares × 100.", "OpenStreetMap parks"),
            new("airLevel", "Air pollution now", "How polluted the air is right now: 0 % at or below 15 µg/m³ PM2.5, 100 % at 75 µg/m³ or more.", "(average PM2.5 across Kraków GIOŚ stations − 15) ÷ 60, limited to 0–1.", "GIOŚ national air-quality network"),
            new("cells", "Squares scored", "Number of 250 m × 250 m squares that are built up (at least 3 mapped street lamps or a public transport stop) and so get a score.", "Count of grid squares with ≥ 3 mapped lamps or ≥ 1 stop.", "OpenStreetMap lamps + ZTP Kraków GTFS stops"),
            new("averageScore", "Average score", "The mean of the score of the planned event across all scored squares. Every score points the same way, higher = better: for Heat relief the heat-relief score, for Night safety the safety score, for Flood the flood-safety score, for Air the clean-air score.", "Mean of the event score over all squares (each square counts once).", "This model (SafetyModel)"),
            new("criticalCells", "Critical squares", "Squares in the worst band for the planned event: a score below 35.", "Count of squares whose band is Critical.", "This model (SafetyModel)"),
            new("weakCells", "Weak squares", "Squares in the second worst band: a score from 35 to 55.", "Count of squares whose band is Weak.", "This model (SafetyModel)"),
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
            [safety, heat, flood, air],
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
    public const string Combined = "Overall (API only) = 0.6 × the lower of the heat-relief score and the safety score + 0.4 × their average, so a place is only as good as its weak side. Higher = better.";

    public const string Priority = "Priority = (100 − score) × exposure × pressure, limited to 100. For heat relief, 'score' is the heat-relief score and pressure is 0.8 with no warning, 1.0 on a hot day (30 °C or more), 1.1 / 1.25 / 1.5 for heat warnings level 1 / 2 / 3. For night safety, pressure is 1.0.";

    public const string Exposure = "Exposure (0.2–1.0) = (street lamps + 3 × stops in the 3×3 squares around) ÷ 200. Busy, built-up squares count more than thin edges. There is no population grid, so this is a proxy.";
}
