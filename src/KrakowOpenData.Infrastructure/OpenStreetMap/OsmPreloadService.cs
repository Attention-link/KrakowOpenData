using KrakowOpenData.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Infrastructure.OpenStreetMap;

/// <summary>
/// Downloads the OpenStreetMap datasets when the API starts and refreshes them once a day,
/// so pages showing street lights, amenities or P+R never wait for Overpass.
/// </summary>
public sealed class OsmPreloadService(
    OverpassClient amenities,
    StreetLightsClient streetLights,
    SafetyPlacesClient safetyPlaces,
    IOptions<KrakowDataOptions> options,
    ILogger<OsmPreloadService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.PreloadOnStartup) return;

        var every = TimeSpan.FromMinutes(Math.Max(10, options.Value.OsmRefreshMinutes));
        while (!stoppingToken.IsCancellationRequested)
        {
            // One after the other: the shared runner spaces the requests anyway.
            await WarmUp(amenities.Dataset.WarmUpAsync, "amenities", stoppingToken);
            await WarmUp(streetLights.Dataset.WarmUpAsync, "street lights", stoppingToken);
            await WarmUp(safetyPlaces.Dataset.WarmUpAsync, "parks, libraries, pharmacies, hospitals and police", stoppingToken);

            try
            {
                await Task.Delay(every, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task WarmUp(Func<CancellationToken, Task> warmUp, string what, CancellationToken ct)
    {
        try
        {
            await warmUp(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Preloading OpenStreetMap {What} failed; requests will retry", what);
        }
    }
}
