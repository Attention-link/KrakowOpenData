using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Domain.UrbanSpace;
using KrakowOpenData.Infrastructure.Common;
using KrakowOpenData.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Infrastructure.OpenStreetMap;

/// <summary>~27,000 street lamps for Kraków from OpenStreetMap, downloaded in the background.</summary>
public sealed class StreetLightsClient(
    OverpassQueryRunner runner, ISnapshotStore store, IOptions<KrakowDataOptions> options, IClock clock, ILogger<StreetLightsClient> logger)
{
    public BackgroundDataset<IReadOnlyList<StreetLight>> Dataset { get; } = new(
        "osm-street-lights",
        "Street lights",
        ct => runner.RunAsync(StreetLightParser.BuildQuery(options.Value.OsmAreaName), ct),
        StreetLightParser.Parse,
        store,
        clock,
        TimeSpan.FromMinutes(Math.Max(10, options.Value.OsmRefreshMinutes)),
        TimeSpan.FromSeconds(Math.Max(1, options.Value.FirstLoadWaitSeconds)),
        logger);

    public Task<IReadOnlyList<StreetLight>> GetAsync(CancellationToken ct) => Dataset.GetAsync(ct);
}
