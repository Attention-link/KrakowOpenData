using KrakowOpenData.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace KrakowOpenData.Infrastructure.Common;

/// <summary>
/// A dataset that is slow to download (e.g. ~27,000 street lamps from OpenStreetMap). It is downloaded
/// in the background and kept in memory; requests get the latest copy immediately and never wait for
/// the source, except on the very first start (then for at most <c>firstLoadWait</c>, after which a
/// <see cref="DataSourceLoadingException"/> tells the caller to retry). The raw download is saved via
/// <see cref="ISnapshotStore"/>, so after a restart the saved copy is served while a refresh runs.
/// A failed refresh keeps the previous copy.
/// </summary>
public sealed class BackgroundDataset<T>(
    string name,
    string displayName,
    Func<CancellationToken, Task<string>> download,
    Func<string, T> parse,
    ISnapshotStore store,
    IClock clock,
    TimeSpan refreshEvery,
    TimeSpan firstLoadWait,
    ILogger logger)
    where T : class
{
    private readonly object _lock = new();
    private T? _current;
    private DateTimeOffset _loadedAt;
    private Task? _refresh;
    private Exception? _lastError;
    private DateTimeOffset _nextAttemptAt = DateTimeOffset.MinValue;
    private bool _triedDisk;

    /// <summary>After a failed download, wait this long before trying the source again.</summary>
    public static readonly TimeSpan RetryPause = TimeSpan.FromMinutes(2);

    public string Name { get; } = name;

    public DateTimeOffset? LoadedAt => _current is null ? null : _loadedAt;

    /// <summary>Loads the saved copy (if any) and starts a refresh when it is missing or stale.</summary>
    public async Task WarmUpAsync(CancellationToken ct)
    {
        await LoadFromDiskAsync(ct);
        var refresh = EnsureRefreshIfStale();
        if (refresh is not null) await refresh.ConfigureAwait(false);
    }

    public async Task<T> GetAsync(CancellationToken ct)
    {
        if (_current is null) await LoadFromDiskAsync(ct);
        _ = EnsureRefreshIfStale(); // refresh in the background; this request does not wait for it

        if (_current is { } ready) return ready;

        // First ever load: wait a little, then tell the caller to come back.
        var refresh = _refresh ?? EnsureRefreshIfStale();
        if (refresh is not null)
        {
            var finished = await Task.WhenAny(refresh, Task.Delay(firstLoadWait, ct)).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (finished == refresh && _current is { } loaded) return loaded;
        }

        if (_current is { } late) return late;
        if (_lastError is not null && _refresh is null) throw _lastError;
        throw new DataSourceLoadingException(displayName, TimeSpan.FromSeconds(30));
    }

    /// <summary>For tests and diagnostics: the error of the last failed download, if any.</summary>
    public Exception? LastError => _lastError;

    private async Task LoadFromDiskAsync(CancellationToken ct)
    {
        if (_triedDisk) return;
        _triedDisk = true;

        var saved = await store.LoadAsync(Name, ct).ConfigureAwait(false);
        if (saved is not { } snapshot) return;
        try
        {
            var value = parse(snapshot.Content);
            lock (_lock)
            {
                if (_current is not null) return;
                _current = value;
                _loadedAt = snapshot.SavedAt;
            }

            logger.LogInformation("Loaded saved {Name} from {SavedAt:u}", Name, snapshot.SavedAt);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Saved {Name} could not be read; downloading again", Name);
        }
    }

    /// <summary>Starts one background refresh if the data is missing or older than the refresh interval.</summary>
    private Task? EnsureRefreshIfStale()
    {
        lock (_lock)
        {
            if (_refresh is not null) return _refresh;
            if (_current is not null && clock.UtcNow - _loadedAt < refreshEvery) return null;
            if (clock.UtcNow < _nextAttemptAt) return null;
            _refresh = Task.Run(RefreshAsync);
            return _refresh;
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var content = await download(CancellationToken.None).ConfigureAwait(false);
            var value = parse(content);
            lock (_lock)
            {
                _current = value;
                _loadedAt = clock.UtcNow;
                _lastError = null;
            }

            await store.SaveAsync(Name, content, CancellationToken.None).ConfigureAwait(false);
            logger.LogInformation("Downloaded {Name}", Name);
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                _lastError = ex;
                _nextAttemptAt = clock.UtcNow + RetryPause;
            }

            logger.LogWarning(ex, "Downloading {Name} failed; {State}", Name,
                _current is null ? "will retry on the next request" : "keeping the previous copy");
        }
        finally
        {
            lock (_lock) _refresh = null;
        }
    }
}
