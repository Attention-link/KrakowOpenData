namespace KrakowOpenData.Contracts;

// Transport shapes returned by KrakowOpenData.Api and KrakowOpenData.Client. No dependencies,
// so external apps can reference this package alone.
// Enums are exposed as strings so clients don't need shared enum types.

// ── Catalog ──────────────────────────────────────────────────────────────────
public sealed record DatasetDto(
    string Key,
    string Title,
    string Category,
    string Publisher,
    string SourceUrl,
    string Format,
    string Access,
    string? ApiRoute,
    string Notes);

public sealed record CategoryDto(string Key, string Description, IReadOnlyList<DatasetDto> Datasets);

// ── Mobility ─────────────────────────────────────────────────────────────────
public sealed record StopDto(
    string Id,
    string? Code,
    string Name,
    double Latitude,
    double Longitude,
    string Feed,
    bool? WheelchairAccessible,
    double? DistanceMeters = null);

public sealed record RouteDto(string Id, string ShortName, string? LongName, string Mode, string? Color, string Feed);

public sealed record VehicleDto(
    string Id,
    string? Label,
    string? TripId,
    string? RouteId,
    string? RouteShortName,
    string Mode,
    double Latitude,
    double Longitude,
    float? Bearing,
    float? SpeedKmh,
    DateTimeOffset? Timestamp,
    bool? WheelchairAccessible);

public sealed record DepartureDto(
    string TripId,
    string RouteId,
    string RouteShortName,
    string Mode,
    string? Headsign,
    DateTimeOffset ScheduledDeparture,
    DateTimeOffset ExpectedDeparture,
    int? DelaySeconds,
    bool IsRealtime,
    bool? WheelchairAccessible);

public sealed record TripUpdateDto(
    string Id,
    string TripId,
    string? RouteId,
    int? DelaySeconds,
    int StopUpdates,
    DateTimeOffset? Timestamp);

public sealed record AlertDto(
    string Id,
    string Header,
    string? Description,
    string Cause,
    string Effect,
    IReadOnlyList<string> RouteIds,
    IReadOnlyList<string> StopIds,
    DateTimeOffset? ActiveFrom,
    DateTimeOffset? ActiveTo,
    string? Url);

public sealed record ParkAndRideDto(
    string Id,
    string Name,
    string? Address,
    double? Latitude,
    double? Longitude,
    int? Capacity,
    int? EvChargers,
    int? BikeSpaces,
    string? OpeningHours,
    string? Notes,
    string Source);

// ── Environment ──────────────────────────────────────────────────────────────
public sealed record WeatherDto(
    string StationId,
    string StationName,
    DateTimeOffset ObservedAt,
    double? TemperatureC,
    double? WindSpeedMs,
    int? WindDirectionDegrees,
    double? RelativeHumidityPercent,
    double? PrecipitationMm,
    double? PressureHPa,
    bool IsHot,
    string Source);

public sealed record AirQualityDto(
    string StationId,
    string StationName,
    double? Latitude,
    double? Longitude,
    DateTimeOffset MeasuredAt,
    double? Pm10,
    double? Pm25,
    double? No2,
    string Band,
    string Source,
    string? OfficialIndex = null);

// ── Climate & crisis ─────────────────────────────────────────────────────────
public sealed record RiverGaugeDto(
    string Id,
    string StationName,
    string River,
    double? Latitude,
    double? Longitude,
    double? WaterLevelCm,
    DateTimeOffset? MeasuredAt,
    double? WarningLevelCm,
    double? AlarmLevelCm,
    string State,
    string Source);

public sealed record WarningDto(
    string Id,
    string EventName,
    int Level,
    int? ProbabilityPercent,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    string? Content,
    string Source);

// ── Urban space ──────────────────────────────────────────────────────────────
public sealed record AmenityDto(
    string Id,
    string Kind,
    string? Name,
    double Latitude,
    double Longitude,
    string? Details,
    double? DistanceMeters,
    string Source);

public sealed record StreetLightDto(
    string Id,
    double Latitude,
    double Longitude,
    string Technology,
    string? Mount,
    int? LightCount,
    double? HeightMeters,
    string? Operator,
    string? Reference,
    double? DistanceMeters,
    string Source);

/// <summary>
/// Street-light statistics for the whole city, or for a circle when a point was given
/// (then <see cref="AreaKm2"/> and <see cref="PerKm2"/> are filled in).
/// </summary>
public sealed record StreetLightSummaryDto(
    int Total,
    int WithKnownTechnology,
    double? AreaKm2,
    double? PerKm2,
    IReadOnlyDictionary<string, int> ByTechnology,
    IReadOnlyDictionary<string, int> ByMount,
    string Source);

public sealed record DistrictDto(string Id, int Number, string Name, int? RegisteredPopulation, string? PopulationNote, string Source);

// ── Public services ──────────────────────────────────────────────────────────
public sealed record ServiceCardDto(
    string Id,
    string Title,
    string Topic,
    string Summary,
    IReadOnlyList<string> Steps,
    string? Office,
    string? Address,
    string? OpeningHours,
    string? Phone,
    string? Fee,
    string SourceUrl,
    int Relevance);

public sealed record WaitingListDto(
    string Id,
    string Benefit,
    string Provider,
    string? Place,
    string? Address,
    string? Locality,
    string? Phone,
    double? Latitude,
    double? Longitude,
    bool Urgent,
    int? PeopleWaiting,
    int? AverageWaitDays,
    string? FirstAvailableDate,
    string? StatisticsUpdated,
    bool? StepFree,
    bool? Toilet,
    bool? CarPark,
    string Source);

// ── City Open Data portal (generic tables) ───────────────────────────────────
public sealed record OpenDataTableDto(string Key, string Title, string Category, string ApiUrl);

public sealed record OpenDataRowsDto(
    string Key,
    string Title,
    string Category,
    string ApiUrl,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows,
    bool Truncated,
    string Source);
