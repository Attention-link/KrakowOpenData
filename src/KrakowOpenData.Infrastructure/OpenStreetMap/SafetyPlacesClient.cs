using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Domain.Safety;
using KrakowOpenData.Infrastructure.Common;
using KrakowOpenData.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Infrastructure.OpenStreetMap;

/// <summary>Parks, libraries, pharmacies, hospitals and police stations for Kraków from OpenStreetMap, downloaded in the background.</summary>
public sealed class SafetyPlacesClient(
    OverpassQueryRunner runner, ISnapshotStore store, IOptions<KrakowDataOptions> options, IClock clock, ILogger<SafetyPlacesClient> logger)
{
    public BackgroundDataset<IReadOnlyList<SafetyPlace>> Dataset { get; } = new(
        "osm-safety-places",
        "Parks, libraries, pharmacies, hospitals and police stations",
        ct => runner.RunAsync(SafetyPlacesParser.BuildQuery(options.Value.OsmAreaName), ct),
        SafetyPlacesParser.Parse,
        store,
        clock,
        TimeSpan.FromMinutes(Math.Max(10, options.Value.OsmRefreshMinutes)),
        TimeSpan.FromSeconds(Math.Max(1, options.Value.FirstLoadWaitSeconds)),
        logger);

    public Task<IReadOnlyList<SafetyPlace>> GetAsync(CancellationToken ct) => Dataset.GetAsync(ct);
}
