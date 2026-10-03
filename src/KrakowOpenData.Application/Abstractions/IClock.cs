namespace KrakowOpenData.Application.Abstractions;

/// <summary>Abstracts "now" so time-dependent logic can be tested.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Helpers for Kraków local time (Europe/Warsaw), which timetables use.</summary>
public static class KrakowTime
{
    private static readonly Lazy<TimeZoneInfo> Zone = new(ResolveZone);

    public static TimeZoneInfo TimeZone => Zone.Value;

    public static DateTimeOffset ToLocal(DateTimeOffset moment) => TimeZoneInfo.ConvertTime(moment, TimeZone);

    public static DateOnly LocalDate(DateTimeOffset moment) => DateOnly.FromDateTime(ToLocal(moment).DateTime);

    /// <summary>
    /// Converts a GTFS service date plus a time-of-day offset (may exceed 24h) to an absolute moment.
    /// </summary>
    public static DateTimeOffset FromServiceDay(DateOnly serviceDate, TimeSpan offset)
    {
        var local = serviceDate.ToDateTime(TimeOnly.MinValue).Add(offset);
        var utcOffset = TimeZone.GetUtcOffset(local);
        return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), utcOffset);
    }

    private static TimeZoneInfo ResolveZone()
    {
        foreach (var id in new[] { "Europe/Warsaw", "Central European Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }
}
