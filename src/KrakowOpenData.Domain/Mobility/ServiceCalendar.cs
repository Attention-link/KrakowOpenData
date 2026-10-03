namespace KrakowOpenData.Domain.Mobility;

/// <summary>Weekly service pattern from GTFS calendar.txt.</summary>
public sealed record ServiceCalendar(
    string ServiceId,
    IReadOnlySet<DayOfWeek> Days,
    DateOnly StartDate,
    DateOnly EndDate);

/// <summary>A one-off change from GTFS calendar_dates.txt.</summary>
public sealed record ServiceException(string ServiceId, DateOnly Date, bool IsAdded);

public static class ServiceDayRules
{
    /// <summary>
    /// Decides whether a service runs on a date: exceptions win, otherwise the weekly pattern applies
    /// within its date range. A service with no calendar runs only on dates added as exceptions.
    /// </summary>
    public static bool IsActiveOn(
        DateOnly date,
        ServiceCalendar? calendar,
        IEnumerable<ServiceException>? exceptions)
    {
        var exception = exceptions?.FirstOrDefault(e => e.Date == date);
        if (exception is not null) return exception.IsAdded;

        return calendar is not null &&
               date >= calendar.StartDate &&
               date <= calendar.EndDate &&
               calendar.Days.Contains(date.DayOfWeek);
    }
}
