namespace KrakowOpenData.Application.Safety;

/// <summary>
/// The written reasons behind the accessibility factors and their weights, per profile (shown in the method panel and the planner's weights editor).
/// The numbers (weights, distances) are read from <see cref="AccessFactorSets"/>; the reasons say why each profile weighs a factor the way it does.
/// </summary>
internal static class AccessMethodText
{
    private const string Osm = "OpenStreetMap accessibility tags (Overpass), downloaded for the accessibility area.";

    private static readonly Dictionary<string, (string Measures, string Source, string? Caveat)> Common = new()
    {
        ["steps"] = (
            "Weighted number of flights of steps within 150 m without a ramp that suits the profile (a step counts 1, a step whose ramp is only partly suitable 0.5), plus entrances with steps and no ramp.",
            Osm + " Steps (highway=steps) with ramp and step_count tags, and entrances with step_count.",
            "A step with no ramp tag counts as having none; a ramp that is not mapped is not seen. Where nothing is mapped, no steps are counted, which is why a square needs some mapped items nearby before it gets a score at all."),
        ["kerbs"] = (
            "Weighted number of raised kerbs within 150 m (a kerb the profile cannot pass counts 1, one it only finds difficult 0.5). Lowered and flush kerbs count 0.",
            Osm + " Kerb (kerb=raised, lowered, flush) on crossings and kerb nodes.",
            "kerb=yes (height unknown) and crossings with no kerb tag are not counted either way."),
        ["surface"] = (
            "Share of mapped footway, path and pedestrian stretches within 150 m that are smooth, within the profile's slope limit and wide enough. Rough (sett, cobbles), unpaved, steep or narrow stretches do not count as fine.",
            Osm + " surface, smoothness, incline and width of footways, paths and pedestrian streets.",
            "Stretches with no surface, slope or width tag are not mapped as paths at all, so a place with no mapped footway scores 0 on this factor: no data is never read as a smooth path."),
        ["slope"] = (
            "Number of mapped footway stretches within 150 m steeper than the profile's limit (6 % wheelchair, 8 % pram and limited mobility).",
            Osm + " incline on footways and paths.",
            "Only slopes that are mapped with a number are seen; incline=up/down without a figure is ignored."),
        ["stepFree"] = (
            "Walking distance to the nearest entrance, lift or place that is mapped as wheelchair accessible (wheelchair=yes or designated, or an entrance with a suitable ramp).",
            Osm + " wheelchair tags on places, entrances and lifts.",
            "Only items that say 'yes' count; 'limited', 'no' and untagged items do not."),
        ["accessStops"] = (
            "Walking distance to the nearest public transport stop that the ZTP timetable flags as accessible (wheelchair_boarding = 1).",
            "ZTP Kraków GTFS stops.txt (wheelchair_boarding).",
            "A stop with no flag is not counted. The flag says the stop is accessible, not that the vehicle is low-floor on every trip. If no stop in the ZTP open data is flagged accessible (the feeds currently flag none), this factor is not available: it is shown as 'no data', left out of the score and the other weights are rescaled to 100, so no square is punished for missing data."),
        ["accessToilets"] = (
            "Walking distance to the nearest public toilet that is mapped as wheelchair accessible (toilets:wheelchair or wheelchair = yes); for the pram profile, one with a changing table also counts.",
            Osm + " amenity=toilets with wheelchair and changing_table tags.",
            "Toilets with no wheelchair tag are not counted."),
        ["rest"] = (
            "Walking distance to the nearest mapped bench.",
            Osm + " amenity=bench.",
            "Only mapped benches count; many benches are missing from the map."),
        ["tactile"] = (
            "Walking distance to the nearest crossing or stop with tactile paving.",
            Osm + " tactile_paving=yes.",
            "Tactile paving serves people who are blind or have low vision, whom none of the three profiles represents, so it is shown but carries little or no weight.")
    };

