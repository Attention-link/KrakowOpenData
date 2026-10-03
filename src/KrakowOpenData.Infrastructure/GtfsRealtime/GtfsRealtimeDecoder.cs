using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Mobility;
using KrakowOpenData.Infrastructure.Gtfs;

namespace KrakowOpenData.Infrastructure.GtfsRealtime;

/// <summary>Result of decoding one GTFS-Realtime FeedMessage.</summary>
public sealed record GtfsRealtimeFeed(
    DateTimeOffset? Timestamp,
    IReadOnlyList<VehiclePosition> Vehicles,
    IReadOnlyList<TripUpdate> TripUpdates,
    IReadOnlyList<ServiceAlert> Alerts)
{
    public static GtfsRealtimeFeed Empty { get; } = new(null, [], [], []);

    public static GtfsRealtimeFeed Merge(IEnumerable<GtfsRealtimeFeed> feeds)
    {
        var list = feeds.ToList();
        return new GtfsRealtimeFeed(
            list.Select(f => f.Timestamp).Where(t => t.HasValue).DefaultIfEmpty().Max(),
            list.SelectMany(f => f.Vehicles).ToList(),
            list.SelectMany(f => f.TripUpdates).ToList(),
            list.SelectMany(f => f.Alerts).ToList());
    }
}

/// <summary>
/// Decodes GTFS-Realtime protobuf (gtfs-realtime.proto field numbers) into domain records.
/// Ids are prefixed with the feed key so they join with the static GTFS data.
/// </summary>
public static class GtfsRealtimeDecoder
{
    private static readonly string[] Causes =
    [
        "UNKNOWN_CAUSE", "UNKNOWN_CAUSE", "OTHER_CAUSE", "TECHNICAL_PROBLEM", "STRIKE", "DEMONSTRATION",
        "ACCIDENT", "HOLIDAY", "WEATHER", "MAINTENANCE", "CONSTRUCTION", "POLICE_ACTIVITY", "MEDICAL_EMERGENCY"
    ];

    private static readonly string[] Effects =
    [
        "UNKNOWN_EFFECT", "NO_SERVICE", "REDUCED_SERVICE", "SIGNIFICANT_DELAYS", "DETOUR", "ADDITIONAL_SERVICE",
        "MODIFIED_SERVICE", "OTHER_EFFECT", "UNKNOWN_EFFECT", "STOP_MOVED", "NO_EFFECT", "ACCESSIBILITY_ISSUE"
    ];

    public static GtfsRealtimeFeed Decode(byte[] data, string feedKey, string preferredLanguage = "pl")
    {
        var reader = new ProtoReader(data);
        DateTimeOffset? timestamp = null;
        var vehicles = new List<VehiclePosition>();
        var updates = new List<TripUpdate>();
        var alerts = new List<ServiceAlert>();

        while (reader.TryReadTag(out var field, out var wire))
        {
            switch (field)
            {
                case 1 when wire == ProtoReader.WireLengthDelimited: // FeedHeader
                    timestamp = ReadHeaderTimestamp(reader.ReadMessage());
                    break;
                case 2 when wire == ProtoReader.WireLengthDelimited: // FeedEntity
                    ReadEntity(reader.ReadMessage(), feedKey, preferredLanguage, timestamp, vehicles, updates, alerts);
                    break;
                default:
                    reader.SkipField(wire);
                    break;
            }
        }

        return new GtfsRealtimeFeed(timestamp, vehicles, updates, alerts);
    }

    private static DateTimeOffset? ReadHeaderTimestamp(ProtoReader r)
    {
        DateTimeOffset? ts = null;
        while (r.TryReadTag(out var f, out var w))
        {
            if (f == 3 && w == ProtoReader.WireVarint) ts = FromUnix(r.ReadUInt64());
            else r.SkipField(w);
        }

        return ts;
    }

