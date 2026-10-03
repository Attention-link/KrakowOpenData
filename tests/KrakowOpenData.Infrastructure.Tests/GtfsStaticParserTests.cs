using KrakowOpenData.Domain.Mobility;
using KrakowOpenData.Infrastructure.Gtfs;

namespace KrakowOpenData.Infrastructure.Tests;

public class GtfsStaticParserTests
{
    private static GtfsDataset ParseSample(string feedKey = "T") => GtfsStaticParser.ParseZip(ZipBuilder.Build(
        ("stops.txt", "stop_id,stop_code,stop_name,stop_lat,stop_lon,wheelchair_boarding\n" +
                      "100,01,Teatr Bagatela,50.0632,19.9326,1\n" +
                      "101,02,\"Plac Wszystkich Świętych\",50.0585,19.9386,2\n" +
                      "bad,03,No coordinates,,,\n"),
        ("routes.txt", "route_id,route_short_name,route_long_name,route_type,route_color\n" +
                       "r52,52,,0,1F6FEB\n" +
                       "r179,179,,3,\n"),
        ("trips.txt", "route_id,service_id,trip_id,trip_headsign,direction_id,wheelchair_accessible\n" +
                      "r52,wk,t1,Czerwone Maki P+R,0,1\n" +
                      "r52,wk,t2,Os. Piastów,1,\n"),
        ("stop_times.txt", "trip_id,arrival_time,departure_time,stop_id,stop_sequence\n" +
                           "t1,08:12:00,08:12:30,101,2\n" +
                           "t1,08:10:00,08:10:00,100,1\n" +
                           "t2,24:05:00,24:05:00,100,1\n"),
        ("calendar.txt", "service_id,monday,tuesday,wednesday,thursday,friday,saturday,sunday,start_date,end_date\n" +
                         "wk,1,1,1,1,1,0,0,20260901,20261231\n"),
        ("calendar_dates.txt", "service_id,date,exception_type\n" +
                               "wk,20261111,2\n")), feedKey);

    [Fact]
    public void Ids_are_prefixed_with_the_feed_key()
    {
        var d = ParseSample();
        Assert.Contains("T:100", d.Stops.Keys);
        Assert.Contains("T:r52", d.Routes.Keys);
        Assert.Equal("T:r52", d.Trips["T:t1"].RouteId);
        Assert.Equal("T:wk", d.Trips["T:t1"].ServiceId);
    }

    [Fact]
    public void Stops_without_coordinates_are_skipped_and_accessibility_is_mapped()
    {
        var d = ParseSample();
        Assert.Equal(2, d.Stops.Count);
        Assert.True(d.Stops["T:100"].WheelchairAccessible);
        Assert.False(d.Stops["T:101"].WheelchairAccessible);
        Assert.Equal("Plac Wszystkich Świętych", d.Stops["T:101"].Name);
    }

    [Fact]
    public void Routes_get_mode_and_hash_prefixed_colour()
    {
        var d = ParseSample();
        Assert.Equal(TransportMode.Tram, d.Routes["T:r52"].Mode);
        Assert.Equal("#1F6FEB", d.Routes["T:r52"].Color);
        Assert.Equal(TransportMode.Bus, d.Routes["T:r179"].Mode);
        Assert.Null(d.Routes["T:r179"].Color);
    }

    [Fact]
    public void Stop_times_are_indexed_and_sorted()
    {
        var d = ParseSample();
        Assert.Equal(3, d.StopTimeCount);

        var trip = d.StopTimesByTrip["T:t1"];
        Assert.Equal(new[] { 1, 2 }, trip.Select(s => s.StopSequence).ToArray());

        var atBagatela = d.StopTimesByStop["T:100"];
        Assert.Equal(new TimeSpan(8, 10, 0), atBagatela[0].Departure);
        Assert.Equal(new TimeSpan(24, 5, 0), atBagatela[1].Departure);
    }

    [Fact]
    public void Calendar_and_exceptions_decide_active_days()
    {
        var d = ParseSample();
        Assert.True(d.IsServiceActive("T:wk", new DateOnly(2026, 10, 2)));   // Friday
        Assert.False(d.IsServiceActive("T:wk", new DateOnly(2026, 10, 3)));  // Saturday
        Assert.False(d.IsServiceActive("T:wk", new DateOnly(2026, 11, 11))); // removed (holiday)
    }

    [Fact]
    public void Merge_combines_feeds_without_id_collisions()
    {
        var merged = GtfsDataset.Merge([ParseSample("A"), ParseSample("T")]);
        Assert.Equal(4, merged.Stops.Count);
        Assert.Equal(6, merged.StopTimeCount);
        Assert.Equal(2, merged.StopTimesByStop["A:100"].Count);
    }

    [Fact]
    public void Missing_optional_files_are_tolerated()
    {
        var d = GtfsStaticParser.ParseZip(ZipBuilder.Build(
            ("stops.txt", "stop_id,stop_name,stop_lat,stop_lon\n1,X,50.06,19.93\n")), "A");
        Assert.Single(d.Stops);
        Assert.Empty(d.Trips);
    }

    [Theory]
    [InlineData("08:05:00", 8, 5, 0)]
    [InlineData("25:10:30", 25, 10, 30)]
    [InlineData(" 7:00:00 ", 7, 0, 0)]
    public void GtfsTime_parses_hours_past_midnight(string input, int h, int m, int s)
    {
        Assert.True(GtfsTime.TryParse(input, out var time));
        Assert.Equal(new TimeSpan(h, m, s), time);
    }

    [Theory]
    [InlineData("")]
    [InlineData("8:60:00")]
    [InlineData("abc")]
    [InlineData("08:00")]
    public void GtfsTime_rejects_invalid_values(string input)
    {
        Assert.False(GtfsTime.TryParse(input, out _));
    }
}
