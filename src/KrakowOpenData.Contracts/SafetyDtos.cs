namespace KrakowOpenData.Contracts;

// ── Safety concerns (heat + night safety scores, citizen reports, planner tools) ───────────────
// Enums are exposed as strings. Scores are 0–100. Every score points the same way: HIGHER = BETTER (heat relief: more relief; safety: safer; flood: safer; air: cleaner).
// Every score points the same way: higher = better (for heat, higher = better able to cool down).
// How every number is computed is documented on KrakowOpenData.Application.Safety.SafetyModel.

/// <summary>Where the score grid sits. A cell (row, col) covers latitude OriginLatitude + row × CellLatitudeDegrees (and likewise for longitude).</summary>
public sealed record GridMetaDto(
    double OriginLatitude,
    double OriginLongitude,
    double CellLatitudeDegrees,
    double CellLongitudeDegrees,
    double CellSizeMeters,
    double GoodFrom,
    double FairFrom,
    double WeakFrom,
    double SearchRadiusMeters);

/// <summary>
/// The whole score grid in a compact form (one row per inhabited cell) so a phone can download and cache it.
/// Columns: row, col, heat, safety, combined, exposure (0–100), openReports, priority (0–100, for <see cref="Event"/>).
/// </summary>
public sealed record GridDto(
    GridMetaDto Grid,
    string Event,
    IReadOnlyList<string> Columns,
    IReadOnlyList<int[]> Cells,
    DateTimeOffset GeneratedAt,
    ConditionsDto Conditions);

public sealed record FactorDto(
    string Key,
    string Label,
    string Layer,
    double Weight,
    double? Value,
    string Unit,
    double Score,
    double Points,
    string? NearestName,
    double Contribution);

public sealed record LayerScoreDto(
    double Score,
    string Band,
    double BaseScore,
    double ReportPenalty,
    IReadOnlyList<FactorDto> Factors);

public sealed record NearestFeatureDto(
    string Key,
    string Kind,
    string? Name,
    double Latitude,
    double Longitude,
    double DistanceMeters,
    int WalkingMinutes,
    string? OpeningHours,
    string? Wheelchair = null);

public sealed record SuggestedActionDto(
    string Code,
    string Layer,
    string FactorKey,
    string Severity,
    string Text);

/// <summary>Everything about one place: the two layer scores with their factors, what is nearby, reports and suggested actions.</summary>
public sealed record PlaceScoreDto(
    string CellId,
    double Latitude,
    double Longitude,
    LayerScoreDto Heat,
    LayerScoreDto Safety,
    double Combined,
    string CombinedBand,
    double Exposure,
    double Priority,
    string Event,
    IReadOnlyList<NearestFeatureDto> Nearest,
    IReadOnlyList<ReportDto> Reports,
    IReadOnlyList<SuggestedActionDto> Actions,
    DateTimeOffset GeneratedAt,
    string? Label = null,
    LayerScoreDto? Flood = null,
    LayerScoreDto? Air = null);

public sealed record CorridorSampleDto(double Latitude, double Longitude, double Heat, double Safety, double Combined, double Flood = 0, double Air = 0);

/// <summary>Scores sampled every ~50 m along a straight line between two points (not a street route).</summary>
public sealed record CorridorDto(
    double LengthMeters,
    int WalkingMinutes,
    IReadOnlyList<CorridorSampleDto> Samples,
    double MinSafety,
    double AverageSafety,
    double MinHeat,
    double AverageHeat,
    int WeakestSampleIndex,
    int OpenReportsNearby,
    string Note);

// ── Live conditions ──────────────────────────────────────────────────────────
public sealed record HeatConditionDto(string Pressure, int Level, string Label, double? TemperatureC, string? WarningTitle);

public sealed record AirConditionDto(string Band, double? Pm25, string? Station, double? Pm25Average = null);

public sealed record HydroConditionDto(int ElevatedGauges, string WorstState);

public sealed record ConditionsDto(
    DateTimeOffset GeneratedAt,
    bool IsDark,
    string? SunriseLocal,
    string? SunsetLocal,
    HeatConditionDto Heat,
    AirConditionDto Air,
    HydroConditionDto Hydro,
    IReadOnlyList<WarningDto> Warnings,
    string SuggestedMode,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> DataGaps);

// ── Citizen reports ──────────────────────────────────────────────────────────
public sealed record ReportTypeDto(string Type, string Layer, double Weight, double HalfLifeHours, string Label);

