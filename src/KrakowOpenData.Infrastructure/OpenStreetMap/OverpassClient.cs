using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Infrastructure.Common;
using KrakowOpenData.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Infrastructure.OpenStreetMap;

/// <summary>P+R car parks and amenities for Kraków from one Overpass query, downloaded in the background.</summary>
public sealed class OverpassClient(
    OverpassQueryRunner runner, ISnapshotStore store, IOptions<KrakowDataOptions> options, IClock clock, ILogger<OverpassClient> logger)
{
    public BackgroundDataset<OsmSnapshot> Dataset { get; } = new(
        "osm-amenities",
        "OpenStreetMap amenities and P+R car parks",
        ct => runner.RunAsync(OverpassParser.BuildQuery(options.Value.OsmAreaName), ct),
        OverpassParser.Parse,
        store,
        clock,
        TimeSpan.FromMinutes(Math.Max(10, options.Value.OsmRefreshMinutes)),
        TimeSpan.FromSeconds(Math.Max(1, options.Value.FirstLoadWaitSeconds)),
        logger);

    public Task<OsmSnapshot> GetAsync(CancellationToken ct) => Dataset.GetAsync(ct);
}
