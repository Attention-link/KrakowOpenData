using KrakowOpenData.Domain.ClimateAndCrisis;
using KrakowOpenData.Domain.Environment;
using KrakowOpenData.Infrastructure.Gtfs;
using KrakowOpenData.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Infrastructure.Imgw;

/// <summary>Thin HTTP client for the IMGW public API. Parsing lives in <see cref="ImgwJsonParser"/>.</summary>
public sealed class ImgwClient(IHttpClientFactory httpClientFactory, IOptions<KrakowDataOptions> options)
{
    private readonly KrakowDataOptions _options = options.Value;

    public async Task<IReadOnlyList<WeatherObservation>> GetSynopAsync(CancellationToken ct)
    {
        var json = await GetStringAsync($"synop/station/{Uri.EscapeDataString(_options.ImgwSynopStation)}", ct);
        return ImgwJsonParser.ParseSynop(json);
    }

    public async Task<IReadOnlyList<HydroObservation>> GetHydroAsync(CancellationToken ct)
    {
        // /hydro includes coordinates and warning/alarm levels. (/hydro2 returned an empty body when checked.)
        var json = await GetStringAsync("hydro", ct);
        return ImgwJsonParser.ParseHydro(json, _options.ImgwHydroStationNameFilters, _options.ImgwHydroStationIds,
            _options.ImgwHydroArea.Contains);
    }

    public async Task<IReadOnlyList<WeatherWarning>> GetWarningsAsync(CancellationToken ct)
    {
        var meteo = ImgwJsonParser.ParseWarnings(await GetStringAsync("warningsmeteo", ct));
        IReadOnlyList<WeatherWarning> hydro;
        try
        {
            hydro = ImgwJsonParser.ParseHydroWarnings(await GetStringAsync("warningshydro", ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            hydro = [];
        }

        return meteo.Concat(hydro).ToList();
    }

    private async Task<string> GetStringAsync(string relative, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(GtfsDatasetProvider.HttpClientName);
        var baseUrl = _options.ImgwBaseUrl.EndsWith('/') ? _options.ImgwBaseUrl : _options.ImgwBaseUrl + "/";
        return await client.GetStringAsync(new Uri(new Uri(baseUrl), relative), ct);
    }
}
