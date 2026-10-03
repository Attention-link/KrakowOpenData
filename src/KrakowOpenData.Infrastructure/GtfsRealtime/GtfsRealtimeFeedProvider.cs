using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Infrastructure.Common;
using KrakowOpenData.Infrastructure.Gtfs;
using KrakowOpenData.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Infrastructure.GtfsRealtime;

/// <summary>Supplies the latest merged GTFS-Realtime snapshot.</summary>
public interface IGtfsRealtimeFeedProvider
{
    Task<GtfsRealtimeFeed> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Polls VehiclePositions, TripUpdates and ServiceAlerts for every configured feed, at most once per
/// <see cref="KrakowDataOptions.RealtimeRefreshSeconds"/>. Individual file failures are logged and skipped.
/// </summary>
public sealed class GtfsRealtimeFeedProvider : IGtfsRealtimeFeedProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly KrakowDataOptions _options;
    private readonly ILogger<GtfsRealtimeFeedProvider> _logger;
    private readonly RefreshingCache<GtfsRealtimeFeed> _cache;

    public GtfsRealtimeFeedProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<KrakowDataOptions> options,
        IClock clock,
        ILogger<GtfsRealtimeFeedProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
        _cache = new RefreshingCache<GtfsRealtimeFeed>(
            LoadAsync, TimeSpan.FromSeconds(Math.Max(5, _options.RealtimeRefreshSeconds)), clock);
    }

    public Task<GtfsRealtimeFeed> GetAsync(CancellationToken cancellationToken = default) => _cache.GetAsync(cancellationToken);

    private async Task<GtfsRealtimeFeed> LoadAsync(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(GtfsDatasetProvider.HttpClientName);
        var tasks = new List<Task<GtfsRealtimeFeed>>();

        foreach (var feed in _options.GtfsFeeds)
        {
            foreach (var url in new[] { feed.VehiclePositionsUrl, feed.TripUpdatesUrl, feed.ServiceAlertsUrl })
            {
                if (!string.IsNullOrWhiteSpace(url)) tasks.Add(FetchAsync(client, url, feed.Key, ct));
            }
        }

        var results = await Task.WhenAll(tasks);
        return GtfsRealtimeFeed.Merge(results);
    }

    private async Task<GtfsRealtimeFeed> FetchAsync(HttpClient client, string url, string feedKey, CancellationToken ct)
    {
        try
        {
            var bytes = await client.GetByteArrayAsync(url, ct);
            return GtfsRealtimeDecoder.Decode(bytes, feedKey);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not load realtime file {Url}", url);
            return GtfsRealtimeFeed.Empty;
        }
    }
}
