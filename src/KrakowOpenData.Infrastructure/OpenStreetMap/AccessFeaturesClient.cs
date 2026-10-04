using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Accessibility;
using KrakowOpenData.Domain.Accessibility;
using KrakowOpenData.Infrastructure.Common;
using KrakowOpenData.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Infrastructure.OpenStreetMap;

/// <summary>
/// Steps, kerbs, lifts, entrances, places with wheelchair tags, toilets, benches, tactile paving and path surfaces from
/// OpenStreetMap for <see cref="KrakowDataOptions.AccessArea"/>, downloaded in the background (with the same mirrors, file
/// cache and daily refresh as the other OSM datasets).
/// </summary>
public sealed class AccessFeaturesClient(
    OverpassQueryRunner runner, ISnapshotStore store, IOptions<KrakowDataOptions> options, IClock clock, ILogger<AccessFeaturesClient> logger) : IAccessDataStatus
{
    public BackgroundDataset<IReadOnlyList<AccessFeature>> Dataset { get; } = new(
        "osm-access-v1",
        "Accessibility barriers and amenities",
        ct => runner.RunAsync(AccessFeaturesParser.BuildQuery(options.Value.AccessArea), ct),
        AccessFeaturesParser.Parse,
        store,
        clock,
        TimeSpan.FromMinutes(Math.Max(10, options.Value.OsmRefreshMinutes)),
        TimeSpan.FromSeconds(Math.Max(1, options.Value.FirstLoadWaitSeconds)),
        logger);

    public DateTimeOffset? LoadedAt => Dataset.LoadedAt;

    public Task<IReadOnlyList<AccessFeature>> GetAsync(CancellationToken ct) => Dataset.GetAsync(ct);
}
