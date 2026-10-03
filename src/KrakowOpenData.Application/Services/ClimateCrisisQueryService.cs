using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Mapping;
using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.ClimateAndCrisis;

namespace KrakowOpenData.Application.Services;

/// <summary>Use cases for river levels and warnings (Climate &amp; crisis category).</summary>
public sealed class ClimateCrisisQueryService(
    IReadRepository<HydroObservation> gauges,
    IReadRepository<WeatherWarning> warnings,
    IClock clock)
{
    /// <summary>Kraków county TERYT code.</summary>
    public const string KrakowTeryt = "1261";

    public async Task<IReadOnlyList<RiverGaugeDto>> GetRiverGaugesAsync(bool onlyElevated, CancellationToken ct = default)
    {
        var spec = onlyElevated
            ? new Specification<HydroObservation>(h => h.State is HydroState.AboveWarning or HydroState.AboveAlarm)
            : null;

        return (await gauges.ListAsync(spec, ct))
            .OrderByDescending(h => h.State)
            .ThenBy(h => h.River, StringComparer.CurrentCulture)
            .ThenBy(h => h.StationName, StringComparer.CurrentCulture)
            .Select(h => h.ToDto())
            .ToList();
    }

    public async Task<IReadOnlyList<WarningDto>> GetActiveWarningsAsync(string? teryt, CancellationToken ct = default)
    {
        var area = string.IsNullOrWhiteSpace(teryt) ? KrakowTeryt : teryt.Trim();
        var now = clock.UtcNow;

        return (await warnings.ListAsync(
                new Specification<WeatherWarning>(w => w.IsActiveAt(now) && w.AppliesTo(area)), ct))
            .OrderByDescending(w => w.Level)
            .ThenBy(w => w.ValidFrom)
            .Select(w => w.ToDto())
            .ToList();
    }
}
