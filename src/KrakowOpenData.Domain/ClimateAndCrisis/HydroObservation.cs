using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Domain.ClimateAndCrisis;

/// <summary>A river or stream gauge reading.</summary>
public sealed record HydroObservation(
    string Id,
    string StationName,
    string River,
    GeoPoint? Location,
    double? WaterLevelCm,
    DateTimeOffset? MeasuredAt,
    double? WarningLevelCm,
    double? AlarmLevelCm,
    HydroState? ReportedState,
    string Source) : IEntity
{
    /// <summary>
    /// Computed from thresholds when known; otherwise uses the state reported by the source
    /// (e.g. the city's own "above warning level" bulletins).
    /// </summary>
    public HydroState State
    {
        get
        {
            if (WaterLevelCm is { } level)
            {
                if (AlarmLevelCm is { } alarm && level >= alarm) return HydroState.AboveAlarm;
                if (WarningLevelCm is { } warning && level >= warning) return HydroState.AboveWarning;
                if (WarningLevelCm.HasValue || AlarmLevelCm.HasValue) return HydroState.Normal;
            }

            return ReportedState ?? HydroState.Unknown;
        }
    }
}

public enum HydroState
{
    Unknown,
    Normal,
    AboveWarning,
    AboveAlarm
}