public sealed record ReportDto(
    string Id,
    string Type,
    string Layer,
    double Latitude,
    double Longitude,
    string CellId,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt,
    int Supporters,
    bool VerifiedByPlanner,
    string Status,
    string? Note,
    string? ResolutionNote,
    ReportTriageDto? Triage = null);

/// <summary>AI suggestion about a report (planners only). Advice, not a decision: it never changes the score or the report.</summary>
public sealed record ReportTriageDto(
    string? SuggestedType,
    int Severity,
    string? SummaryPl,
    bool IsAbuse,
    bool ContainsPersonalData,
    string? DuplicateOf,
    double Confidence,
    DateTimeOffset TriagedAt);

public sealed record CreateReportRequest(string Type, double Latitude, double Longitude, string? Note, string DeviceId);

public sealed record ConfirmReportRequest(string DeviceId);

public sealed record ResolveReportRequest(string? Note);

// ── Planner alerts ───────────────────────────────────────────────────────────
public sealed record PlannerAlertDto(
    string Id,
    string? Layer,
    string Severity,
    string Title,
    string Message,
    IReadOnlyDictionary<string, string> Translations,
    double Latitude,
    double Longitude,
    double RadiusMeters,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string? CellId,
    string Status,
    int? DevicesInArea);

public sealed record CreateAlertRequest(
    string? Layer,
    string Severity,
    string Title,
    string Message,
    IReadOnlyDictionary<string, string>? Translations,
    double Latitude,
    double Longitude,
    double RadiusMeters,
    int DurationMinutes,
    string? CellId);

// ── Planner: agencies and dashboard ──────────────────────────────────────────
public sealed record AgencyDto(string Id, string Name, string Responsibility, string? Phone, string? Url, bool ContactVerified);

public sealed record DispatchRequest(string AgencyId, string Subject, string Body, double? Latitude, double? Longitude, string? CellId);

public sealed record DispatchDto(
    string Id,
    string AgencyId,
    string AgencyName,
    string Subject,
    string Body,
    double? Latitude,
    double? Longitude,
    string? CellId,
    string Delivery,
    string Reference,
    DateTimeOffset CreatedAt);

public sealed record KpiDto(string Key, double Value, string Unit);

public sealed record FactorGapDto(string Key, string Label, string Layer, double WeakShare, double AverageScore);

public sealed record HistogramBinDto(int From, int To, int Cells);

public sealed record PriorityCellDto(
    string CellId,
    double Latitude,
    double Longitude,
    double Priority,
    double Heat,
    double Safety,
    double Combined,
    double Exposure,
    int OpenReports,
    IReadOnlyList<string> WeakFactors,
    IReadOnlyList<SuggestedActionDto> Actions,
    string? Label = null,
    double Flood = 0,
    double Air = 0);

public sealed record ReportCountDto(string Type, int Open, int Last24Hours, int Verified);

public sealed record PlannerSummaryDto(
    string Event,
    DateTimeOffset GeneratedAt,
    ConditionsDto Conditions,
    int Cells,
    IReadOnlyList<KpiDto> Kpis,
    IReadOnlyList<HistogramBinDto> Histogram,
    IReadOnlyList<FactorGapDto> FactorGaps,
    IReadOnlyList<PriorityCellDto> TopPriority,
    IReadOnlyList<ReportCountDto> Reports,
    int ActiveAlerts,
    int DevicesActive,
    IReadOnlyList<string> Notes);

// ── Offline support and planner reach ────────────────────────────────────────
/// <summary>A mapped feature that feeds a score factor (water point, park, pharmacy, …), so apps can find "nearest relief" offline.</summary>
/// <summary>A mapped feature. <see cref="Wheelchair"/>: yes | limited | no from OSM or the GTFS stop flag; null = no data (never "accessible").</summary>
public sealed record FeatureDto(string Key, string Kind, string? Name, double Latitude, double Longitude, double RadiusMeters, string? OpeningHours, string? Wheelchair = null);

public sealed record ReachDto(double Latitude, double Longitude, double RadiusMeters, int DevicesInArea, int DevicesActive, int WindowMinutes);

// ── Address search (geocoding) ───────────────────────────────────────────────
/// <summary>An address or place found by name, or the address nearest to a point.</summary>
public sealed record GeocodeResultDto(
    string Label,
    string? Name,
    string? Street,
    string? HouseNumber,
    string? District,
    string? Postcode,
    double Latitude,
    double Longitude,
    string Kind);