    private static void ReadEntity(
        ProtoReader r, string feedKey, string lang, DateTimeOffset? headerTs,
        List<VehiclePosition> vehicles, List<TripUpdate> updates, List<ServiceAlert> alerts)
    {
        string? entityId = null;
        var isDeleted = false;
        ProtoReader? tripUpdate = null, vehicle = null, alert = null;

        while (r.TryReadTag(out var f, out var w))
        {
            switch (f)
            {
                case 1 when w == ProtoReader.WireLengthDelimited: entityId = r.ReadString(); break;
                case 2 when w == ProtoReader.WireVarint: isDeleted = r.ReadBool(); break;
                case 3 when w == ProtoReader.WireLengthDelimited: tripUpdate = r.ReadMessage(); break;
                case 4 when w == ProtoReader.WireLengthDelimited: vehicle = r.ReadMessage(); break;
                case 5 when w == ProtoReader.WireLengthDelimited: alert = r.ReadMessage(); break;
                default: r.SkipField(w); break;
            }
        }

        if (isDeleted || entityId is null) return;

        if (tripUpdate is not null && ReadTripUpdate(tripUpdate, entityId, feedKey, headerTs) is { } tu) updates.Add(tu);
        if (vehicle is not null && ReadVehicle(vehicle, entityId, feedKey) is { } vp) vehicles.Add(vp);
        if (alert is not null) alerts.Add(ReadAlert(alert, entityId, feedKey, lang));
    }

    private static TripUpdate? ReadTripUpdate(ProtoReader r, string entityId, string feedKey, DateTimeOffset? headerTs)
    {
        TripRef trip = default;
        var stopUpdates = new List<StopTimeUpdate>();
        DateTimeOffset? timestamp = null;
        int? delay = null;

        while (r.TryReadTag(out var f, out var w))
        {
            switch (f)
            {
                case 1 when w == ProtoReader.WireLengthDelimited: trip = ReadTrip(r.ReadMessage()); break;
                case 2 when w == ProtoReader.WireLengthDelimited: stopUpdates.Add(ReadStopTimeUpdate(r.ReadMessage(), feedKey)); break;
                case 4 when w == ProtoReader.WireVarint: timestamp = FromUnix(r.ReadUInt64()); break;
                case 5 when w == ProtoReader.WireVarint: delay = r.ReadInt32(); break;
                default: r.SkipField(w); break;
            }
        }

        if (trip.TripId is null) return null;

        return new TripUpdate(
            GtfsStaticParser.Prefix(feedKey, entityId),
            GtfsStaticParser.Prefix(feedKey, trip.TripId),
            trip.RouteId is null ? null : GtfsStaticParser.Prefix(feedKey, trip.RouteId),
            delay,
            stopUpdates,
            timestamp ?? headerTs,
            feedKey);
    }

    private static StopTimeUpdate ReadStopTimeUpdate(ProtoReader r, string feedKey)
    {
        int? sequence = null;
        string? stopId = null;
        (int? Delay, DateTimeOffset? Time) arrival = default, departure = default;
        var skipped = false;

        while (r.TryReadTag(out var f, out var w))
        {
            switch (f)
            {
                case 1 when w == ProtoReader.WireVarint: sequence = (int)r.ReadUInt32(); break;
                case 2 when w == ProtoReader.WireLengthDelimited: arrival = ReadStopTimeEvent(r.ReadMessage()); break;
                case 3 when w == ProtoReader.WireLengthDelimited: departure = ReadStopTimeEvent(r.ReadMessage()); break;
                case 4 when w == ProtoReader.WireLengthDelimited: stopId = r.ReadString(); break;
                case 5 when w == ProtoReader.WireVarint: skipped = r.ReadInt32() == 1; break; // 1 = SKIPPED
                default: r.SkipField(w); break;
            }
        }

        return new StopTimeUpdate(
            stopId is null ? null : GtfsStaticParser.Prefix(feedKey, stopId),
            sequence,
            arrival.Delay,
            departure.Delay,
            arrival.Time,
            departure.Time,
            skipped);
    }

    private static (int? Delay, DateTimeOffset? Time) ReadStopTimeEvent(ProtoReader r)
    {
        int? delay = null;
        DateTimeOffset? time = null;
        while (r.TryReadTag(out var f, out var w))
        {
            switch (f)
            {
                case 1 when w == ProtoReader.WireVarint: delay = r.ReadInt32(); break;
                case 2 when w == ProtoReader.WireVarint: time = FromUnix((ulong)r.ReadInt64()); break;
                default: r.SkipField(w); break;
            }
        }

        return (delay, time);
    }

