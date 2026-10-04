using KrakowOpenData.Application.Abstractions;

namespace KrakowOpenData.Infrastructure.Common;

/// <summary>
/// A small in-memory cache with a fixed time to live and a hard cap on entries: when full, the oldest entry goes.
/// Used for answers from public fair-use services (address search, walking routes), where the shared
/// <c>IMemoryCache</c> has no size limit and a busy event could fill memory with one-off queries.
/// </summary>
public sealed class BoundedTtlCache<TValue>(int capacity, TimeSpan timeToLive, IClock clock)
{
    private readonly Dictionary<string, LinkedListNode<Entry>> _map = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _order = new();   // oldest first
    private readonly object _lock = new();

    public int Capacity { get; } = Math.Max(1, capacity);

    public int Count
    {
        get { lock (_lock) return _map.Count; }
    }

    public bool TryGet(string key, out TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                if (node.Value.Expires > clock.UtcNow)
                {
                    value = node.Value.Value;
                    return true;
                }

                _order.Remove(node);
                _map.Remove(key);
            }
        }

        value = default!;
        return false;
    }

    public void Set(string key, TValue value)
    {
        lock (_lock)
        {
            if (_map.Remove(key, out var old)) _order.Remove(old);
            _map[key] = _order.AddLast(new Entry(key, value, clock.UtcNow + timeToLive));
            while (_map.Count > Capacity && _order.First is { } first)
            {
                _order.RemoveFirst();
                _map.Remove(first.Value.Key);
            }
        }
    }

    private sealed record Entry(string Key, TValue Value, DateTimeOffset Expires);
}
