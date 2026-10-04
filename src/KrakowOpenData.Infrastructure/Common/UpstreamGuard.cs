using System.Net;
using KrakowOpenData.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace KrakowOpenData.Infrastructure.Common;

/// <summary>
/// A public fair-use service asked us to back off (429, or a 5xx with Retry-After), or keeps failing. The call was not
/// made; callers treat it like any other unreachable upstream (the API answers 503, routing falls back to a straight line).
/// </summary>
public sealed class UpstreamBusyException(string service, TimeSpan retryAfter)
    : HttpRequestException(
        $"{service} is busy right now. Try again in about {Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds))} seconds.",
        null,
        HttpStatusCode.ServiceUnavailable)
{
    public string Service { get; } = service;

    public TimeSpan RetryAfter { get; } = retryAfter;
}

/// <summary>
/// Shared by every call to one public fair-use service (Photon, the OSRM foot router), so a burst of users (an event where
/// hundreds of phones search at once) queues briefly instead of hammering it:
/// <list type="bullet">
/// <item>at most <c>maxConcurrent</c> requests run at once; a call that cannot start within <c>maxQueueWait</c> gives up as busy;</item>
/// <item>429 pauses all calls for the Retry-After time (default <see cref="RateLimitedPause"/>), a 5xx with Retry-After likewise;</item>
/// <item><see cref="FailuresToOpen"/> failures in a row (5xx, timeout, network) pause calls for <see cref="FailurePause"/>;</item>
/// <item>while paused, calls fail at once with <see cref="UpstreamBusyException"/>: no retries, no waiting on a dead server.</item>
/// </list>
/// Pauses are capped at <see cref="MaxPause"/>. Any other answer (including a 4xx such as OSRM's 400 "NoRoute") means the
/// service is up and resets the failure count.
/// </summary>
public sealed class UpstreamGuard(string service, int maxConcurrent, TimeSpan maxQueueWait, IClock clock, ILogger logger)
{
    public const int FailuresToOpen = 3;
    public static readonly TimeSpan FailurePause = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan RateLimitedPause = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaxPause = TimeSpan.FromMinutes(2);

    private readonly SemaphoreSlim _gate = new(Math.Max(1, maxConcurrent));
    private readonly object _lock = new();
    private DateTimeOffset _pausedUntil = DateTimeOffset.MinValue;
    private int _failures;

    /// <summary>How long calls are still skipped (zero when they are not).</summary>
    public TimeSpan RemainingPause
    {
        get
        {
            lock (_lock)
            {
                var left = _pausedUntil - clock.UtcNow;
                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
        }
    }

    /// <summary>GETs <paramref name="url"/> and returns the body; throws <see cref="HttpRequestException"/> for any non-success.</summary>
    public async Task<string> GetStringAsync(HttpClient client, string url, CancellationToken ct)
    {
        ThrowIfPaused();
        if (!await _gate.WaitAsync(maxQueueWait, ct)) throw new UpstreamBusyException(service, maxQueueWait);
        try
        {
            ThrowIfPaused();   // it may have paused while this call waited for a slot

            HttpResponseMessage response;
            try
            {
                response = await client.GetAsync(url, ct);
            }
            catch (Exception ex) when (ex is (HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
            {
                Failed();   // network error, or the HttpClient timeout
                throw;
            }

            using (response)
            {
                var status = (int)response.StatusCode;
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    Pause(RetryAfter(response) ?? RateLimitedPause, "HTTP 429");
                    throw new UpstreamBusyException(service, RemainingPause);
                }

                if (status >= 500)
                {
                    if (RetryAfter(response) is { } wait) Pause(wait, $"HTTP {status}");
                    else Failed();
                }
                else Succeeded();

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ThrowIfPaused()
    {
        var left = RemainingPause;
        if (left > TimeSpan.Zero) throw new UpstreamBusyException(service, left);
    }

    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta) return delta;
        if (header?.Date is { } date) return date - clock.UtcNow;
        return null;
    }

    private void Pause(TimeSpan wait, string reason)
    {
        if (wait < TimeSpan.FromSeconds(1)) wait = TimeSpan.FromSeconds(1);
        if (wait > MaxPause) wait = MaxPause;
        lock (_lock)
        {
            _failures = 0;
            var until = clock.UtcNow + wait;
            if (until <= _pausedUntil) return;
            _pausedUntil = until;
        }

        logger.LogWarning("{Service} paused for {Seconds} s after {Reason}", service, (int)Math.Ceiling(wait.TotalSeconds), reason);
    }

    private void Failed()
    {
        bool open;
        lock (_lock) open = ++_failures >= FailuresToOpen;
        if (open) Pause(FailurePause, $"{FailuresToOpen} failures in a row");
    }

    private void Succeeded()
    {
        lock (_lock) _failures = 0;
    }
}