    private static VehiclePosition? ReadVehicle(ProtoReader r, string entityId, string feedKey)
    {
        TripRef trip = default;
        float? lat = null, lon = null, bearing = null, speed = null;
        DateTimeOffset? timestamp = null;
        string? stopId = null;
        VehicleRef vehicle = default;

        while (r.TryReadTag(out var f, out var w))
        {
            switch (f)
            {
                case 1 when w == ProtoReader.WireLengthDelimited: trip = ReadTrip(r.ReadMessage()); break;
                case 2 when w == ProtoReader.WireLengthDelimited:
                    var p = r.ReadMessage();
                    while (p.TryReadTag(out var pf, out var pw))
                    {
                        switch (pf)
                        {
                            case 1 when pw == ProtoReader.WireFixed32: lat = p.ReadFloat(); break;
                            case 2 when pw == ProtoReader.WireFixed32: lon = p.ReadFloat(); break;
                            case 3 when pw == ProtoReader.WireFixed32: bearing = p.ReadFloat(); break;
                            case 5 when pw == ProtoReader.WireFixed32: speed = p.ReadFloat(); break;
                            default: p.SkipField(pw); break;
                        }
                    }

                    break;
                case 5 when w == ProtoReader.WireVarint: timestamp = FromUnix(r.ReadUInt64()); break;
                case 7 when w == ProtoReader.WireLengthDelimited: stopId = r.ReadString(); break;
                case 8 when w == ProtoReader.WireLengthDelimited: vehicle = ReadVehicleDescriptor(r.ReadMessage()); break;
                default: r.SkipField(w); break;
            }
        }

        if (lat is null || lon is null) return null;

        return new VehiclePosition(
            GtfsStaticParser.Prefix(feedKey, vehicle.Id ?? entityId),
            trip.TripId is null ? null : GtfsStaticParser.Prefix(feedKey, trip.TripId),
            trip.RouteId is null ? null : GtfsStaticParser.Prefix(feedKey, trip.RouteId),
            new GeoPoint(lat.Value, lon.Value),
            bearing,
            speed,
            vehicle.Label,
            stopId is null ? null : GtfsStaticParser.Prefix(feedKey, stopId),
            timestamp,
            feedKey,
            vehicle.WheelchairAccessible);
    }

    private static ServiceAlert ReadAlert(ProtoReader r, string entityId, string feedKey, string lang)
    {
        var periods = new List<ActivePeriod>();
        var routeIds = new List<string>();
        var stopIds = new List<string>();
        var cause = 1;
        var effect = 8;
        string? url = null, header = null, description = null;

        while (r.TryReadTag(out var f, out var w))
        {
            switch (f)
            {
                case 1 when w == ProtoReader.WireLengthDelimited:
                    var tr = r.ReadMessage();
                    DateTimeOffset? start = null, end = null;
                    while (tr.TryReadTag(out var tf, out var tw))
                    {
                        if (tf == 1 && tw == ProtoReader.WireVarint) start = FromUnix(tr.ReadUInt64());
                        else if (tf == 2 && tw == ProtoReader.WireVarint) end = FromUnix(tr.ReadUInt64());
                        else tr.SkipField(tw);
                    }

                    periods.Add(new ActivePeriod(start, end));
                    break;
                case 5 when w == ProtoReader.WireLengthDelimited:
                    var es = r.ReadMessage();
                    while (es.TryReadTag(out var ef, out var ew))
                    {
                        if (ef == 2 && ew == ProtoReader.WireLengthDelimited) routeIds.Add(GtfsStaticParser.Prefix(feedKey, es.ReadString()));
                        else if (ef == 4 && ew == ProtoReader.WireLengthDelimited)
                        {
                            var t = ReadTrip(es.ReadMessage());
                            if (t.RouteId is not null) routeIds.Add(GtfsStaticParser.Prefix(feedKey, t.RouteId));
                        }
                        else if (ef == 5 && ew == ProtoReader.WireLengthDelimited) stopIds.Add(GtfsStaticParser.Prefix(feedKey, es.ReadString()));
                        else es.SkipField(ew);
                    }

                    break;
                case 6 when w == ProtoReader.WireVarint: cause = r.ReadInt32(); break;
                case 7 when w == ProtoReader.WireVarint: effect = r.ReadInt32(); break;
                case 8 when w == ProtoReader.WireLengthDelimited: url = ReadTranslated(r.ReadMessage(), lang); break;
                case 10 when w == ProtoReader.WireLengthDelimited: header = ReadTranslated(r.ReadMessage(), lang); break;
                case 11 when w == ProtoReader.WireLengthDelimited: description = ReadTranslated(r.ReadMessage(), lang); break;
                default: r.SkipField(w); break;
            }
        }

        return new ServiceAlert(
            GtfsStaticParser.Prefix(feedKey, entityId),
            header ?? description ?? "(no header)",
            description,
            NameOf(Causes, cause, "UNKNOWN_CAUSE"),
            NameOf(Effects, effect, "UNKNOWN_EFFECT"),
            periods,
            routeIds.Distinct().ToList(),
            stopIds.Distinct().ToList(),
            url,
            feedKey);
    }

