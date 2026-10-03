using KrakowOpenData.Domain.Mobility;

namespace KrakowOpenData.Infrastructure.Gtfs;

/// <summary>An in-memory, indexed GTFS timetable (one feed or several merged).</summary>
public sealed class GtfsDataset
{
    public static GtfsDataset Empty { get; } = new();

    public Dictionary<string, TransitStop> Stops { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, TransitRoute> Routes { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, TransitTrip> Trips { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, List<StopTime>> StopTimesByStop { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, List<StopTime>> StopTimesByTrip { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, ServiceCalendar> Calendars { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, List<ServiceException>> CalendarExceptions { get; } = new(StringComparer.Ordinal);

    public int StopTimeCount { get; private set; }

    public void AddStopTime(StopTime stopTime)
    {
        if (!StopTimesByStop.TryGetValue(stopTime.StopId, out var byStop))
        {
            byStop = [];
            StopTimesByStop[stopTime.StopId] = byStop;
        }

        if (!StopTimesByTrip.TryGetValue(stopTime.TripId, out var byTrip))
        {
            byTrip = [];
            StopTimesByTrip[stopTime.TripId] = byTrip;
        }

        byStop.Add(stopTime);
        byTrip.Add(stopTime);
        StopTimeCount++;
    }

    public void AddException(ServiceException exception)
    {
        if (!CalendarExceptions.TryGetValue(exception.ServiceId, out var list))
        {
            list = [];
            CalendarExceptions[exception.ServiceId] = list;
        }

        list.Add(exception);
    }

    /// <summary>Sorts stop times so lookups return them in departure / sequence order.</summary>
    public void Seal()
    {
        foreach (var list in StopTimesByStop.Values) list.Sort((a, b) => a.Departure.CompareTo(b.Departure));
        foreach (var list in StopTimesByTrip.Values) list.Sort((a, b) => a.StopSequence.CompareTo(b.StopSequence));
    }

    public bool IsServiceActive(string serviceId, DateOnly date)
    {
        Calendars.TryGetValue(serviceId, out var calendar);
        CalendarExceptions.TryGetValue(serviceId, out var exceptions);
        return ServiceDayRules.IsActiveOn(date, calendar, exceptions);
    }

    /// <summary>Combines datasets. Ids are expected to be feed-prefixed, so they don't collide.</summary>
    public static GtfsDataset Merge(IEnumerable<GtfsDataset> datasets)
    {
        var merged = new GtfsDataset();
        foreach (var d in datasets)
        {
            foreach (var (k, v) in d.Stops) merged.Stops[k] = v;
            foreach (var (k, v) in d.Routes) merged.Routes[k] = v;
            foreach (var (k, v) in d.Trips) merged.Trips[k] = v;
            foreach (var (k, v) in d.Calendars) merged.Calendars[k] = v;
            foreach (var list in d.CalendarExceptions.Values)
            foreach (var e in list) merged.AddException(e);
            foreach (var list in d.StopTimesByTrip.Values)
            foreach (var st in list) merged.AddStopTime(st);
        }

        merged.Seal();
        return merged;
    }
}
