using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Domain.Environment;

/// <summary>Air quality at a monitoring station. Concentrations in µg/m³.</summary>
public sealed record AirQualityMeasurement(
    string Id,
    string StationName,
    GeoPoint? Location,
    DateTimeOffset MeasuredAt,
    double? Pm10,
    double? Pm25,
    double? No2,
    string Source,
    string? OfficialIndex = null) : IEntity
{
    /// <summary>
    /// Simple band based on PM2.5 against the WHO 2021 24-hour guideline (15 µg/m³).
    /// <see cref="OfficialIndex"/> holds the official Polish index name from GIOŚ when available
    /// (e.g. "Dobry"), which also covers stations without a PM2.5 sensor.
    /// </summary>
    public AirQualityBand Band => Pm25 switch
    {
        null => AirQualityBand.Unknown,
        <= 15 => AirQualityBand.Good,
        <= 35 => AirQualityBand.Moderate,
        <= 55 => AirQualityBand.Poor,
        _ => AirQualityBand.VeryPoor
    };
}

public enum AirQualityBand
{
    Unknown,
    Good,
    Moderate,
    Poor,
    VeryPoor
}