    /// <summary>Picks the translation in the preferred language, else one without a language, else the first.</summary>
    private static string? ReadTranslated(ProtoReader r, string lang)
    {
        var translations = new List<(string Text, string? Language)>();
        while (r.TryReadTag(out var f, out var w))
        {
            if (f == 1 && w == ProtoReader.WireLengthDelimited)
            {
                var t = r.ReadMessage();
                string? text = null, language = null;
                while (t.TryReadTag(out var tf, out var tw))
                {
                    if (tf == 1 && tw == ProtoReader.WireLengthDelimited) text = t.ReadString();
                    else if (tf == 2 && tw == ProtoReader.WireLengthDelimited) language = t.ReadString();
                    else t.SkipField(tw);
                }

                if (text is not null) translations.Add((text, language));
            }
            else
            {
                r.SkipField(w);
            }
        }

        if (translations.Count == 0) return null;
        return translations.FirstOrDefault(t => string.Equals(t.Language, lang, StringComparison.OrdinalIgnoreCase)).Text
               ?? translations.FirstOrDefault(t => string.IsNullOrEmpty(t.Language)).Text
               ?? translations[0].Text;
    }

    private static TripRef ReadTrip(ProtoReader r)
    {
        string? tripId = null, routeId = null;
        while (r.TryReadTag(out var f, out var w))
        {
            if (f == 1 && w == ProtoReader.WireLengthDelimited) tripId = r.ReadString();
            else if (f == 5 && w == ProtoReader.WireLengthDelimited) routeId = r.ReadString();
            else r.SkipField(w);
        }

        return new TripRef(tripId, routeId);
    }

    private static VehicleRef ReadVehicleDescriptor(ProtoReader r)
    {
        string? id = null, label = null;
        bool? wheelchair = null;
        while (r.TryReadTag(out var f, out var w))
        {
            switch (f)
            {
                case 1 when w == ProtoReader.WireLengthDelimited: id = r.ReadString(); break;
                case 2 when w == ProtoReader.WireLengthDelimited: label = r.ReadString(); break;
                case 4 when w == ProtoReader.WireVarint:
                    wheelchair = r.ReadInt32() switch
                    {
                        2 => true,  // WHEELCHAIR_ACCESSIBLE
                        3 => false, // WHEELCHAIR_INACCESSIBLE
                        _ => null
                    };
                    break;
                default: r.SkipField(w); break;
            }
        }

        return new VehicleRef(id, label, wheelchair);
    }

    private static string NameOf(string[] names, int value, string fallback) =>
        value >= 0 && value < names.Length ? names[value] : fallback;

    private static DateTimeOffset? FromUnix(ulong seconds) =>
        seconds == 0 || seconds > 253402300799UL ? null : DateTimeOffset.FromUnixTimeSeconds((long)seconds);

    private readonly record struct TripRef(string? TripId, string? RouteId);

    private readonly record struct VehicleRef(string? Id, string? Label, bool? WheelchairAccessible);
}
