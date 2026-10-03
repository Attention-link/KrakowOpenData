using KrakowOpenData.Infrastructure.GtfsRealtime;

namespace KrakowOpenData.Infrastructure.Tests;

public class GtfsRealtimeDecoderTests
{
    private const long Timestamp = 1_790_000_000; // 2026-09-21

    private static ProtoWriter Header(ProtoWriter w) =>
        w.Message(1, h => h.String(1, "2.0").Varint(3, Timestamp));

    [Fact]
    public void Decodes_vehicle_positions_with_prefixed_ids()
    {
        var bytes = Header(new ProtoWriter())
            .Message(2, e => e
                .String(1, "entity-1")
                .Message(4, v => v
                    .Message(1, t => t.String(1, "trip-7").String(5, "52"))
                    .Message(2, p => p.Float(1, 50.0617f).Float(2, 19.9373f).Float(3, 90f).Float(5, 8.5f))
                    .Varint(5, Timestamp)
                    .String(7, "stop-3")
                    .Message(8, d => d.String(1, "veh-1").String(2, "RZ123").Varint(4, 2))))
            .ToArray();

        var feed = GtfsRealtimeDecoder.Decode(bytes, "T");

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(Timestamp), feed.Timestamp);
        var v = Assert.Single(feed.Vehicles);
        Assert.Equal("T:veh-1", v.Id);
        Assert.Equal("T:trip-7", v.TripId);
        Assert.Equal("T:52", v.RouteId);
        Assert.Equal("T:stop-3", v.StopId);
        Assert.Equal("RZ123", v.VehicleLabel);
        Assert.True(v.WheelchairAccessible);
        Assert.Equal(50.0617, v.Location.Latitude, precision: 4);
        Assert.Equal(19.9373, v.Location.Longitude, precision: 4);
        Assert.Equal(8.5f, v.SpeedMetersPerSecond);
    }

    [Fact]
    public void Decodes_trip_updates_including_negative_delays()
    {
        var bytes = Header(new ProtoWriter())
            .Message(2, e => e
                .String(1, "tu-1")
                .Message(3, tu => tu
                    .Message(1, t => t.String(1, "trip-9").String(5, "179"))
                    .Message(2, s => s.Varint(1, 4).String(4, "stop-A").Message(3, ev => ev.Varint(1, -45)))
                    .Message(2, s => s.Varint(1, 5).String(4, "stop-B").Varint(5, 1)) // SKIPPED
                    .Varint(5, 120)))
            .ToArray();

        var feed = GtfsRealtimeDecoder.Decode(bytes, "A");

        var tu = Assert.Single(feed.TripUpdates);
        Assert.Equal("A:trip-9", tu.TripId);
        Assert.Equal("A:179", tu.RouteId);
        Assert.Equal(120, tu.DelaySeconds);
        Assert.Equal(2, tu.StopTimeUpdates.Count);
        Assert.Equal(-45, tu.StopTimeUpdates[0].DepartureDelaySeconds);
        Assert.Equal("A:stop-A", tu.StopTimeUpdates[0].StopId);
        Assert.True(tu.StopTimeUpdates[1].Skipped);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(Timestamp), tu.Timestamp); // falls back to header
    }

    [Fact]
    public void Decodes_alerts_and_prefers_polish_translation()
    {
        var bytes = Header(new ProtoWriter())
            .Message(2, e => e
                .String(1, "al-1")
                .Message(5, a => a
                    .Message(1, p => p.Varint(1, Timestamp).Varint(2, Timestamp + 3600))
                    .Message(5, s => s.String(2, "52"))
                    .Message(5, s => s.String(5, "stop-X"))
                    .Varint(6, 10) // CONSTRUCTION
                    .Varint(7, 4)  // DETOUR
                    .Message(10, ts => ts
                        .Message(1, t => t.String(1, "Detour").String(2, "en"))
                        .Message(1, t => t.String(1, "Objazd").String(2, "pl")))
                    .Message(11, ts => ts.Message(1, t => t.String(1, "Tramwaje jadą objazdem.")))))
            .ToArray();

        var alert = Assert.Single(GtfsRealtimeDecoder.Decode(bytes, "T").Alerts);

        Assert.Equal("T:al-1", alert.Id);
        Assert.Equal("Objazd", alert.Header);
        Assert.Equal("Tramwaje jadą objazdem.", alert.Description);
        Assert.Equal("CONSTRUCTION", alert.Cause);
        Assert.Equal("DETOUR", alert.Effect);
        Assert.Equal(new[] { "T:52" }, alert.RouteIds.ToArray());
        Assert.Equal(new[] { "T:stop-X" }, alert.StopIds.ToArray());
        Assert.True(alert.IsActiveAt(DateTimeOffset.FromUnixTimeSeconds(Timestamp + 60)));
        Assert.False(alert.IsActiveAt(DateTimeOffset.FromUnixTimeSeconds(Timestamp + 7200)));
    }

    [Fact]
    public void Unknown_fields_and_deleted_entities_are_ignored()
    {
        var bytes = Header(new ProtoWriter())
            .Varint(99, 12345)               // unknown top-level field
            .Fixed64(98, 1.5)                // unknown 64-bit field
            .Message(2, e => e.String(1, "gone").Varint(2, 1)
                .Message(4, v => v.Message(2, p => p.Float(1, 50f).Float(2, 19f))))
            .Message(2, e => e.String(1, "kept").String(42, "extension")
                .Message(4, v => v.Message(2, p => p.Float(1, 50f).Float(2, 19f))))
            .ToArray();

        var feed = GtfsRealtimeDecoder.Decode(bytes, "T");

        Assert.Equal("T:kept", Assert.Single(feed.Vehicles).Id);
    }

    [Fact]
    public void Vehicle_without_position_is_dropped()
    {
        var bytes = Header(new ProtoWriter())
            .Message(2, e => e.String(1, "x").Message(4, v => v.Message(1, t => t.String(1, "trip"))))
            .ToArray();

        Assert.Empty(GtfsRealtimeDecoder.Decode(bytes, "T").Vehicles);
    }

    [Fact]
    public void Truncated_data_throws_InvalidDataException()
    {
        var bytes = Header(new ProtoWriter()).ToArray();
        Assert.Throws<InvalidDataException>(() => GtfsRealtimeDecoder.Decode(bytes[..^1], "T"));
    }

    [Fact]
    public void Empty_payload_gives_empty_feed()
    {
        var feed = GtfsRealtimeDecoder.Decode([], "T");
        Assert.Empty(feed.Vehicles);
        Assert.Null(feed.Timestamp);
    }

    [Fact]
    public void Merge_concatenates_feeds_and_keeps_latest_timestamp()
    {
        var a = new GtfsRealtimeFeed(DateTimeOffset.FromUnixTimeSeconds(100), [], [], []);
        var b = new GtfsRealtimeFeed(DateTimeOffset.FromUnixTimeSeconds(200), [], [], []);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(200), GtfsRealtimeFeed.Merge([a, b, GtfsRealtimeFeed.Empty]).Timestamp);
    }
}
