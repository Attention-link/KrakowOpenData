using System.Net;
using KrakowOpenData.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Infrastructure.OpenStreetMap;

/// <summary>
/// Sends Overpass queries one at a time with a pause between them (Overpass answers 429 to
/// back-to-back requests). If a server is busy, slow or down, the same query goes to the next
/// mirror in <see cref="KrakowDataOptions.OverpassUrls"/>.
/// </summary>
public sealed class OverpassQueryRunner(
    IHttpClientFactory httpClientFactory,
    IOptions<KrakowDataOptions> options,
    ILogger<OverpassQueryRunner> logger)
{
    /// <summary>Named HttpClient with the long Overpass timeout (configured in DependencyInjection).</summary>
    public const string HttpClientName = "Overpass";

    private static readonly TimeSpan MinimumGap = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastFinished = DateTimeOffset.MinValue;

    public async Task<string> RunAsync(string query, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var urls = options.Value.OverpassUrls.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
            if (urls.Count == 0) throw new InvalidOperationException("No Overpass URLs configured (KrakowData:OverpassUrls).");

            Exception? last = null;
            foreach (var url in urls)
            {
                var wait = _lastFinished + MinimumGap - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);

                try
                {
                    return await PostAsync(url, query, ct);
                }
                catch (Exception ex) when (IsWorthTryingAnotherMirror(ex, ct))
                {
                    last = ex;
                    logger.LogWarning("Overpass at {Url} failed ({Reason}); trying the next mirror", url, ex.Message);
                }
                finally
                {
                    _lastFinished = DateTimeOffset.UtcNow;
                }
            }

            throw last!;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> PostAsync(string url, string query, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var body = new FormUrlEncodedContent([new KeyValuePair<string, string>("data", query)]);
        using var response = await client.PostAsync(url, body, ct);
        if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.GatewayTimeout or HttpStatusCode.ServiceUnavailable)
            throw new HttpRequestException($"Overpass busy ({(int)response.StatusCode})", null, response.StatusCode);

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(ct);

        // Overpass reports server-side timeouts inside a 200 response.
        if (content.Contains("\"remark\"", StringComparison.Ordinal) && content.Contains("runtime error", StringComparison.OrdinalIgnoreCase))
            throw new HttpRequestException("Overpass query timed out on the server");

        return content;
    }

    /// <summary>Network errors, timeouts and busy/failed servers: another mirror may succeed. Caller cancellation is not retried.</summary>
    private static bool IsWorthTryingAnotherMirror(Exception ex, CancellationToken ct) =>
        !ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or IOException;
}