    private static readonly Dictionary<(string Profile, string Base), string> Why = new()
    {
        [("wheelchair", "steps")] = "A flight of steps with no ramp stops a wheelchair completely, so steps carry the largest weight for this profile.",
        [("pram", "steps")] = "A pram can be lifted over one step with help but not comfortably, and ramps tagged for strollers count as fine, so steps weigh less than for a wheelchair.",
        [("mobility", "steps")] = "People who walk with difficulty can usually manage a few steps with a handrail, so steps are a difficulty here (counted half) and carry a low weight.",
        [("wheelchair", "kerbs")] = "A raised kerb at a crossing blocks a wheelchair or needs a long detour, so kerbs are the second largest weight.",
        [("pram", "kerbs")] = "A raised kerb is a nuisance with a pram but it can be tilted over, so kerbs carry a medium weight.",
        [("mobility", "kerbs")] = "A raised kerb is a difficulty, not a barrier, for most people who walk with difficulty, so kerbs carry a small weight.",
        [("wheelchair", "surface")] = "Cobbles, gravel and narrow paths slow a wheelchair and can stop it, but a little rough stretch can be crossed, so surface has a medium weight.",
        [("pram", "surface")] = "Surface is felt on every metre of a walk with a pram (cobbles shake the child, gravel is heavy to push), so it carries the largest weight for this profile.",
        [("mobility", "surface")] = "An even surface lowers the risk of tripping and is easier on tired legs, so surface carries a medium weight.",
        [("wheelchair", "slope")] = "Slopes above about 6 % are hard or impossible to climb in a manual wheelchair and unsafe to descend, so slope has a medium-low weight (a steep stretch usually also shows up in steps and surface).",
        [("pram", "slope")] = "Steeper slopes are heavy with a loaded pram but only up to a point, so slope has a medium weight.",
        [("mobility", "slope")] = "Climbing is the thing that tires people who walk with difficulty most, so slope carries the second largest weight for this profile.",
        [("wheelchair", "stepFree")] = "A step-free entrance or lift is where a wheelchair user can actually get in, but it only helps if the way to it is open, so it has a medium weight.",
        [("pram", "stepFree")] = "Step-free entrances matter for shops and offices with a pram but a pram can be carried in; medium-low weight.",
        [("mobility", "stepFree")] = "Step-free entrances and lifts help people who cannot manage stairs, but most can manage a few steps, so a medium-low weight.",
        [("wheelchair", "accessStops")] = "Public transport is how a wheelchair user covers longer distances; a stop with flagged boarding access extends reach beyond the walkable area.",
        [("pram", "accessStops")] = "Low-floor boarding is what lets a pram on a bus or tram without lifting it, so accessible stops matter more than for a wheelchair walk.",
        [("mobility", "accessStops")] = "A stop close by shortens the distance people with limited stamina have to walk, so the weight is as for a pram.",
        [("wheelchair", "accessToilets")] = "An accessible toilet is a precondition for a longer outing for many wheelchair users; medium weight.",
        [("pram", "accessToilets")] = "A toilet with a changing table matters on an outing with a baby, but less than the route itself.",
        [("mobility", "accessToilets")] = "Toilets within reach matter for a longer walk, but the effect is smaller than slopes and places to rest.",
        [("wheelchair", "rest")] = "A wheelchair user rests in the chair, so benches matter little for this profile; they stay as a small weight because they also serve companions.",
        [("pram", "rest")] = "Parents stop to feed or soothe a child, so benches carry a medium weight.",
        [("mobility", "rest")] = "The distance a person with limited stamina can walk between rests is short, so benches carry the largest weight for this profile; the distance bands are tighter (full within 80 m, zero from 300 m).",
        [("wheelchair", "tactile")] = "Tactile paving is for people who are blind or have low vision, not for wheelchair users: shown for completeness, weight 0.",
        [("pram", "tactile")] = "Tactile paving is for people who are blind or have low vision, not for pram users: shown for completeness, weight 0.",
        [("mobility", "tactile")] = "Tactile paving also helps people who walk unsteadily find crossings and stops, so it has a small weight."
    };

    public static (string Measures, string Why, string Source, string? Caveat) Get(string profile, string @base)
    {
        var (measures, source, caveat) = Common[@base];
        return (measures, Why[(profile, @base)], source, caveat);
    }
}
