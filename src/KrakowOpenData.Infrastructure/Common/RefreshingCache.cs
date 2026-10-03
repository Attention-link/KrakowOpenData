using KrakowOpenData.Application.Abstractions;

namespace KrakowOpenData.Infrastructure.Common;

/// <summary>
/// Holds one value produced by an async factory and refreshes it after a time-to-live.
/// Concurrent callers share one refresh. If a refresh fails, the last good value is served
/// (stale-if-error); if there is no previous value, the error propagates.
/// </summary>
public sealed class RefreshingCache<T>(Func<CancellationToken, Task<T>> factory, TimeSpan timeToLive, IClock clock)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private T? _value;
    private bool _hasValue;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public DateTimeOffset? LastRefreshed { get; private set; }

    public Exception? LastError { get; private set; }

    public async Task<T> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_hasValue && clock.UtcNow < _expiresAt) return _value!;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_hasValue && clock.UtcNow < _expiresAt) return _value!;

            try
            {
                var fresh = await factory(cancellationToken);
                _value = fresh;
                _hasValue = true;
                LastRefreshed = clock.UtcNow;
                LastError = null;
                _expiresAt = clock.UtcNow + timeToLive;
                return fresh;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && _hasValue)
            {
                // Serve stale data and retry sooner than a full TTL.
                LastError = ex;
                _expiresAt = clock.UtcNow + TimeSpan.FromTicks(Math.Min(timeToLive.Ticks, TimeSpan.FromSeconds(30).Ticks));
                return _value!;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate() => _expiresAt = DateTimeOffset.MinValue;
}
