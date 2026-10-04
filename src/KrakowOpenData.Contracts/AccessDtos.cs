namespace KrakowOpenData.Contracts;

// ── Accessibility ("Kraków bez barier") ──────────────────────────────────────
// Concrete barriers and amenities for wheelchair users, people with prams and people with limited mobility.
// Every item carries its source, the date of the last edit in OpenStreetMap and a reliability level
// (confirmed | osm_recent | osm_old | user_unverified | unknown). Status is yes | limited | no | unknown;
// unknown ("brak danych") is never accessible. Profiles are preferences about barriers, not health data.

/// <summary>
/// One fact about an item. <see cref="Key"/> and <see cref="Value"/> are machine values for apps to translate
/// (e.g. kerb / raised); <see cref="Label"/> and <see cref="Text"/> are the Polish wording, for apps that do not.
/// </summary>
public sealed record AccessFactDto(string Key, string Label, string Value, string Text);

/// <summary>
/// A barrier or amenity. <see cref="Kind"/>: steps | kerb | elevator | entrance | place | toilets | bench | tactile | path.
/// <see cref="Barrier"/> is true when it counts against the chosen profile (status limited or no).
/// <see cref="LastEdited"/> is the last edit of the OSM element, not a verification date.
/// </summary>
public sealed record AccessItemDto(
    string Id,
    string Kind,
    double Latitude,
    double Longitude,
    string? Name,
    string Status,
    bool Barrier,
    IReadOnlyList<AccessFactDto> Facts,
    string Source,
    DateTimeOffset? LastEdited,
    string Reliability,
    string OsmUrl,
    string EditUrl,
    double? DistanceMeters,
    double? AlongRouteMeters = null);

/// <summary>How many items of one kind are within the radius, by status.</summary>
public sealed record AccessCountDto(string Kind, int Total, int Yes, int Limited, int No, int Unknown);

/// <summary>"<see cref="Known"/> of <see cref="Total"/> entrances have wheelchair data", and the like.</summary>
public sealed record AccessCoverageDto(string Key, int Known, int Total, string Label);

/// <summary>A resident report about a barrier: always shown separately from mapped data, as user_unverified.</summary>
public sealed record AccessUserReportDto(
    string Id,
    string Type,
    double Latitude,
    double Longitude,
    double DistanceMeters,
    DateTimeOffset CreatedAt,
    int Supporters,
    bool VerifiedByPlanner,
    string Reliability);

/// <summary>Where accessibility data has been downloaded (a box; outside it everything is unknown).</summary>
public sealed record AccessAreaDto(double MinLatitude, double MinLongitude, double MaxLatitude, double MaxLongitude);

/// <summary>Barriers and amenities around a point for a profile.</summary>
public sealed record AccessNearbyDto(
    string Profile,
    double Latitude,
    double Longitude,
    double RadiusMeters,
    IReadOnlyList<AccessItemDto> Items,
    int TotalMatched,
    IReadOnlyList<AccessCountDto> Counts,
    IReadOnlyList<AccessCoverageDto> Coverage,
    string? DataNoteCode,
    string? DataNote,
    bool InDataArea,
    AccessAreaDto DataArea,
    DateTimeOffset? DataLoadedAt,
    IReadOnlyList<AccessUserReportDto> UserReports,
    string Licence,
    bool SampleData);

/// <summary>
/// One line of a route's barrier summary. <see cref="Code"/>: steps_no_ramp | steps_ramp | kerb_raised | kerb_unknown |
/// rough | unpaved | steep | narrow | unknown_share | elevator | toilets | bench. <see cref="Text"/> is Polish.
/// </summary>
public sealed record AccessSummaryLineDto(string Code, int Count, double Meters, double Share, string Severity, string Text);

/// <summary>
/// What a walking route means for a profile: barriers within <see cref="MatchMeters"/> of the line (sampled every
/// <see cref="StepMeters"/>), metres on rough surface or steep slope, and the share of the route with no data
/// (which is never counted as accessible).
/// </summary>
public sealed record AccessRouteDto(
    string Profile,
    double LengthMeters,
    double MatchMeters,
    double StepMeters,
    int StepsNoRamp,
    int StepsWithRamp,
    int RaisedKerbs,
    int LoweredKerbs,
    int UnknownKerbs,
    double RoughMeters,
    double UnpavedMeters,
    double SteepMeters,
    double UnknownShare,
    int Elevators,
    int AccessibleToilets,
    int Benches,
    IReadOnlyList<AccessSummaryLineDto> Summary,
    IReadOnlyList<AccessItemDto> Barriers,
    bool InDataArea,
    string Note,
    string Licence,
    bool SampleData);

/// <summary>Body of POST /api/safety/access/route: a path as [lat, lon] pairs (e.g. a route from /api/safety/route).</summary>
public sealed record AccessRouteRequest(IReadOnlyList<double[]> Path, string? Profile);
