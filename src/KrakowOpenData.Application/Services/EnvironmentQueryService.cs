using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Contracts;
using KrakowOpenData.Domain.Environment;

namespace KrakowOpenData.Application.Services;

/// <summary>Use cases for weather and air quality (Environment category).</summary>
public sealed class EnvironmentQueryService(
    IReadRepository<WeatherObservation> weather,
    IReadRepository<AirQualityMeasurement> airQuality)
{
    public async Task<IReadOnlyList<WeatherDto>> GetWeatherAsync(CancellationToken ct = default) =>
        (await weather.ListAsync(cancellationToken: ct))
            .OrderByDescending(w => w.ObservedAt)
            .Select(w => w.ToDto())
            .ToList();

    public async Task<IReadOnlyList<AirQualityDto>> GetAirQualityAsync(CancellationToken ct = default) =>
        (await airQuality.ListAsync(cancellationToken: ct))
            .OrderByDescending(a => a.Pm25 ?? -1)
            .Select(a => a.ToDto())
            .ToList();
}
