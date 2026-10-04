using System.Collections.Concurrent;
using KrakowOpenData.Application.Abstractions;

namespace KrakowOpenData.Infrastructure.Ai;

/// <summary>
/// Keeps Workers AI inside the free tier and stops one device (or one Telegram chat) from using it all. Voice: at most
/// <see cref="PerDevicePerHour"/> recordings per device an hour and <see cref="PerDay"/> in total a day. Text triage asked for
/// by the bot: <see cref="TriagePerDevicePerHour"/> and <see cref="TriagePerDay"/>. In memory only.
/// </summary>
public sealed class VoiceQuota(IClock clock)
{
    public const int PerDevicePerHour = 10;
    public const int PerDay = 300;
    public const int TriagePerDevicePerHour = 20;
    public const int TriagePerDay = 500;

    private readonly Counter _voice = new(PerDevicePerHour, PerDay);
    private readonly Counter _triage = new(TriagePerDevicePerHour, TriagePerDay);

    /// <summary>One voice transcription for <paramref name="deviceId"/>; false when its hour or the day is used up.</summary>
    public bool TryTake(string deviceId) => _voice.TryTake(deviceId, clock.UtcNow);

    /// <summary>One text triage for <paramref name="deviceId"/>; false when its hour or the day is used up.</summary>
    public bool TryTakeTriage(string deviceId) => _triage.TryTake(deviceId, clock.UtcNow);

    private sealed class Counter(int perDevicePerHour, int perDay)
    {
        private readonly object _lock = new();
        private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _byDevice = new(StringComparer.Ordinal);
        private DateOnly _day;
        private int _today;

        public bool TryTake(string deviceId, DateTimeOffset now)
        {
            lock (_lock)
            {
                var day = DateOnly.FromDateTime(now.UtcDateTime);
                if (day != _day) { _day = day; _today = 0; _byDevice.Clear(); }
                if (_today >= perDay) return false;
                var times = _byDevice.GetOrAdd(deviceId, _ => []);
                times.RemoveAll(t => t < now.AddHours(-1));
                if (times.Count >= perDevicePerHour) return false;
                times.Add(now);
                _today++;
                return true;
            }
        }
    }
}
