using System.Globalization;
using System.IO.Compression;
using System.Text;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Mobility;
using KrakowOpenData.Infrastructure.Parsing;

namespace KrakowOpenData.Infrastructure.Gtfs;

/// <summary>
/// Parses a GTFS static feed into a <see cref="GtfsDataset"/>. Every id is prefixed with the
/// feed key ("T:123") so bus, tram and agglomeration feeds can be merged safely.
/// </summary>
public static class GtfsStaticParser
{
    public static GtfsDataset ParseZip(Stream zipStream, string feedKey)
    {
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        return Parse(feedKey, name =>
        {
            var entry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            return entry is null ? null : new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        });
    }

    public static GtfsDataset ParseDirectory(string directory, string feedKey) =>
        Parse(feedKey, name =>
        {
            var path = Path.Combine(directory, name);
            return File.Exists(path) ? new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true) : null;
        });

    /// <param name="feedKey">Prefix for ids.</param>
    /// <param name="openFile">Returns a reader for a GTFS file name, or null if the file is absent.</param>
    public static GtfsDataset Parse(string feedKey, Func<string, TextReader?> openFile)
    {
        var dataset = new GtfsDataset();
        string P(string rawId) => Prefix(feedKey, rawId);

        ForEachRow(openFile, "stops.txt", row =>
        {
            var id = row.Get("stop_id");
            if (id is null) return;
            if (!TryDouble(row.Get("stop_lat"), out var lat) || !TryDouble(row.Get("stop_lon"), out var lon)) return;

            dataset.Stops[P(id)] = new TransitStop(
                P(id),
                row.Get("stop_code"),
                row.Get("stop_name") ?? id,
                new GeoPoint(lat, lon),
                feedKey,
                ParseAccessibility(row.Get("wheelchair_boarding")));
        });

        ForEachRow(openFile, "routes.txt", row =>
        {
            var id = row.Get("route_id");
            if (id is null) return;
            var type = int.TryParse(row.Get("route_type"), out var t) ? t : -1;
            var color = row.Get("route_color");

            dataset.Routes[P(id)] = new TransitRoute(
                P(id),
                row.Get("route_short_name") ?? row.Get("route_long_name") ?? id,
                row.Get("route_long_name"),
                TransportModeExtensions.FromGtfsRouteType(type),
                color is null ? null : "#" + color.TrimStart('#'),
                feedKey);
        });

        ForEachRow(openFile, "trips.txt", row =>
        {
            var id = row.Get("trip_id");
            var routeId = row.Get("route_id");
            var serviceId = row.Get("service_id");
            if (id is null || routeId is null || serviceId is null) return;

            dataset.Trips[P(id)] = new TransitTrip(
                P(id),
                P(routeId),
                P(serviceId),
                row.Get("trip_headsign"),
                int.TryParse(row.Get("direction_id"), out var dir) ? dir : null,
                feedKey,
                ParseAccessibility(row.Get("wheelchair_accessible")));
        });

        ForEachRow(openFile, "stop_times.txt", row =>
        {
            var tripId = row.Get("trip_id");
            var stopId = row.Get("stop_id");
            if (tripId is null || stopId is null) return;

            var arrivalOk = GtfsTime.TryParse(row.Get("arrival_time"), out var arrival);
            var departureOk = GtfsTime.TryParse(row.Get("departure_time"), out var departure);
            if (!arrivalOk && !departureOk) return; // untimed stop: skip in this scaffold
            if (!arrivalOk) arrival = departure;
            if (!departureOk) departure = arrival;

            dataset.AddStopTime(new StopTime(
                P(tripId),
                P(stopId),
                int.TryParse(row.Get("stop_sequence"), out var seq) ? seq : 0,
                arrival,
                departure));
        });

        ForEachRow(openFile, "calendar.txt", row =>
        {
            var serviceId = row.Get("service_id");
            if (serviceId is null) return;
            if (!TryDate(row.Get("start_date"), out var start) || !TryDate(row.Get("end_date"), out var end)) return;

            var days = new HashSet<DayOfWeek>();
            void Day(string column, DayOfWeek day)
            {
                if (row.Get(column) == "1") days.Add(day);
            }

            Day("monday", DayOfWeek.Monday);
            Day("tuesday", DayOfWeek.Tuesday);
            Day("wednesday", DayOfWeek.Wednesday);
            Day("thursday", DayOfWeek.Thursday);
            Day("friday", DayOfWeek.Friday);
            Day("saturday", DayOfWeek.Saturday);
            Day("sunday", DayOfWeek.Sunday);

            dataset.Calendars[P(serviceId)] = new ServiceCalendar(P(serviceId), days, start, end);
        });

        ForEachRow(openFile, "calendar_dates.txt", row =>
        {
            var serviceId = row.Get("service_id");
            if (serviceId is null || !TryDate(row.Get("date"), out var date)) return;
            var type = row.Get("exception_type");
            if (type is not ("1" or "2")) return;

            dataset.AddException(new ServiceException(P(serviceId), date, type == "1"));
        });

        dataset.Seal();
        return dataset;
    }

    public static string Prefix(string feedKey, string rawId) =>
        string.IsNullOrEmpty(feedKey) ? rawId : $"{feedKey}:{rawId}";

    private static void ForEachRow(Func<string, TextReader?> openFile, string fileName, Action<CsvRow> handle)
    {
        using var reader = openFile(fileName);
        if (reader is null) return;
        foreach (var row in CsvReader.Read(reader)) handle(row);
    }

    /// <summary>GTFS accessibility: 1 = yes, 2 = no, 0/empty = unknown.</summary>
    internal static bool? ParseAccessibility(string? value) => value switch
    {
        "1" => true,
        "2" => false,
        _ => null
    };

    private static bool TryDouble(string? value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private static bool TryDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}

/// <summary>GTFS "HH:MM:SS" times, where hours may be 24 or more for after-midnight service.</summary>
public static class GtfsTime
{
    public static bool TryParse(string? value, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var parts = value.Trim().Split(':');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var h) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var m) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var s))
        {
            return false;
        }

        if (m > 59 || s > 59 || h > 47) return false;

        time = new TimeSpan(h, m, s);
        return true;
    }
}
