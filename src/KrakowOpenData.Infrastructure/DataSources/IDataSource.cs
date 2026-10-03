using KrakowOpenData.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace KrakowOpenData.Infrastructure.DataSources;

/// <summary>
/// Produces the full current list of one kind of record from somewhere (a feed, an API, a seed file).
/// Repositories sit on top of data sources; swapping a source never touches the Application layer.
/// </summary>
public interface IDataSource<T>
{
    Task<IReadOnlyList<T>> LoadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Adapter: a data source backed by a delegate.</summary>
public sealed class DelegateDataSource<T>(Func<CancellationToken, Task<IReadOnlyList<T>>> load) : IDataSource<T>
{
    public Task<IReadOnlyList<T>> LoadAsync(CancellationToken cancellationToken = default) => load(cancellationToken);
}

/// <summary>Decorator: caches another source for a time-to-live, serving stale data if a refresh fails.</summary>
public sealed class CachedDataSource<T> : IDataSource<T>
{
    private readonly Common.RefreshingCache<IReadOnlyList<T>> _cache;

    public CachedDataSource(IDataSource<T> inner, TimeSpan timeToLive, IClock clock)
    {
        _cache = new Common.RefreshingCache<IReadOnlyList<T>>(inner.LoadAsync, timeToLive, clock);
    }

    public Task<IReadOnlyList<T>> LoadAsync(CancellationToken cancellationToken = default) => _cache.GetAsync(cancellationToken);
}

/// <summary>
/// Composite: concatenates several sources. A failing source is logged and skipped, so e.g. the bundled
/// historical river readings still show when the live IMGW API is unreachable. Throws only if all fail.
/// </summary>
public sealed class CompositeDataSource<T>(IReadOnlyList<IDataSource<T>> sources, ILogger? logger = null) : IDataSource<T>
{
    public async Task<IReadOnlyList<T>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<T>();
        var failures = new List<Exception>();

        foreach (var source in sources)
        {
            try
            {
                results.AddRange(await source.LoadAsync(cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add(ex);
                logger?.LogWarning(ex, "Data source {Source} failed; continuing with the others", source.GetType().Name);
            }
        }

        if (failures.Count > 0 && failures.Count == sources.Count)
            throw new AggregateException("All data sources failed.", failures);

        return results;
    }
}
