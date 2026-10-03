using KrakowOpenData.Domain.Environment;
using KrakowOpenData.Infrastructure.Gtfs;
using KrakowOpenData.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Infrastructure.Gios;

/// <summary>
/// Live air quality for Kraków from GIOŚ (no key). Per station: latest PM2.5, PM10 and NO₂ plus the
/// official index. The station and sensor list changes rarely, so it is kept for a day.
/// Results are cached by <see cref="DataSources.CachedDataSource{T}"/>, so GIOŚ is called every 20 minutes at most.
/// </summary>
public sealed class GiosClient(IHttpClientFactory httpClientFactory, IOptions<KrakowDataOptions> options, ILogger<GiosClient> logger)
{
    private static readonly TimeSpan StationListLifetime = TimeSpan.FromHours(24);

    private readonly KrakowDataOptions _options = options.Value;
    private readonly SemaphoreSlim _stationLock = new(1, 1);
    private IReadOnlyList<(GiosStation Station, IReadOnlyList<GiosSensor> Sensors)>? _stations;
    private DateTimeOffset _stationsLoadedAt;

    public async Task<IReadOnlyList<AirQualityMeasurement>> GetAirQualityAsync(CancellationToken ct)
    {
        var stations = await GetStationsAsync(ct);
        var results = new List<AirQualityMeasurement>();
        Exception? lastError = null;

        foreach (var (station, sensors) in stations)
        {
            try
            {
                results.Add(await ReadStationAsync(station, sensors, ct));
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
            {
                lastError = ex;
                logger.LogWarning(ex, "GIOŚ station {Station} could not be read", station.Name);
            }
        }

        if (results.Count == 0 && lastError is not null) throw lastError;
        return results;
    }

    private async Task<AirQualityMeasurement> ReadStationAsync(GiosStation station, IReadOnlyList<GiosSensor> sensors, CancellationToken ct)
    {
        async Task<(DateTimeOffset MeasuredAt, double Value)?> Latest(string code)
        {
            var sensor = sensors.FirstOrDefault(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase));
            if (sensor is null) return null;
            return GiosJsonParser.ParseLatest(await GetStringAsync($"data/getData/{sensor.Id}?size=24", ct));
        }

        var pm25 = await Latest("PM2.5");
        var pm10 = await Latest("PM10");
        var no2 = await Latest("NO2");

        string? index = null;
        try
        {
            index = GiosJsonParser.ParseIndexName(await GetStringAsync($"aqindex/getIndex/{station.Id}", ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            logger.LogDebug(ex, "No GIOŚ index for {Station}", station.Name);
        }

        var measuredAt = new[] { pm25, pm10, no2 }
            .Where(x => x is not null)
            .Select(x => x!.Value.MeasuredAt)
            .DefaultIfEmpty(DateTimeOffset.UtcNow)
            .Max();

        return new AirQualityMeasurement(
            $"gios-{station.Id}",
            station.Name,
            station.Location,
            measuredAt,
            pm10?.Value,
            pm25?.Value,
            no2?.Value,
            GiosJsonParser.SourceName,
            index);
    }

    private async Task<IReadOnlyList<(GiosStation Station, IReadOnlyList<GiosSensor> Sensors)>> GetStationsAsync(CancellationToken ct)
    {
        if (_stations is not null && DateTimeOffset.UtcNow - _stationsLoadedAt < StationListLifetime) return _stations;

        await _stationLock.WaitAsync(ct);
        try
        {
            if (_stations is not null && DateTimeOffset.UtcNow - _stationsLoadedAt < StationListLifetime) return _stations;

            var stations = GiosJsonParser.ParseStations(await GetStringAsync("station/findAll?size=500", ct), _options.GiosCity);
            var list = new List<(GiosStation, IReadOnlyList<GiosSensor>)>();
            foreach (var station in stations)
            {
                list.Add((station, GiosJsonParser.ParseSensors(await GetStringAsync($"station/sensors/{station.Id}", ct))));
            }

            _stations = list;
            _stationsLoadedAt = DateTimeOffset.UtcNow;
            return list;
        }
        finally
        {
            _stationLock.Release();
        }
    }

    private Task<string> GetStringAsync(string relative, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(GtfsDatasetProvider.HttpClientName);
        var baseUrl = _options.GiosBaseUrl.EndsWith('/') ? _options.GiosBaseUrl : _options.GiosBaseUrl + "/";
        return client.GetStringAsync(new Uri(new Uri(baseUrl), relative), ct);
    }
}
