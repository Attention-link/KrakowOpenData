using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Domain.Environment;

/// <summary>A synoptic weather station reading (IMGW). Id is the station id.</summary>
public sealed record WeatherObservation(
    string Id,
    string StationName,
    DateTimeOffset ObservedAt,
    double? TemperatureC,
    double? WindSpeedMs,
    int? WindDirectionDegrees,
    double? RelativeHumidityPercent,
    double? PrecipitationMm,
    double? PressureHPa,
    string Source) : IEntity
{
    /// <summary>True when this reading is 30 °C or more (the usual "hot day" threshold).</summary>
    public bool IsHot => TemperatureC >= 30;
}
