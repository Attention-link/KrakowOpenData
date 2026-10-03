using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Infrastructure.Common;
using KrakowOpenData.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Infrastructure.Gtfs;

/// <summary>Supplies the current merged GTFS timetable.</summary>
public interface IGtfsDatasetProvider
{
    Task<GtfsDataset> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Downloads every configured GTFS zip, parses and merges them, and keeps the result in memory
/// for <see cref="KrakowDataOptions.StaticRefreshMinutes"/>. A failing feed is logged and skipped
/// so one broken file doesn't take the whole API down.
/// </summary>
public sealed class GtfsDatasetProvider : IGtfsDatasetProvider
{
    public const string HttpClientName = "krakow-open-data";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly KrakowDataOptions _options;
    private readonly ILogger<GtfsDatasetProvider> _logger;
    private readonly RefreshingCache<GtfsDataset> _cache;

    public GtfsDatasetProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<KrakowDataOptions> options,
        IClock clock,
        ILogger<GtfsDatasetProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
        _cache = new RefreshingCache<GtfsDataset>(LoadAsync, TimeSpan.FromMinutes(_options.StaticRefreshMinutes), clock);
    }

    public Task<GtfsDataset> GetAsync(CancellationToken cancellationToken = default) => _cache.GetAsync(cancellationToken);

    private async Task<GtfsDataset> LoadAsync(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var parsed = new List<GtfsDataset>();
        Exception? lastError = null;

        foreach (var feed in _options.GtfsFeeds.Where(f => !string.IsNullOrWhiteSpace(f.StaticUrl)))
        {
            try
            {
                _logger.LogInformation("Downloading GTFS feed {Feed} from {Url}", feed.Key, feed.StaticUrl);
                var bytes = await client.GetByteArrayAsync(feed.StaticUrl, ct);
                using var stream = new MemoryStream(bytes, writable: false);
                var dataset = GtfsStaticParser.ParseZip(stream, feed.Key);
                _logger.LogInformation(
                    "GTFS feed {Feed}: {Stops} stops, {Routes} routes, {Trips} trips, {StopTimes} stop times",
                    feed.Key, dataset.Stops.Count, dataset.Routes.Count, dataset.Trips.Count, dataset.StopTimeCount);
                parsed.Add(dataset);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Could not load GTFS feed {Feed}", feed.Key);
            }
        }

        if (parsed.Count == 0 && lastError is not null)
            throw new HttpRequestException("No GTFS feed could be loaded.", lastError);

        return GtfsDataset.Merge(parsed);
    }
}
