using System.Collections.Concurrent;
using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Catalog;
using KrakowOpenData.Infrastructure.Common;
using KrakowOpenData.Infrastructure.Gtfs;
using KrakowOpenData.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Infrastructure.OpenDataPortal;

/// <summary>
/// Reads tables from the City of Kraków Open Data API (api.um.krakow.pl), following the
/// <c>$after</c> cursor page by page (100 rows each) until <c>maxRows</c>. Each table is cached
/// (6 hours by default); the data is updated by the city a few times a year at most.
/// </summary>
public sealed class OpenDataPortalClient(IHttpClientFactory httpClientFactory, IOptions<KrakowDataOptions> options, IClock clock)
    : IOpenDataTableReader
{
    public const string SourceName = "Otwarte Dane Kraków (api.um.krakow.pl)";

    private readonly KrakowDataOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, RefreshingCache<OpenDataTableContent>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public Task<OpenDataTableContent> ReadAsync(OpenDataTable table, int maxRows, CancellationToken cancellationToken = default)
    {
        var cache = _cache.GetOrAdd($"{table.Key}:{maxRows}", _ => new RefreshingCache<OpenDataTableContent>(
            ct => DownloadAsync(table, maxRows, ct),
            TimeSpan.FromMinutes(Math.Max(1, _options.OpenDataRefreshMinutes)),
            clock));
        return cache.GetAsync(cancellationToken);
    }

    private async Task<OpenDataTableContent> DownloadAsync(OpenDataTable table, int maxRows, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(GtfsDatasetProvider.HttpClientName);
        var baseUrl = _options.OpenDataApiBaseUrl.EndsWith('/') ? _options.OpenDataApiBaseUrl : _options.OpenDataApiBaseUrl + "/";
        var url = $"{baseUrl}{table.Api}/v1/{table.Table}";

        var rows = new List<IReadOnlyDictionary<string, string?>>();
        string? after = null;
        var truncated = false;

        // Safety cap on pages in case the cursor never ends.
        for (var page = 0; page < 200; page++)
        {
            var pageUrl = after is null ? url : $"{url}?$after={after}";
            var result = OpenDataPortalParser.ParsePage(await client.GetStringAsync(pageUrl, ct));
            rows.AddRange(result.Rows);

            if (rows.Count >= maxRows)
            {
                truncated = rows.Count > maxRows || result.NextAfter is not null;
                if (rows.Count > maxRows) rows.RemoveRange(maxRows, rows.Count - maxRows);
                break;
            }

            if (result.NextAfter is null || result.Rows.Count == 0) break;
            after = result.NextAfter;
        }

        return new OpenDataTableContent(OpenDataPortalParser.Columns(rows), rows, truncated, SourceName);
    }
}
