using System.Collections.Concurrent;
using KrakowOpenData.Application.Abstractions;

namespace KrakowOpenData.Infrastructure.Ai;

/// <summary>
/// Keeps voice transcription inside the Workers AI free tier and stops one device from using it all: at most
/// <see cref="PerDevicePerHour"/> recordings per device an hour and <see cref="PerDay"/> in total a day. In memory only.
/// </summary>
public sealed class VoiceQuota(IClock clock)
{
    public const int PerDevicePerHour = 10;
    public const int PerDay = 300;

    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _byDevice = new(StringComparer.Ordinal);
    private DateOnly _day;
    private int _today;

    public bool TryTake(string deviceId)
    {
        var now = clock.UtcNow;
        lock (_lock)
        {
            var day = DateOnly.FromDateTime(now.UtcDateTime);
            if (day != _day) { _day = day; _today = 0; _byDevice.Clear(); }
            if (_today >= PerDay) return false;
            var times = _byDevice.GetOrAdd(deviceId, _ => []);
            times.RemoveAll(t => t < now.AddHours(-1));
            if (times.Count >= PerDevicePerHour) return false;
            times.Add(now);
            _today++;
            return true;
        }
    }
}