// ── Walking routes along streets ─────────────────────────────────────────────
/// <summary>
/// One walking route. <see cref="Path"/> follows the streets as [lat, lon] pairs. <see cref="Samples"/> are the scores every ~50 m along it.
/// <see cref="Average"/> and <see cref="Worst"/> are in the scale of the requested mode: night = safety score (worst = lowest),
/// every mode uses its own score, and for every score the worst value is the lowest.
/// </summary>
public sealed record RouteOptionDto(
    string Kind,
    double LengthMeters,
    int WalkingMinutes,
    IReadOnlyList<double[]> Path,
    IReadOnlyList<CorridorSampleDto> Samples,
    double Average,
    double Worst,
    int WeakestSampleIndex,
    int OpenReportsNearby);

/// <summary>
/// The fastest route and, when it is meaningfully better for the chosen mode, a safer / cooler one to compare it with.
/// <see cref="Source"/> is "street" (real street routing) or "straight-line" (routing unavailable: only a corridor check).
/// </summary>
public sealed record RoutesDto(
    string Mode,
    string Source,
    RouteOptionDto Fastest,
    RouteOptionDto? Better,
    string BetterKind,
    double ScoreGain,
    double ExtraMeters,
    int ExtraMinutes,
    string Note,
    bool Widened = false,
    bool FastestIsAcceptable = true,
    double? ThresholdAverage = null,
    double? ThresholdWorst = null);

// ── How the scores are built (documentation served by the API, used for tooltips, legends and the planner) ──
public sealed record BandInfoDto(string Band, double From, double To, string Meaning);

public sealed record FactorInfoDto(
    string Key,
    string Label,
    string Layer,
    double Weight,
    string Measures,
    string FullScoreAt,
    string ZeroScoreAt,
    string WhyWeighted,
    string Source,
    int MappedCount,
    string? DataCaveat,
    double? DefaultWeight = null);

// ── Factor weights (planner) ─────────────────────────────────────────────────
/// <param name="Weight">The weight in use (points out of 100 in its layer).</param>
/// <param name="DefaultWeight">The weight the model ships with.</param>
/// <param name="Why">Why this factor carries weight at all, and why the default is what it is.</param>
public sealed record FactorWeightDto(string Key, string Label, double Weight, double DefaultWeight, string Measures, string Why, string Source, string? Caveat);

public sealed record LayerWeightsDto(string Layer, string Title, bool Customized, IReadOnlyList<FactorWeightDto> Factors);

public sealed record WeightsDto(IReadOnlyList<LayerWeightsDto> Layers, bool Customized);

/// <summary>Weights by factor key. Any non-negative numbers: each layer is scaled so its weights add up to 100.</summary>
public sealed record SetWeightsRequest(IReadOnlyDictionary<string, double> Weights);

/// <summary>
/// When is the fastest walking route "good enough" for one safety measure? Below <see cref="Average"/> on average, or with a stretch below
/// <see cref="Worst"/>, a safer alternative is searched for (and farther away). <see cref="Why"/> explains the default.
/// </summary>
public sealed record LayerRouteThresholdDto(string Layer, string Title, double Average, double Worst, double DefaultAverage, double DefaultWorst, bool Customized, string Why);

/// <param name="Min">Lowest value a planner may set.</param>
/// <param name="Max">Highest value a planner may set.</param>
public sealed record RouteThresholdsDto(IReadOnlyList<LayerRouteThresholdDto> Layers, bool Customized, double Min, double Max);

public sealed record RouteThresholdValue(double Average, double Worst);

/// <summary>Layer name (Safety, Heat, Flood, Air) to its two limits. Layers not listed keep what they have.</summary>
public sealed record SetRouteThresholdsRequest(IReadOnlyDictionary<string, RouteThresholdValue> Thresholds);

public sealed record LayerMethodDto(
    string Layer,
    string Title,
    string Direction,
    string Meaning,
    string Formula,
    IReadOnlyList<BandInfoDto> Bands,
    IReadOnlyList<FactorInfoDto> Factors);

public sealed record KpiInfoDto(string Key, string Title, string Definition, string Formula, string Source);

public sealed record MethodDto(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<LayerMethodDto> Layers,
    string CombinedFormula,
    string ReportRule,
    string PriorityFormula,
    string ExposureFormula,
    double? TemperatureC,
    string HeatPressure,
    IReadOnlyList<KpiInfoDto> Kpis,
    IReadOnlyList<string> Limits);
