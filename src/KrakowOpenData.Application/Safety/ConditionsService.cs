using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Services;
using KrakowOpenData.Contracts;

namespace KrakowOpenData.Application.Safety;

/// <summary>
/// What is happening in Kraków right now, in one object: heat pressure (IMGW weather + warnings), air quality (GIOŚ),
/// river alerts (IMGW) and whether it is dark. Each live source is optional: if one is down, the rest is returned
/// and the gap is listed in <see cref="ConditionsDto.DataGaps"/>.
///
/// <para><b>Heat pressure</b> is the highest of: an active IMGW warning whose event name contains "upał" (heat)
/// at its level 1–3; otherwise "hot day" when the latest Kraków temperature is 30 °C or more; otherwise none.
/// It scales the planner priority (see <see cref="SafetyModel.HeatPressureFactor"/>).</para>
/// </summary>
public sealed class ConditionsService(
    ClimateCrisisQueryService crisis,
    EnvironmentQueryService environment,
    IClock clock)
{
    private const double KrakowLatitude = 50.0617, KrakowLongitude = 19.9373;

    public async Task<ConditionsDto> GetAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var gaps = new List<string>();
        var notes = new List<string>();

        var warnings = await Try(() => crisis.GetActiveWarningsAsync(null, ct), "warnings", gaps) ?? [];
        var weather = await Try(() => environment.GetWeatherAsync(ct), "weather", gaps) ?? [];
        var air = await Try(() => environment.GetAirQualityAsync(ct), "air", gaps) ?? [];
        var gauges = await Try(() => crisis.GetRiverGaugesAsync(onlyElevated: true, ct), "rivers", gaps) ?? [];

        var temperature = weather.Select(w => w.TemperatureC).FirstOrDefault(t => t is not null);
        var heatWarning = warnings
            .Where(w => w.EventName.Contains("upał", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(w => w.Level)
            .FirstOrDefault();

        var pressure = heatWarning is not null
            ? (HeatPressure)Math.Clamp(heatWarning.Level + 1, 2, 4)
            : temperature >= 30 ? HeatPressure.HotDay : HeatPressure.None;

        var heat = new HeatConditionDto(
            pressure.ToString(),
            (int)pressure,
            pressure switch
            {
                HeatPressure.None => "No heat pressure",
                HeatPressure.HotDay => "Hot day (30 °C or more)",
                HeatPressure.WarningLevel1 => "Heat warning, level 1",
                HeatPressure.WarningLevel2 => "Heat warning, level 2",
                _ => "Heat warning, level 3"
            },
            temperature,
            heatWarning?.EventName);

        var worstAir = air.Where(a => a.Pm25 is not null).OrderByDescending(a => a.Pm25).FirstOrDefault();
        var pm25Values = air.Where(a => a.Pm25 is not null).Select(a => a.Pm25!.Value).ToList();
        var airCondition = new AirConditionDto(worstAir?.Band ?? "Unknown", worstAir?.Pm25, worstAir?.StationName, pm25Values.Count == 0 ? null : Math.Round(pm25Values.Average(), 1));
        var hydro = new HydroConditionDto(
            gauges.Count,
            gauges.Any(g => g.State.Contains("Alarm", StringComparison.OrdinalIgnoreCase)) ? "AboveAlarm" : gauges.Count > 0 ? "AboveWarning" : "Normal");

        var isDark = SolarTime.IsDark(now, KrakowLatitude, KrakowLongitude);
        var (sunrise, sunset) = SolarTime.SunTimes(DateOnly.FromDateTime(now.UtcDateTime), KrakowLatitude, KrakowLongitude);

        var suggested = (pressure >= HeatPressure.HotDay, isDark) switch
        {
            (true, true) => "both",
            (true, false) => "heat",
            (false, true) => "safety",
            _ => "both"
        };

        // A river above its warning level or heavy smog is more urgent than the day/night default.
        if (hydro.WorstState != "Normal") suggested = "flood";
        else if (SafetyModel.AirLevel(airCondition.Pm25Average ?? airCondition.Pm25) >= 0.5) suggested = "air";

        if (gaps.Count > 0) notes.Add("Some live sources are unavailable; the rest is shown.");

        return new ConditionsDto(
            now,
            isDark,
            sunrise is null ? null : KrakowTime.ToLocal(sunrise.Value).ToString("HH:mm"),
            sunset is null ? null : KrakowTime.ToLocal(sunset.Value).ToString("HH:mm"),
            heat,
            airCondition,
            hydro,
            warnings,
            suggested,
            notes,
            gaps);
    }

    /// <summary>The heat pressure level only, for scoring.</summary>
    public async Task<HeatPressure> GetHeatPressureAsync(CancellationToken ct = default) =>
        Enum.TryParse<HeatPressure>((await GetAsync(ct)).Heat.Pressure, out var p) ? p : HeatPressure.None;

    private static async Task<T?> Try<T>(Func<Task<T>> load, string name, List<string> gaps) where T : class
    {
        try
        {
            return await load();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            gaps.Add(name);
            return null;
        }
    }
}
