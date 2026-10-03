using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Domain.PublicServices;
using KrakowOpenData.Infrastructure.Gtfs;
using KrakowOpenData.Infrastructure.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Infrastructure.Nfz;

/// <summary>
/// Searches NFZ waiting lists for Kraków (no key). Each search is cached for an hour, as NFZ
/// updates the statistics monthly and the API is slow.
/// </summary>
public sealed class NfzClient(IHttpClientFactory httpClientFactory, IOptions<KrakowDataOptions> options, IMemoryCache cache)
    : IWaitingListSource
{
    private readonly KrakowDataOptions _options = options.Value;

    public async Task<IReadOnlyList<WaitingListEntry>> SearchAsync(string benefit, bool urgent, CancellationToken cancellationToken = default)
    {
        var key = $"nfz:{urgent}:{benefit.ToUpperInvariant()}";
        if (cache.TryGetValue(key, out IReadOnlyList<WaitingListEntry>? cached) && cached is not null) return cached;

        var result = await DownloadAsync(benefit, urgent, cancellationToken);
        cache.Set(key, result, TimeSpan.FromHours(1));
        return result;
    }

    private async Task<IReadOnlyList<WaitingListEntry>> DownloadAsync(string benefit, bool urgent, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(GtfsDatasetProvider.HttpClientName);
        var baseUri = new Uri(_options.NfzBaseUrl.EndsWith('/') ? _options.NfzBaseUrl : _options.NfzBaseUrl + "/");
        var first = new Uri(baseUri,
            $"queues?page=1&limit=25&format=json&case={(urgent ? 2 : 1)}" +
            $"&province={Uri.EscapeDataString(_options.NfzProvince)}" +
            $"&locality={Uri.EscapeDataString(_options.NfzLocality)}" +
            $"&benefit={Uri.EscapeDataString(benefit)}&api-version=1.3");

        var entries = new List<WaitingListEntry>();
        Uri? next = first;
        for (var page = 0; next is not null && page < Math.Max(1, _options.NfzMaxPages); page++)
        {
            var result = NfzJsonParser.ParseQueues(await client.GetStringAsync(next, ct));
            entries.AddRange(result.Entries);
            next = result.Next is null || result.Entries.Count == 0 ? null : new Uri(baseUri, result.Next);
        }

        return entries.DistinctBy(e => e.Id).ToList();
    }
}
